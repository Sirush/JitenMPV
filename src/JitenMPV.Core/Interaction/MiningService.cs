using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Media;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Plugin;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Interaction;

public sealed class MiningService(
    JitenApiClient api,
    ParseCache cache,
    StatusOverlay status,
    PluginSettings settings,
    ILogger logger,
    MediaCaptureCoordinator? media = null)
{
    private volatile PluginSettings _settings = settings;
    private volatile IReadOnlyList<StudyDeckListItem> _decks = [];

    /// Word lists only for mining
    private volatile IReadOnlyList<StudyDeckListItem> _wordListDecks = [];

    /// Ids of static word lists only. Media and frequency decks also appear in a word's
    /// studyDeckIds and would otherwise mark nearly every word as already mined.
    private volatile HashSet<int> _wordListIds = [];

    /// Tracked per deck: the same word can legitimately be mined into more than one list.
    private readonly Lock _minedLock = new();
    private readonly HashSet<(int WordId, byte ReadingIndex, int DeckId)> _mined = [];

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    /// Active decks for membership display only
    public IReadOnlyList<StudyDeckListItem> Decks => _decks;

    public IReadOnlyList<StudyDeckListItem> WordListDecks => _wordListDecks;

    public bool IsMinedTo(int wordId, byte readingIndex, int deckId)
    {
        lock (_minedLock)
            return _mined.Contains((wordId, readingIndex, deckId));
    }

    private bool IsMinedAnywhere(int wordId, byte readingIndex)
    {
        lock (_minedLock)
            return _mined.Any(m => m.WordId == wordId && m.ReadingIndex == readingIndex);
    }

    /// The word's decks from the parse response, plus anything mined this session — the cached
    /// parse response predates those mines, so the popup would otherwise show stale membership.
    public IReadOnlyList<int> EffectiveDeckIds(
        int wordId, byte readingIndex, IReadOnlyList<int> studyDeckIds)
    {
        lock (_minedLock)
        {
            var mined = _mined
                .Where(m => m.WordId == wordId && m.ReadingIndex == readingIndex)
                .Select(m => m.DeckId)
                .Where(id => !studyDeckIds.Contains(id))
                .ToList();

            if (mined.Count == 0) return studyDeckIds;
            return [..studyDeckIds, ..mined];
        }
    }

    /// True when mining this word again would be a no-op: already in the deck we would mine into.
    /// Falls back to "any word list" when no target deck is set.
    public bool IsInTargetList(int wordId, byte readingIndex, IReadOnlyList<int> studyDeckIds)
    {
        var deckIds = EffectiveDeckIds(wordId, readingIndex, studyDeckIds);

        if (ResolveTargetDeck() is { } targetDeck)
            return deckIds.Contains(targetDeck);

        if (deckIds.Count == 0) return false;

        var wordLists = _wordListIds;
        return wordLists.Count > 0 && deckIds.Any(wordLists.Contains);
    }

    public async Task RefreshDecksAsync(CancellationToken ct)
    {
        try
        {
            var decks = await api.GetStudyDecksAsync(ct);
            var wordLists = decks.Where(d => d.DeckType == StudyDeckType.StaticWordList).ToList();
            _decks = decks;
            _wordListDecks = wordLists;
            _wordListIds = [..wordLists.Select(d => d.UserStudyDeckId)];
            if (media is not null)
                media.DeckOptions = [..wordLists.Select(d => new MiningDeckOption(d.UserStudyDeckId, d.Name))];

            logger.LogInformation("Loaded {Count} study decks ({Lists} word lists)",
                decks.Count, wordLists.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load study decks");
        }
    }

    /// Resolves the deck to mine into without prompting, or null when the popup should ask.
    public int? ResolveTargetDeck()
    {
        var s = _settings;
        if (!s.MiningToStudyDeck) return null;
        return s.MiningStudyDeckId is > 0 ? s.MiningStudyDeckId : null;
    }

    /// The deck for callers that cannot offer the popup's picker — a keybind, a double click, a
    /// mine chained off a grade. MiningToStudyDeck only governs whether the popup asks, so the
    /// selected list is still the target for everything that has no way of asking.
    public int? ResolveDeckWithoutPicker()
        => ResolveTargetDeck() ?? (_settings.MiningStudyDeckId is > 0 ? _settings.MiningStudyDeckId : null);

    /// Mines into the configured deck, reporting to the OSD when none is set.
    public async Task<bool> MineWithConfiguredDeckAsync(
        int wordId, byte readingIndex, string? subtitleText, MpvIpcClient ipc, CancellationToken ct)
    {
        if (!_settings.MiningEnabled) return false;

        if (ResolveDeckWithoutPicker() is not { } deckId)
        {
            await status.ShowAsync(ipc, "No target word list selected (Ctrl+J > Features > Mining > Target word list)", 2500, ct);
            return false;
        }
        return await MineAsync(wordId, readingIndex, deckId, subtitleText, ipc, ct);
    }

    /// <param name="reportSkip">
    /// False for mining triggered as a side effect of another action, where a "already in deck"
    /// notice would talk over the feedback for the action the user actually asked for.
    /// </param>
    public async Task<bool> MineAsync(
        int wordId, byte readingIndex, int deckId,
        string? subtitleText, MpvIpcClient ipc, CancellationToken ct, bool reportSkip = true)
    {
        var s = _settings;
        if (!s.MiningEnabled) return false;

        var entry = subtitleText is not null ? cache.GetOrDefault(subtitleText) : null;
        var word = entry?.VocabDetails.GetValueOrDefault((wordId, readingIndex));
        var spelling = word?.Spelling ?? "word";

        // A redundant card is covered by another card and cannot be mined, mirroring the Reader.
        var state = entry?.VocabStates.GetValueOrDefault((wordId, readingIndex));
        if (state == KnownState.Redundant)
        {
            await status.ShowAsync(ipc, $"{spelling}: redundant, not mined", 2000, ct);
            return false;
        }

        var alreadyInDeck = s.MiningSkipIfPresent && IsAlreadyInDeck(wordId, readingIndex, deckId, word);

        var surfaceForm = ResolveSurfaceForm(entry, subtitleText, wordId, readingIndex);
        string? sentence = null;
        string? source = null;

        if (s.MiningCaptureSentence && subtitleText is not null && entry is not null)
        {
            sentence = SentenceFormatter.WithMarkers(subtitleText, surfaceForm);
            source = await GetMediaTitleAsync(ipc, ct);
        }

        // Capture runs even when the deck add is skipped: re-mining an existing card is how a user
        // replaces a bad screenshot or a clipped audio sample.
        var capture = media is not null
            ? await media.CaptureAndConfirmAsync(new MediaCaptureRequest(
                wordId, readingIndex, surfaceForm, subtitleText, spelling,
                word?.Reading ?? "", deckId), ipc, ct)
            : MediaCaptureResult.Disabled;

        if (capture.Cancelled) return false;
        if (capture.Sentence is not null) sentence = capture.Sentence;
        if (capture.DeckId is { } chosenDeck) deckId = chosenDeck;

        await ReportCaptureGateAsync(capture, ipc, ct);

        if (alreadyInDeck && !capture.HasUploads)
        {
            if (reportSkip)
            {
                var target = DeckName(deckId);
                await status.ShowAsync(ipc,
                    target is not null ? $"{spelling}: already in {target}" : $"{spelling}: already mined",
                    2000, ct);
            }
            return false;
        }

        try
        {
            if (!alreadyInDeck)
                await api.AddToStudyDeckAsync(deckId, wordId, readingIndex, sentence, source, ct);

            lock (_minedLock)
                _mined.Add((wordId, readingIndex, deckId));

            var upload = capture.HasUploads && media is not null
                ? await media.UploadAsync(wordId, readingIndex, capture, ct)
                : null;

            await status.ShowAsync(ipc,
                BuildStatusMessage(spelling, DeckName(deckId), alreadyInDeck, capture, upload), 2500, ct);

            logger.LogInformation("Mined word {WordId}:{ReadingIndex} into deck {DeckId}",
                wordId, readingIndex, deckId);
            return true;
        }
        catch (JitenApiKeyRejectedException)
        {
            await status.ShowAsync(ipc, "API key rejected", 3000, ct);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to mine word {WordId}:{ReadingIndex}", wordId, readingIndex);
            await status.ShowAsync(ipc, $"{spelling}: mining failed", 2000, ct);
            return false;
        }
    }

    private static string? ResolveSurfaceForm(
        ParseCacheEntry? entry, string? subtitleText, int wordId, byte readingIndex)
    {
        if (entry is null || subtitleText is null) return null;

        var token = entry.Tokens.Find(t => t.WordId == wordId && t.ReadingIndex == readingIndex);
        return token is not null && token.Start + token.Length <= subtitleText.Length
            ? subtitleText.Substring(token.Start, token.Length)
            : null;
    }

    /// One notice per session for the two gates a user can act on; everything else stays silent so
    /// the feedback for the mine they asked for is not talked over.
    private async Task ReportCaptureGateAsync(
        MediaCaptureResult capture, MpvIpcClient ipc, CancellationToken ct)
    {
        if (media is null) return;

        if (capture.Outcome == MediaCaptureOutcome.NotEntitled && media.ShouldReportNotEntitled())
            await status.ShowAsync(ipc, "No Jiten+, so nothing was attached", 3000, ct);
        else if ((capture.Outcome == MediaCaptureOutcome.NoFfmpeg || capture.FfmpegMissing)
                 && media.ShouldReportNoFfmpeg())
            await status.ShowAsync(ipc, "Clips and audio need ffmpeg installed", 3000, ct);
    }

    private static string BuildStatusMessage(
        string spelling, string? deckName, bool alreadyInDeck,
        MediaCaptureResult capture, MediaUploadOutcome? upload)
    {
        if (upload is { QuotaExceeded: true })
            return $"Your Jiten+ storage is full ({FormatBytes(upload.MaxBytes)})";

        if (upload is { Revoked: true })
            return $"{spelling}: mined, but no Jiten+ so nothing was attached";

        if (upload is { ImageAttempted: true, AudioAttempted: true }
            && upload.ImageUploaded != upload.AudioUploaded)
        {
            return upload.ImageUploaded
                ? $"{spelling}: screenshot saved, audio did not"
                : $"{spelling}: audio saved, screenshot did not";
        }

        var badges = upload is null || !upload.AnyUploaded
            ? ""
            : " +" + (upload.ImageUploaded ? "\U0001F5BC" : "") + (upload.AudioUploaded ? "\U0001F50A" : "");

        var suffix = capture.AnimationFellBack ? " (screenshot instead of a clip)" : "";

        if (alreadyInDeck)
            return deckName is not null
                ? $"{spelling}: already in {deckName}, updated{suffix}"
                : $"{spelling}: updated{suffix}";

        return deckName is not null
            ? $"{spelling} to {deckName}{badges}{suffix}"
            : $"{spelling}: mined{badges}{suffix}";
    }

    private static string FormatBytes(long bytes)
        => bytes >= 1024L * 1024 * 1024
            ? $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"
            : $"{bytes / (1024.0 * 1024):0.#} MB";

    private bool IsAlreadyInDeck(int wordId, byte readingIndex, int deckId, ReaderWord? word)
        => IsMinedTo(wordId, readingIndex, deckId) || word?.StudyDeckIds.Contains(deckId) == true;

    private string? DeckName(int deckId)
        => _decks.FirstOrDefault(d => d.UserStudyDeckId == deckId)?.Name;

    private async Task<string?> GetMediaTitleAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        try
        {
            return await ipc.GetPropertyAsync<string>("media-title", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read media-title");
            return null;
        }
    }

    public void Reset()
    {
        lock (_minedLock)
            _mined.Clear();
        media?.Reset();
    }
}
