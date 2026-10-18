using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;
using JitenMPV.Core.MediaInfo;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Tracking;

public sealed class EpisodeTracker(
    IMediaDeckApi api,
    DeckResolver resolver,
    MediaMap map,
    PendingMarks pending,
    PluginSettings settings,
    ILogger logger,
    Func<string, bool, CancellationToken, Task> notify,
    IEpisodePickerPresenter? picker = null)
{
    /// End of file forgives this much of the threshold: the last poll before it and a skipped
    /// opening should not cost an episode that was watched to the end.
    private const double EndOfFileSlack = 0.1;

    private volatile PluginSettings _settings = settings;
    private readonly WatchTimeAccumulator _watch = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lock _lock = new();

    private MediaIdentity? _identity;
    private DeckMatch? _match;
    private double? _duration;
    private bool _marked;
    private int _fileVersion;
    private CancellationTokenSource? _fileCts;
    private Task _writes = Task.CompletedTask;

    public void UpdateSettings(PluginSettings newSettings)
    {
        var wasEnabled = _settings.EpisodeTrackingEnabled;
        _settings = newSettings;
        if (wasEnabled || !newSettings.EpisodeTrackingEnabled) return;

        MediaIdentity? identity;
        int version;
        CancellationToken token;
        lock (_lock)
        {
            identity = _identity;
            version = _fileVersion;
            token = _fileCts?.Token ?? CancellationToken.None;
        }
        if (identity is not null && _match is null)
            _ = TaskHelper.RunSafe(() => ResolveAsync(identity, version, token), logger, "Resolve episode");
    }

    public bool IsTracking => _identity is not null;

    public DeckMatch? CurrentMatch => _match;

    public async Task OnFileLoadedAsync(MediaIdentity? identity, CancellationToken ct)
    {
        CancellationToken token;
        int version;
        lock (_lock)
        {
            TaskHelper.CancelAndDispose(ref _fileCts);
            _watch.Reset();
            _match = null;
            _duration = null;
            _marked = false;
            _identity = identity is null || identity.IsExtra || (identity.IsStream && identity.Episode is null)
                ? null
                : identity;
            _fileCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            token = _fileCts.Token;
            version = ++_fileVersion;
            if (_identity is null || !_settings.EpisodeTrackingEnabled) return;
        }

        await ResolveAsync(_identity, version, token);
    }

    public void OnPlayback(double position, double? duration, double speed, double wallSeconds)
    {
        lock (_lock)
        {
            if (_identity is null) return;
            _watch.Sample(position, wallSeconds, speed);
            if (duration > 0) _duration = duration;
        }
        if (ReachedThreshold(0)) QueueMark();
    }

    public void OnEndFile(bool eof)
    {
        if (eof && ReachedThreshold(EndOfFileSlack)) QueueMark();
    }

    private bool ReachedThreshold(double slack)
    {
        var threshold = Math.Clamp(_settings.EpisodeTrackingThresholdPercent, 10, 100) / 100.0 - slack;
        return _duration is > 0 and var duration && _watch.Watched >= threshold * duration;
    }

    /// Stored before it is sent and run behind the previous write, so marks never interleave and
    /// one cut short by mpv quitting is still on disk for the next start.
    private void QueueMark()
    {
        EpisodeMark mark;
        lock (_lock)
        {
            if (_marked || !_settings.EpisodeTrackingEnabled) return;
            if (_match is not { Kind: DeckMatchKind.Matched, Deck: { } deck } match) return;
            if (!match.IsMovie && match.Episode is null) return;

            _marked = true;
            mark = new EpisodeMark(
                deck.DeckId, match.IsMovie ? null : match.Episode, deck.DisplayTitle,
                _settings.EpisodeTrackingMarkEarlier, _settings.EpisodeTrackingMarkShowCompleted,
                _settings.EpisodeTrackingKeepShowStatus, null);
            pending.Add(mark);
            _writes = WriteQueuedAsync(_writes, mark);
        }
    }

    private async Task WriteQueuedAsync(Task previous, EpisodeMark mark)
    {
        try { await previous; } catch { }

        try
        {
            var changed = await WriteAsync(mark, CancellationToken.None);
            logger.LogInformation("Episode tracking: {Title} episode {Episode} {Outcome}",
                mark.Title, mark.Episode, changed ? "marked watched" : "was already watched");
            if (changed)
                await LogAsync(mark.Episode is { } n
                    ? $"Jiten: marked Ep {n} watched"
                    : $"Jiten: marked {mark.Title} watched");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not mark {Title} episode {Episode} watched", mark.Title, mark.Episode);
            await ShowErrorAsync("Jiten: could not mark this episode watched, will retry next time");
        }
    }

    public async Task RetryPendingAsync(CancellationToken ct)
    {
        foreach (var mark in pending.All())
        {
            try
            {
                await WriteAsync(mark, ct);
                logger.LogInformation("Retried pending mark for {Title} episode {Episode}", mark.Title, mark.Episode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Pending mark for {Title} episode {Episode} still fails", mark.Title, mark.Episode);
            }
        }
    }

    /// Lets a write started by the end of the file finish before the process exits.
    public async Task FlushAsync(TimeSpan timeout)
    {
        Task writes;
        lock (_lock) writes = _writes;
        try { await writes.WaitAsync(timeout); }
        catch (TimeoutException) { }
    }

    /// An episode already completed is a rewatch: nothing is written, not even earlier episodes.
    private async Task<bool> WriteAsync(EpisodeMark mark, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            if (mark.Episode is not { } number)
            {
                var movieChanged = await api.SetDeckStatusAsync(mark.DeckId, DeckStatus.Completed, ct);
                pending.Remove(mark);
                return movieChanged;
            }

            var episodes = await api.GetEpisodesAsync(mark.DeckId, ct);
            var target = episodes.Find(number)
                         ?? throw new InvalidDataException($"{mark.Title} has no deck titled Episode {number}");
            if (await api.GetDeckStatusAsync(target.DeckId, ct) == DeckStatus.Completed)
            {
                pending.Remove(mark);
                return false;
            }

            // Recorded before the first write, so a retry after a partial failure restores the
            // status the show had, not the one the failed attempt promoted it to.
            if (mark.KeepShowStatus && mark.ShowStatusBefore is null)
            {
                mark = mark with { ShowStatusBefore = await api.GetDeckStatusAsync(mark.DeckId, ct) };
                pending.Add(mark);
            }

            if (mark.MarkEarlier)
                foreach (var earlier in episodes.Episodes
                             .Where(e => e.Number < number && e.Status != DeckStatus.Completed)
                             .OrderBy(e => e.Number))
                    await api.SetDeckStatusAsync(earlier.DeckId, DeckStatus.Completed, ct);

            await api.SetDeckStatusAsync(target.DeckId, DeckStatus.Completed, ct);

            if (mark.MarkShowCompleted && number == episodes.LastNumber)
                await api.SetDeckStatusAsync(mark.DeckId, DeckStatus.Completed, ct);
            else if (mark.ShowStatusBefore is { } before)
                await api.SetDeckStatusAsync(mark.DeckId, before, ct);

            pending.Remove(mark);
            return true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task OpenPickerAsync(CancellationToken ct)
    {
        var identity = _identity;
        if (identity is null || picker is null)
        {
            await ShowErrorAsync("Jiten: nothing to track in this file");
            return;
        }

        var match = _match;
        var candidates = match?.Candidates.ToList() ?? [];
        if (match?.Deck is { } current && candidates.All(c => c.Deck.DeckId != current.DeckId))
            candidates.Insert(0, new DeckCandidate(current, 1));

        var data = new EpisodePickerData(
            identity.Title ?? identity.TitleSource ?? "",
            match?.Episode ?? identity.Episode,
            match?.Deck?.DeckId,
            candidates,
            resolver.SearchAsync);

        if (await picker.ShowAsync(data, ct) is not { } result) return;

        var key = MediaMap.KeyFor(identity);
        if (key is null)
        {
            await ShowErrorAsync("Jiten: this file has no title to remember a choice by");
            return;
        }

        if (result.IgnoreFolder)
        {
            map.Set(key, new MediaMapEntry { Ignore = true, Title = identity.Title });
            lock (_lock) _match = new DeckMatch(DeckMatchKind.Ignored, null, null, []);
            await LogAsync("Jiten: not tracking this folder");
            return;
        }

        if (result.Deck is not { } deck) return;
        var offset = result.Episode is { } episode ? episode - (identity.Episode ?? 0) : 0;
        map.Set(key, new MediaMapEntry { DeckId = deck.DeckId, EpisodeOffset = offset, Title = deck.DisplayTitle });

        int version;
        CancellationToken token;
        lock (_lock)
        {
            if (_identity != identity) return;
            version = _fileVersion;
            token = _fileCts?.Token ?? ct;
        }
        await ResolveAsync(identity, version, token);
        if (ReachedThreshold(0)) QueueMark();
    }

    private async Task ResolveAsync(MediaIdentity identity, int version, CancellationToken ct)
    {
        DeckMatch match;
        try
        {
            match = await resolver.ResolveAsync(identity, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not look up {Title} on Jiten", identity.Title);
            return;
        }

        lock (_lock)
        {
            if (version != _fileVersion || _identity != identity) return;
            _match = match;
        }

        logger.LogInformation("Episode tracking: {Title} ep {Episode} -> {Kind} deck {DeckId} ep {DeckEpisode}",
            identity.Title, identity.Episode, match.Kind, match.Deck?.DeckId, match.Episode);
        var key = _settings.KeybindEpisodePicker;
        var choose = string.IsNullOrWhiteSpace(key) ? "" : $", press {key} to choose";
        switch (match)
        {
            case { Kind: DeckMatchKind.Ignored }:
                break;
            case { Kind: DeckMatchKind.Matched, Deck: { } deck, IsMovie: true }:
                await LogAsync($"Jiten: {deck.DisplayTitle}");
                break;
            case { Kind: DeckMatchKind.Matched, Deck: { } deck, Episode: { } n }:
                await LogAsync($"Jiten: {deck.DisplayTitle} · Ep {n}");
                break;
            // Nothing gets tracked until the user picks, so these stay on screen.
            case { Kind: DeckMatchKind.Matched, Deck: { } deck }:
                await ShowErrorAsync($"Jiten: {deck.DisplayTitle} · episode unknown{choose}");
                break;
            default:
                await ShowErrorAsync($"Jiten: no sure match for {identity.Title}{choose}");
                break;
        }
    }

    private Task LogAsync(string message) => NotifyAsync(message, onScreen: false);

    private Task ShowErrorAsync(string message) => NotifyAsync(message, onScreen: true);

    private async Task NotifyAsync(string message, bool onScreen)
    {
        try { await notify(message, onScreen, CancellationToken.None); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not show episode tracking notice");
        }
    }
}
