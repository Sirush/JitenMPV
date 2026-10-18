using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JitenMPV.Core.Config;
using JitenMPV.Core.Interaction;
using JitenMPV.Core.MediaInfo;
using JitenMPV.Core.Mpv;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Jimaku;

/// One request runs at a time, and a file change cancels it.
public sealed partial class JimakuService : IDisposable
{
    public const string ActionMessage = "jiten-jimaku";
    public const string QueryMessage = "jiten-jimaku-query";
    private const string LuaScriptName = "jiten_mpv";

    /// mpv publishes the new path before the file's tracks, and auto-loaded external subtitles
    /// arrive after file-loaded.
    private static readonly TimeSpan FileSettleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TrackPollInterval = TimeSpan.FromMilliseconds(500);
    private const int TrackPollAttempts = 20;

    /// Per kind of search. Jimaku allows 25 requests a minute; title retries have to leave room for
    /// the file listings.
    private const int MaxSearchRequests = 5;

    private const int NoticeDurationMs = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly MpvIpcClient _ipc;
    private readonly StatusOverlay _status;
    private readonly ILogger _logger;
    private readonly HttpMessageHandler? _handler;
    private readonly string _cacheDir;
    private readonly SemaphoreSlim _busy = new(1, 1);

    private volatile PluginSettings _settings;
    private volatile JimakuClient _client;
    private CancellationTokenSource? _fileCts;
    private Session? _session;

    private sealed class Session(string mediaPath)
    {
        public string MediaPath { get; } = mediaPath;
        public string? MediaName { get; init; }
        public int? Episode { get; init; }
        public List<JimakuEntry> Entries { get; init; } = [];
        public bool Searched { get; init; }
        public int FileTracks { get; set; }
        public int FirstEntryFiles { get; set; }

        /// -1 while cycling the file's own Japanese tracks.
        public int EntryIndex { get; set; }
        public List<Candidate> Candidates { get; set; } = [];
        public int Index { get; set; } = -1;
        public JimakuEntry Entry => Entries[EntryIndex];
    }

    /// LocalPath is set once the file is on disk, or from the start for an unpacked archive member.
    /// TrackId marks a Japanese track the file already has.
    private sealed record Candidate(string Name, string? Url, string? LocalPath, int? TrackId = null);

    private sealed record RememberedChoice(JimakuEntry Entry, int? Episode, List<Candidate> Candidates, int Index);

    public JimakuService(MpvIpcClient ipc, StatusOverlay status, PluginSettings settings, ILogger logger,
        HttpMessageHandler? handler = null, string? cacheDir = null)
    {
        _ipc = ipc;
        _status = status;
        _settings = settings;
        _logger = logger;
        _handler = handler;
        _cacheDir = cacheDir ?? Path.Combine(AppPaths.CacheDir, "jimaku");
        _client = new JimakuClient(settings.JimakuApiKey, logger, handler);
    }

    public void UpdateSettings(PluginSettings newSettings)
    {
        var previous = _settings;
        _settings = newSettings;
        if (previous.JimakuApiKey != newSettings.JimakuApiKey)
            _client = new JimakuClient(newSettings.JimakuApiKey, _logger, _handler);
    }

    public void OnFileChanged(CancellationToken lifetime)
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var previous = Interlocked.Exchange(ref _fileCts, next);
        TaskHelper.CancelAndDispose(ref previous);

        var settings = _settings;
        if (!settings.JimakuEnabled || !settings.JimakuAutoFetch) return;

        var token = next.Token;
        _ = TaskHelper.RunSafe(async () =>
        {
            await Task.Delay(FileSettleDelay, token);
            await _busy.WaitAsync(token);
            try
            {
                await RunGuardedAsync(() => AutoFetchAsync(token), userInitiated: false, token);
            }
            finally
            {
                _busy.Release();
            }
        }, _logger, "Jimaku auto-fetch");
    }

    public void OnAction(string action, CancellationToken lifetime)
    {
        if (!_settings.JimakuEnabled) return;
        switch (action)
        {
            case "search":
                RunExclusive(ForceSearchAsync, lifetime);
                break;
            case "next":
                RunExclusive(NextAsync, lifetime);
                break;
            case "prev":
                RunExclusive(PreviousAsync, lifetime);
                break;
            default:
                _logger.LogWarning("Unknown Jimaku action: {Action}", action);
                break;
        }
    }

    public void OnQuery(string query, CancellationToken lifetime)
    {
        if (!_settings.JimakuEnabled || string.IsNullOrWhiteSpace(query)) return;
        RunExclusive(token => SearchAsync(query.Trim(), token), lifetime);
    }

    private void RunExclusive(Func<CancellationToken, Task> run, CancellationToken lifetime)
    {
        var token = _fileCts is { } cts ? cts.Token : lifetime;
        if (!_busy.Wait(0))
        {
            _ = TaskHelper.RunSafe(() => NotifyAsync("Jimaku: still busy with the last request", false, token),
                _logger);
            return;
        }

        _ = TaskHelper.RunSafe(async () =>
        {
            try
            {
                await RunGuardedAsync(() => run(token), userInitiated: true, token);
            }
            finally
            {
                _busy.Release();
            }
        }, _logger, "Jimaku");
    }

    private async Task RunGuardedAsync(Func<Task> run, bool userInitiated, CancellationToken ct)
    {
        try
        {
            await run();
        }
        catch (JimakuException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Jimaku {Kind}: {Message}", ex.Kind, ex.Message);
            if (ex.Kind == JimakuErrorKind.MissingKey && !userInitiated) return;
            await NotifyAsync(ErrorText(ex), important: true, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not save Jimaku subtitles");
            await NotifyAsync($"Jimaku: could not save subtitles: {ex.Message}", important: true, ct);
        }
    }

    private static string ErrorText(JimakuException ex) => ex.Kind switch
    {
        JimakuErrorKind.MissingKey => "Jimaku: no API key set. Add one in the settings (Ctrl+J).",
        JimakuErrorKind.Unauthorized => "Jimaku: the API key was refused. Check it in the settings (Ctrl+J).",
        JimakuErrorKind.RateLimited => ex.RetryAfter is { } wait
            ? $"Jimaku: too many requests, try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))}s"
            : "Jimaku: too many requests, try again shortly",
        _ => $"Jimaku: {ex.Message}"
    };

    private async Task AutoFetchAsync(CancellationToken ct)
    {
        var tracks = await ReadTracksAsync(ct, waitForTracks: true);
        var (state, track) = JapaneseTrackDetector.Find(tracks, IsJapaneseTrack);
        if (state == JapaneseTrackState.Selected) return;

        if (state == JapaneseTrackState.Available)
        {
            _logger.LogInformation("Selecting existing Japanese subtitle track {Id}", track!.Id);
            await _ipc.SetPropertyAsync("sid", track.Id, ct);
            await NotifyAsync($"Selected Japanese subtitles: {track.Title ?? track.Lang}", false, ct);
            return;
        }

        var props = await MediaIdentityReader.ReadPropertiesAsync(_ipc, _logger, ct);
        if (props?.Path is not { } path) return;
        var identity = MediaIdentityResolver.Resolve(props);
        if (identity is { IsExtra: true }) return;

        if (await TryLoadRememberedAsync(path, props.Filename, ct)) return;
        if (!_client.HasKey)
        {
            _logger.LogInformation("No Japanese subtitles, and no Jimaku API key to fetch them with");
            return;
        }

        await SearchAndLoadAsync(path, props.Filename, identity, null, userInitiated: false, ct);
    }

    /// Without a typed query, the file's own Japanese tracks come before Jimaku.
    private async Task SearchAsync(string? query, CancellationToken ct, bool skipFileTracks = false)
    {
        var props = await MediaIdentityReader.ReadPropertiesAsync(_ipc, _logger, ct);
        if (props?.Path is not { } path)
        {
            await NotifyAsync("Jimaku: no file loaded", true, ct);
            return;
        }

        var identity = MediaIdentityResolver.Resolve(props);

        // Submitting the detected title unchanged keeps its season and fallback queries.
        if (query is not null && query == SearchQueries(identity).FirstOrDefault())
            (query, skipFileTracks) = (null, true);

        if (query is null && !skipFileTracks)
        {
            var (tracks, selected) = await FileTracksAsync(ct);
            if (selected + 1 < tracks.Count)
            {
                var (entries, firstEntryFiles) = await PeekEntriesAsync(identity, ct);
                var session = new Session(path)
                {
                    MediaName = props.Filename, Episode = identity?.Episode, Entries = entries,
                    Searched = entries.Count > 0, Candidates = tracks, EntryIndex = -1, FileTracks = tracks.Count,
                    FirstEntryFiles = firstEntryFiles
                };
                _session = session;
                if (await AdvanceAsync(session, selected + 1, 1, userInitiated: true, ct)) return;
            }
        }

        if (!_client.HasKey)
            throw new JimakuException(JimakuErrorKind.MissingKey, "No Jimaku API key set");

        await SearchAndLoadAsync(path, props.Filename, identity, query, userInitiated: true, ct);
    }

    private async Task ForceSearchAsync(CancellationToken ct)
    {
        var props = await MediaIdentityReader.ReadPropertiesAsync(_ipc, _logger, ct);
        if (props?.Path is null)
        {
            await NotifyAsync("Jimaku: no file loaded", true, ct);
            return;
        }

        var title = SearchQueries(MediaIdentityResolver.Resolve(props)).FirstOrDefault() ?? "";
        await _ipc.SendScriptMessageAsync(LuaScriptName, "jiten-jimaku-prompt", title, "search", ct);
    }

    /// Searched up front even when the file has Japanese tracks, so the position shown while cycling
    /// them already counts the Jimaku files that follow.
    private async Task<(List<JimakuEntry> Entries, int FirstEntryFiles)> PeekEntriesAsync(
        MediaIdentity? identity, CancellationToken ct)
    {
        var queries = SearchQueries(identity);
        if (!_client.HasKey || queries.Count == 0) return ([], 0);
        try
        {
            var entries = await FindEntriesAsync(queries, identity?.Season, identity?.Episode, ct);
            if (entries.Count == 0) return ([], 0);
            var files = await _client.GetFilesAsync(entries[0].Id, identity?.Episode, ct);
            var usable = JimakuFileRanker.Rank(files, f => f.Name, identity?.Episode, identity?.TitleSource).Count;
            return (entries, usable);
        }
        catch (JimakuException ex)
        {
            _logger.LogWarning("Jimaku lookup failed, cycling the file's own tracks only: {Message}", ex.Message);
            return ([], 0);
        }
    }

    private async Task NextAsync(CancellationToken ct)
    {
        var path = (await MediaIdentityReader.ReadPropertiesAsync(_ipc, _logger, ct))?.Path;
        if (_session is not { } session || session.MediaPath != path)
        {
            await SearchAsync(null, ct);
            return;
        }

        if (await AdvanceAsync(session, session.Index + 1, 1, userInitiated: true, ct)) return;
        if (!session.Searched)
        {
            await SearchAsync(null, ct, skipFileTracks: true);
            return;
        }

        for (var entry = session.EntryIndex + 1; entry < session.Entries.Count; entry++)
            if (await LoadEntryAsync(session, entry, userInitiated: true, ct))
                return;

        await NotifyAsync("Jimaku: no more subtitles, starting over", false, ct);
        (session.Candidates, _) = await FileTracksAsync(ct);
        session.FileTracks = session.Candidates.Count;
        session.EntryIndex = -1;
        if (await AdvanceAsync(session, 0, 1, userInitiated: true, ct)) return;
        if (session.Entries.Count == 0 || !await LoadEntryAsync(session, 0, userInitiated: true, ct))
            await NotifyAsync("Jimaku: none of the subtitles could be loaded", true, ct);
    }

    /// Steps back through the same order as NextAsync; before the first position it wraps to the end of
    /// the list it started in rather than fetching every entry to find the overall last one.
    private async Task PreviousAsync(CancellationToken ct)
    {
        var path = (await MediaIdentityReader.ReadPropertiesAsync(_ipc, _logger, ct))?.Path;
        if (_session is not { } session || session.MediaPath != path)
        {
            await SearchAsync(null, ct);
            return;
        }

        var (startEntry, startCandidates) = (session.EntryIndex, session.Candidates);
        if (await AdvanceAsync(session, session.Index - 1, -1, userInitiated: true, ct)) return;

        if (startEntry >= 0)
        {
            for (var entry = startEntry - 1; entry >= 0; entry--)
                if (await LoadEntryAsync(session, entry, userInitiated: true, ct, fromEnd: true))
                    return;

            (session.Candidates, _) = await FileTracksAsync(ct);
            session.FileTracks = session.Candidates.Count;
            session.EntryIndex = -1;
            if (await AdvanceAsync(session, session.Candidates.Count - 1, -1, userInitiated: true, ct)) return;
        }

        (session.EntryIndex, session.Candidates) = (startEntry, startCandidates);
        if (!await AdvanceAsync(session, session.Candidates.Count - 1, -1, userInitiated: true, ct))
            await NotifyAsync("Jimaku: none of the subtitles could be loaded", true, ct);
    }

    private async Task SearchAndLoadAsync(
        string path, string? mediaName, MediaIdentity? identity, string? query, bool userInitiated,
        CancellationToken ct)
    {
        List<string> queries;
        int? episode;
        int? season;
        if (query is not null)
        {
            var parsed = ReleaseNameParser.Parse(query);
            queries = [parsed.IsJunkTitle ? query : parsed.Title];
            episode = parsed.Episode ?? identity?.Episode;
            season = parsed.Season;
        }
        else
        {
            queries = SearchQueries(identity);
            episode = identity?.Episode;
            season = identity?.Season;
        }

        if (queries.Count == 0)
        {
            if (userInitiated) await PromptAsync("", ct);
            return;
        }

        var label = episode is { } ep ? $"{queries[0]} ep {ep}" : queries[0];
        await NotifyAsync($"Jimaku: searching {label}", false, ct);

        var entries = await FindEntriesAsync(queries, season, episode, ct);
        if (entries.Count == 0)
        {
            await NotifyAsync($"Jimaku: nothing found for {queries[0]}", userInitiated, ct);
            if (userInitiated) await PromptAsync(queries[0], ct);
            return;
        }

        var (fileTracks, _) = await FileTracksAsync(ct);
        var session = new Session(path)
        {
            MediaName = mediaName, Episode = episode, Entries = entries, Searched = true, FileTracks = fileTracks.Count
        };
        _session = session;

        // Automatic fetching sticks to the best entry; the next key walks the rest.
        var last = userInitiated ? entries.Count : 1;
        for (var i = 0; i < last; i++)
            if (await LoadEntryAsync(session, i, userInitiated, ct))
                return;

        await NotifyAsync($"Jimaku: no subtitles for {label}", userInitiated, ct);
        if (userInitiated) await PromptAsync(queries[0], ct);
    }

    internal static List<string> SearchQueries(MediaIdentity? identity)
    {
        if (identity is null) return [];
        var queries = new List<string>();
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !ReleaseNameParser.IsJunkTitle(value) && !queries.Contains(value))
                queries.Add(value);
        }

        Add(identity.Title);
        Add(identity.SeriesTitle);
        Add(identity.FolderTitle);
        foreach (var candidate in identity.TitleCandidates)
        {
            Add(candidate);
            Add(ReleaseNameParser.Parse(candidate).SeriesTitle);
        }
        return queries;
    }

    /// The plausible entries of the first query that has any, followed by the rest of that
    /// search's results so the next key can still reach them. Live action is searched only when no
    /// anime entry fits, so anime matches stay as they were.
    private async Task<List<JimakuEntry>> FindEntriesAsync(
        List<string> queries, int? season, int? episode, CancellationToken ct)
    {
        foreach (var anime in new[] { true, false })
        {
            var requests = 0;
            foreach (var query in queries)
            {
                if (requests >= MaxSearchRequests) break;
                requests++;
                var results = await _client.SearchAsync(query, anime, ct);

                var cleaned = Whitespace().Replace(AsciiPunctuation().Replace(query, " "), " ").Trim();
                if (results.Count == 0 && cleaned != query && cleaned.Length > 0 && requests < MaxSearchRequests)
                {
                    requests++;
                    results = await _client.SearchAsync(cleaned, anime, ct);
                }

                var ranked = JimakuEntryMatcher.Rank(results, query, season, episode);
                _logger.LogInformation("Jimaku {Kind} search '{Query}': {Count} entries, {Plausible} plausible",
                    anime ? "anime" : "live action", query, results.Count, ranked.Count);
                if (ranked.Count > 0)
                    return [.. ranked, .. results.Where(r => !ranked.Contains(r))];
            }
        }
        return [];
    }

    private async Task<bool> LoadEntryAsync(Session session, int entryIndex, bool userInitiated, CancellationToken ct,
        bool fromEnd = false)
    {
        session.EntryIndex = entryIndex;
        var entry = session.Entry;

        var files = await _client.GetFilesAsync(entry.Id, session.Episode, ct);

        var ranked = JimakuFileRanker.Rank(files, f => f.Name, session.Episode, session.MediaName);
        _logger.LogInformation("Jimaku entry {Id} '{Name}': {Count} files, {Usable} usable",
            entry.Id, entry.Name, files.Count, ranked.Count);
        if (ranked.Count == 0) return false;

        session.Candidates = [.. ranked.Select(f => new Candidate(f.Name, f.Url, null))];
        session.Index = -1;
        await NotifyAsync($"Jimaku: found {entry.Name}", false, ct);
        return fromEnd
            ? await AdvanceAsync(session, session.Candidates.Count - 1, -1, userInitiated, ct)
            : await AdvanceAsync(session, 0, 1, userInitiated, ct);
    }

    /// Skips past downloads that fail and archives without this episode.
    private async Task<bool> AdvanceAsync(Session session, int start, int step, bool userInitiated,
        CancellationToken ct)
    {
        for (var i = start; i >= 0 && i < session.Candidates.Count; i += step)
        {
            ct.ThrowIfCancellationRequested();
            session.Index = i;
            try
            {
                if (await LoadCandidateAsync(session, i, userInitiated, ct, fromEnd: step < 0))
                {
                    if (session.Candidates[session.Index].TrackId is null) Remember(session);
                    return true;
                }
            }
            catch (JimakuException ex) when (ex.Kind is JimakuErrorKind.NotFound or JimakuErrorKind.BadResponse
                                                 or JimakuErrorKind.Network)
            {
                _logger.LogWarning("Skipping {Name}: {Message}", session.Candidates[i].Name, ex.Message);
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning("Skipping {Name}: {Message}", session.Candidates[i].Name, ex.Message);
            }
            i = session.Index;
        }
        return false;
    }

    /// Stepping backwards into an archive lands on its last member.
    private async Task<bool> LoadCandidateAsync(Session session, int index, bool userInitiated, CancellationToken ct,
        bool fromEnd = false)
    {
        var candidate = session.Candidates[index];
        if (candidate.TrackId is { } trackId)
        {
            await _ipc.SetPropertyAsync("sid", trackId, ct);
            await NotifyAsync(Position(session, index), userInitiated, ct);
            return true;
        }

        if (candidate.LocalPath is null)
        {
            var target = Path.Combine(_cacheDir, session.Entry.Id.ToString(), SafeFileName(candidate.Name));
            if (!File.Exists(target) || new FileInfo(target).Length == 0)
            {
                await NotifyAsync(Position(session, index, "Downloading"), userInitiated, ct);
                await _client.DownloadAsync(candidate.Url!, target, ct);
            }
            candidate = candidate with { LocalPath = target };
            session.Candidates[index] = candidate;
        }

        var local = candidate.LocalPath!;
        if (JimakuFileRanker.IsArchive(local))
        {
            var directory = local + ".d";
            var members = ArchiveEpisodeFilter.Filter(
                await SubtitleArchive.ExtractAsync(local, directory, _logger, ct), session.Episode);
            members = JimakuFileRanker.Rank(members, Path.GetFileName, null, session.MediaName);
            if (members.Count == 0)
            {
                _logger.LogInformation("No subtitles for this episode in {Name}", candidate.Name);
                return false;
            }

            session.Candidates.RemoveAt(index);
            session.Candidates.InsertRange(index, members.Select(m => new Candidate(
                $"{candidate.Name}/{Path.GetRelativePath(directory, m)}", null, m)));
            if (fromEnd) index += members.Count - 1;
            session.Index = index;
            candidate = session.Candidates[index];
            local = candidate.LocalPath!;
        }

        if (!File.Exists(local)) return false;
        if (!await AddTrackAsync(await PrepareAsync(local, ct), candidate.Name, ct)) return false;

        _logger.LogInformation("Jimaku loaded {Name} ({Index}/{Count})",
            candidate.Name, index + 1, session.Candidates.Count);
        if (session.EntryIndex == 0) session.FirstEntryFiles = session.Candidates.Count;
        await NotifyAsync(Position(session, index), userInitiated, ct);
        return true;
    }

    private static string Position(Session session, int index, string? state = null)
    {
        var candidate = session.Candidates[index];
        return PositionText(session.FileTracks, session.FirstEntryFiles, session.EntryIndex,
            session.EntryIndex > 0 ? session.Entry.Name : null, index, session.Candidates.Count,
            candidate.TrackId is null ? Path.GetFileName(candidate.Name) : candidate.Name, state);
    }

    /// The file's own tracks and the first entry's files count as one cycle; a later entry restarts
    /// the count, so its name is shown.
    internal static string PositionText(int fileTracks, int firstEntryFiles, int entryIndex, string? entryName,
        int index, int count, string name, string? state = null)
    {
        var (position, total) = entryIndex switch
        {
            < 0 => (index + 1, count + firstEntryFiles),
            0 => (fileTracks + index + 1, fileTracks + count),
            _ => (index + 1, count)
        };
        var label = entryName is null ? name : $"{entryName}: {name}";
        return state is null ? $"[{position}/{total}] {label}" : $"[{position}/{total}] {state}: {label}";
    }

    /// Writes a Japanese-only copy beside a bilingual JP/CN subtitle; anything else loads as is.
    private async Task<string> PrepareAsync(string path, CancellationToken ct)
    {
        var extension = JimakuFileRanker.ExtensionOf(path);
        if (!_settings.JimakuStripChinese || !JimakuFileRanker.IsText(path))
            return path;

        var (text, changed) = ChineseLineStripper.Strip(await File.ReadAllTextAsync(path, ct), extension);
        if (!changed) return path;

        var output = Path.Combine(Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}.jp-only.{extension}");
        await File.WriteAllTextAsync(output, text, ct);
        _logger.LogInformation("Stripped Chinese lines into {Path}", output);
        return output;
    }

    /// Re-selects a subtitle this file already has rather than adding it a second time.
    private async Task<bool> AddTrackAsync(string path, string name, CancellationToken ct)
    {
        var existing = (await ReadTracksAsync(ct, waitForTracks: false))
            .FirstOrDefault(t => t.ExternalFilename == path);
        if (existing is not null)
        {
            await _ipc.SetPropertyAsync("sid", existing.Id, ct);
            return true;
        }

        var error = await _ipc.SubAddAsync(path, "select", $"Jimaku: {Path.GetFileName(name)}", "ja", ct);
        if (error is null) return true;

        _logger.LogWarning("mpv could not load {Path}: {Error}", path, error);
        return false;
    }

    /// The Japanese text tracks the file has without Jimaku: embedded ones and those mpv loaded itself.
    private async Task<(List<Candidate> Tracks, int Selected)> FileTracksAsync(CancellationToken ct)
    {
        var tracks = (await ReadTracksAsync(ct, waitForTracks: false))
            .Where(t => !JapaneseTrackDetector.IsBitmap(t) && IsJapaneseTrack(t)
                        && t.ExternalFilename?.StartsWith(_cacheDir, StringComparison.Ordinal) != true)
            .ToList();
        return ([.. tracks.Select(t => new Candidate(t.Title ?? t.Lang ?? $"track {t.Id}", null, null, t.Id))],
            tracks.FindIndex(t => t.Selected));
    }

    private async Task<List<SubtitleTrack>> ReadTracksAsync(CancellationToken ct, bool waitForTracks)
    {
        for (var attempt = 0; ; attempt++)
        {
            var raw = await _ipc.GetPropertyRawAsync("track-list", ct);
            var loaded = raw is { ValueKind: JsonValueKind.Array } list && list.GetArrayLength() > 0;
            if (loaded || !waitForTracks || attempt >= TrackPollAttempts)
                return raw is { } value ? JapaneseTrackDetector.ParseTrackList(value) : [];
            await Task.Delay(TrackPollInterval, ct);
        }
    }

    /// A subtitle downloaded by hand rarely carries a language tag, so an untagged external file
    /// is judged by its text.
    private bool IsJapaneseTrack(SubtitleTrack track)
    {
        if (JapaneseTrackDetector.IsJapanese(track)) return true;
        if (track.Lang is { Length: > 0 } || track.ExternalFilename is not { } file || !File.Exists(file))
            return false;

        try
        {
            using var reader = new StreamReader(file, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[32 * 1024];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            return JapaneseTrackDetector.LooksJapanese(new string(buffer, 0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not read {Path}", file);
            return false;
        }
    }

    private async Task<bool> TryLoadRememberedAsync(string mediaPath, string? mediaName, CancellationToken ct)
    {
        RememberedChoice? choice;
        try
        {
            var file = RememberPath(mediaPath);
            if (!File.Exists(file)) return false;
            await using var stream = File.OpenRead(file);
            choice = await JsonSerializer.DeserializeAsync<RememberedChoice>(stream, JsonOptions, ct);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not read the remembered Jimaku choice");
            return false;
        }

        if (choice is null || choice.Index < 0 || choice.Index >= choice.Candidates.Count
            || choice.Candidates[choice.Index].LocalPath is not { } local || !File.Exists(local))
            return false;

        var (fileTracks, _) = await FileTracksAsync(ct);
        var session = new Session(mediaPath)
        {
            MediaName = mediaName,
            Episode = choice.Episode,
            Entries = [choice.Entry],
            Searched = true,
            FileTracks = fileTracks.Count,
            Candidates = choice.Candidates,
            Index = choice.Index
        };
        _session = session;
        _logger.LogInformation("Reusing {Name} from an earlier viewing", choice.Candidates[choice.Index].Name);
        return await LoadCandidateAsync(session, choice.Index, userInitiated: false, ct);
    }

    private void Remember(Session session)
    {
        try
        {
            var file = RememberPath(session.MediaPath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(
                new RememberedChoice(session.Entry, session.Episode, session.Candidates, session.Index), JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not remember the Jimaku choice");
        }
    }

    private string RememberPath(string mediaPath)
        => Path.Combine(_cacheDir, "remembered",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(mediaPath)))[..32] + ".json");

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. Path.GetFileName(name).Select(c => invalid.Contains(c) || c is '\\' or ':' ? '_' : c)])
            .Trim();
        return safe.Length == 0 ? "subtitle" : safe;
    }

    private Task PromptAsync(string defaultText, CancellationToken ct)
        => _ipc.SendScriptMessageAsync(LuaScriptName, "jiten-jimaku-prompt", defaultText, ct);

    /// Only problems the user has to act on, and where a key press landed, show over the video;
    /// progress goes to mpv's log.
    private Task NotifyAsync(string message, bool important, CancellationToken ct)
        => important
            ? _status.ShowAsync(_ipc, AssOverride().Replace(message, "_"), NoticeDurationMs, ct)
            : _status.LogAsync(_ipc, message, ct);

    public void Dispose()
    {
        TaskHelper.CancelAndDispose(ref _fileCts);
    }

    [GeneratedRegex(@"[!-/:-@\[-`{-~]")]
    private static partial Regex AsciiPunctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[{}\\]")]
    private static partial Regex AssOverride();
}
