using System.Text.Json;
using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Interaction;
using JitenMPV.Core.Media;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Plus;
using JitenMPV.Core.Rendering;
using JitenMPV.Core.Pitch;
using JitenMPV.Core.Subtitles;
using JitenMPV.Core.Theming;
using JitenMPV.Core.Update;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plugin;

public sealed class PluginHost(
    string pipePath,
    ILogger logger,
    IPopupPresenter popupPresenter,
    IMiningReviewPresenter? reviewPresenter = null,
    IMediaOverwritePresenter? overwritePresenter = null)
{
    internal const int SubtitleOverlayId = 1;
    internal const int UnderlineOverlayId = 2;
    internal const int HitboxDebugOverlayId = 3;
    internal const string OpenSettingsMessage = "jiten-open-settings";
    internal const string ToggleSubtitlesMessage = "jiten-toggle-subtitles";
    internal const string NavigateSubtitleMessage = "jiten-nav-sub";
    private const string LuaScriptName = "jiten_mpv";

    /// mpv reports the new track before it has finished loading it, and a file change fires several
    /// of these at once; the reload waits this long so it reads a settled track-list exactly once.
    private static readonly TimeSpan SubtitleSourceSettleDelay = TimeSpan.FromMilliseconds(400);
    /// mpv normally publishes width and height as separate events. Waiting briefly prevents
    /// measuring the subtitle once against a half-old geometry and again against the final pair.
    private static readonly TimeSpan OsdGeometrySettleDelay = TimeSpan.FromMilliseconds(50);

    private CancellationTokenSource? _currentSubtitleCts;
    private CancellationTokenSource? _preParseCts;
    private int _subtitleRenderVersion;
    private volatile PluginSettings? _settings;
    private volatile StyleResolver? _styleResolver;
    private volatile OverlayRenderer? _renderer;
    private volatile SubtitleColorizer? _colorizer;
    private volatile PopupDataBuilder? _popupDataBuilder;
    private volatile InteractionHandler? _interactionHandler;
    private volatile AutopauseService? _autopause;
    private volatile BlurHoverManager? _blurManager;
    private volatile StatusOverlay? _statusOverlay;
    private volatile SubtitleMeasurer? _measurer;
    private volatile SubtitleLineJoiner? _lineJoiner;
    private volatile JitenApiClient? _apiClient;
    private volatile MpvIpcClient? _ipcClient;
    private volatile KeybindManager? _keybindManager;
    private volatile MiningService? _miningService;
    private volatile RotationService? _rotationService;
    private volatile JitenPlusService? _plusService;
    private volatile MediaCaptureCoordinator? _mediaCapture;
    private volatile bool _wasPausedBeforeSettings;
    private volatile bool _subtitlesVisible = true;
    private readonly SemaphoreSlim _subtitleVisibilityLock = new(1, 1);

    private volatile bool _shuttingDown;
    private volatile string? _currentSubtitleText;

    /// The rects the bar overlay was last drawn against, so a re-render driven by interaction can
    /// redraw the bars without re-measuring the line that is already on screen.
    private volatile IReadOnlyList<WordRect> _lastLayout = [];
    private long? _mpvWindowId;
    private IReadOnlyList<string> _mpvDisplayNames = [];

    /// The line as mpv gave it, kept so the joined form can be recomputed when a setting that
    /// decides whether it fits changes under a subtitle already on screen.
    private volatile string? _currentSubtitleRaw;

    public event Action? OpenSettingsRequested;

    public void PausePlayback()
    {
        if (_ipcClient is null) return;
        _autopause?.SuspendRelease();
        _ = TaskHelper.RunSafe(async () =>
        {
            var paused = await _ipcClient.GetPropertyAsync<bool>("pause", CancellationToken.None);
            _wasPausedBeforeSettings = paused;
            if (!paused)
                await _ipcClient.SetPropertyAsync("pause", true, CancellationToken.None);
        }, logger, "Pause playback");
    }

    public void ResumePlayback()
    {
        var ipc = _ipcClient;
        if (ipc is null) return;

        var autopause = _autopause;
        _ = TaskHelper.RunSafe(async () =>
        {
            if (!_wasPausedBeforeSettings)
                await ipc.SetPropertyAsync("pause", false, CancellationToken.None);

            if (autopause is not null)
                await autopause.ResumeReleaseAsync(ipc, CancellationToken.None);
        }, logger, "Resume playback");
    }

    public void ReloadSettings(PluginSettings newSettings)
    {
        if (_styleResolver is null || _renderer is null || _colorizer is null) return;

        var previous = _settings;
        _settings = newSettings;

        _apiClient?.UpdateConnection(newSettings.ApiKey, newSettings.ApiBaseUrl, newSettings.ApiTimeoutSeconds);

        var theme = ResolveTheme(newSettings.Theme);
        _styleResolver.UpdateTheme(
            theme,
            newSettings.IPlusOneEnabled ? ThemePresets.IPlusOne : null,
            newSettings.FrequencyMarkingEnabled ? ThemePresets.Frequency : null,
            StyleResolver.BuildBlurStates(newSettings),
            newSettings.BlurStrength,
            PitchStyleBuilder.Build(newSettings));

        _renderer.UpdateSettings(newSettings);
        _popupDataBuilder?.UpdateSettings(newSettings);
        _interactionHandler?.UpdateSettings(newSettings);
        _autopause?.UpdateSettings(newSettings);
        _blurManager?.UpdateBlurStates(newSettings);
        _statusOverlay?.UpdateSettings(newSettings);
        _measurer?.UpdateSettings(newSettings);
        _lineJoiner?.UpdateSettings(newSettings);
        _miningService?.UpdateSettings(newSettings);
        _rotationService?.UpdateSettings(newSettings);
        _mediaCapture?.UpdateSettings(newSettings);

        if (!string.Equals(previous?.FfmpegPath, newSettings.FfmpegPath, StringComparison.Ordinal))
            FfmpegLocator.Invalidate();

        if (_plusService is { } plus
            && (previous?.ApiKey != newSettings.ApiKey || previous?.ApiBaseUrl != newSettings.ApiBaseUrl))
            _ = RunSafe(() => plus.RefreshAsync(CancellationToken.None));

        var (iPlusOne, freqMarker) = BuildDetectors(newSettings);
        _colorizer.UpdateDetectors(iPlusOne, freqMarker);

        if (_keybindManager is not null)
            _ = RunSafe(() => _keybindManager.ConfigureKeybindsAsync(newSettings, CancellationToken.None));

        if (_ipcClient is { } ipc)
        {
            if (previous?.DebugShowHitboxes == true && !newSettings.DebugShowHitboxes)
                _ = RunSafe(() => ipc.RemoveOverlayAsync(HitboxDebugOverlayId, CancellationToken.None));

            _ = RunSafe(() => ipc.SendScriptMessageAsync(LuaScriptName, "jiten-set-mouse-zone",
                newSettings.MouseZonePercent.ToString(), CancellationToken.None));
            _ = RunSafe(() => ipc.SendScriptMessageAsync(LuaScriptName, "jiten-set-buttons",
                newSettings.SettingsButtonEnabled ? "1" : "0",
                newSettings.SubtitleNavButtonsEnabled ? "1" : "0", CancellationToken.None));
            _ = RunSafe(() => SendNavKeysAsync(ipc, newSettings, CancellationToken.None));

            var raw = _currentSubtitleRaw;
            if (_subtitlesVisible && !string.IsNullOrWhiteSpace(raw))
            {
                var colorizer = _colorizer;
                var measurer = _measurer;
                var interaction = _interactionHandler;
                var joiner = _lineJoiner;
                _ = TaskHelper.RunSafe(async () =>
                {
                    var text = joiner is null
                        ? raw
                        : await joiner.ResolveAsync(raw, ipc, CancellationToken.None);
                    if (_currentSubtitleRaw != raw) return;
                    _currentSubtitleText = text;

                    var (ass, entry, underlines) = await colorizer.ColorizeAsync(text, CancellationToken.None);

                    // A subtitle change during the round trip means this overlay and layout are
                    // stale; writing them would clobber the newer line's rendering and hit-test rects.
                    if (!_subtitlesVisible || _currentSubtitleText != text) return;
                    await ipc.ShowOverlayAsync(SubtitleOverlayId, ass, CancellationToken.None);

                    if (entry is not null && measurer is not null && interaction is not null)
                    {
                        var layout = await measurer.MeasureAsync(text, entry, ipc, CancellationToken.None);
                        if (!_subtitlesVisible || _currentSubtitleText != text) return;
                        interaction.UpdateLayout(layout);
                        _lastLayout = layout;
                        await RenderUnderlineBarsAsync(entry, underlines, layout, ipc, CancellationToken.None);
                        await RenderDebugHitboxesAsync(layout, ipc, CancellationToken.None);
                    }
                }, logger, "Re-render subtitle after settings change");
            }
        }
    }

    private IReadOnlyDictionary<KnownState, WordStyleState> ResolveTheme(string themeName)
    {
        if (ThemePresets.All.TryGetValue(themeName, out var theme))
            return theme;

        if (themeName == "Custom" && _settings?.CustomThemeColors is { } custom)
            return BuildCustomTheme(custom);

        logger.LogWarning("Unknown theme '{Theme}', falling back to Default", themeName);
        return ThemePresets.Default;
    }

    private static IReadOnlyDictionary<KnownState, WordStyleState> BuildCustomTheme(
        Dictionary<string, CustomStateStyle> custom)
    {
        var theme = new Dictionary<KnownState, WordStyleState>();
        foreach (var state in Enum.GetValues<KnownState>())
        {
            if (custom.TryGetValue(state.ToString(), out var style))
                theme[state] = style.ToWordStyleState();
            else if (ThemePresets.Default.TryGetValue(state, out var fallback))
                theme[state] = fallback;
        }
        return theme;
    }

    private async Task RenderUnderlineBarsAsync(
        ParseCacheEntry? entry,
        IReadOnlyDictionary<(int WordId, byte ReadingIndex), UnderlineBar>? themeUnderlines,
        IReadOnlyList<WordRect> layout,
        MpvIpcClient ipc, CancellationToken ct)
    {
        var settings = _settings;
        if (settings is null) return;

        var ass = UnderlineBarRenderer.Render(
            BuildUnderlineBars(entry, themeUnderlines, layout, PitchStyleBuilder.BuildUnderlineColors(settings),
                settings.PitchUnderlineThickness),
            _measurer?.LinePitch);

        if (ass.Length == 0)
            await ipc.RemoveOverlayAsync(UnderlineOverlayId, ct);
        else
            await ipc.ShowOverlayAsync(UnderlineOverlayId, ass, ct);
    }

    /// The theme bar comes first so it sits nearest the text, leaving pitch stacked beneath it.
    private static IEnumerable<(WordRect Rect, IReadOnlyList<UnderlineBar> Bars)> BuildUnderlineBars(
        ParseCacheEntry? entry,
        IReadOnlyDictionary<(int WordId, byte ReadingIndex), UnderlineBar>? themeUnderlines,
        IReadOnlyList<WordRect> layout,
        IReadOnlyDictionary<PitchClass, string> pitchColors,
        double pitchThickness)
    {
        foreach (var rect in layout)
        {
            var key = (rect.WordId, rect.ReadingIndex);
            List<UnderlineBar>? bars = null;

            if (themeUnderlines?.TryGetValue(key, out var themeBar) == true)
                (bars ??= []).Add(themeBar);

            if (pitchColors.Count > 0 && entry is not null
                && entry.PitchClasses.TryGetValue(key, out var pitchClass)
                && pitchColors.TryGetValue(pitchClass, out var pitchColor))
            {
                (bars ??= []).Add(new UnderlineBar(pitchColor, pitchThickness));
            }

            if (bars is not null)
                yield return (rect, bars);
        }
    }

    /// No-ops while the option is off so the common path costs no extra round trip; ReloadSettings
    /// clears the overlay when it is turned off.
    private Task RenderDebugHitboxesAsync(
        IReadOnlyList<WordRect> layout, MpvIpcClient ipc, CancellationToken ct)
    {
        if (_settings?.DebugShowHitboxes != true) return Task.CompletedTask;

        var ass = HitboxDebugRenderer.Render(layout);
        return ass.Length == 0
            ? ipc.RemoveOverlayAsync(HitboxDebugOverlayId, ct)
            : ipc.ShowOverlayAsync(HitboxDebugOverlayId, ass, ct);
    }

    private static (IPlusOneDetector?, FrequencyMarker?) BuildDetectors(PluginSettings settings)
    {
        var iPlusOne = settings.IPlusOneEnabled
            ? new IPlusOneDetector(settings.IPlusOneMinTokens, settings.IPlusOneMaxFrequencyRank)
            : null;
        var freqMarker = settings.FrequencyMarkingEnabled
            ? new FrequencyMarker(settings.FrequencyTopN, settings.FrequencyMarkAllStates)
            : null;
        return (iPlusOne, freqMarker);
    }

    public async Task RunAsync(PluginSettings? preloaded, CancellationToken ct)
    {
        logger.LogInformation("JitenMPV starting, pipe: {Path}", pipePath);

        var settings = preloaded ?? await SettingsManager.LoadAsync(ct);
        _settings = settings;

        // Ctrl+J is bound over IPC further down, so aborting here would remove the only way to fix
        // the problem being reported. ReloadSettings picks the key up live once it is set.
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            logger.LogWarning("No API key configured; set one from Ctrl+J or in {Path}",
                Path.Combine(AppPaths.ConfigDir, "config.json"));
            await SettingsManager.SaveAsync(settings, ct);
        }

        var apiClient = new JitenApiClient(
            settings.ApiKey, settings.ApiBaseUrl, settings.ApiTimeoutSeconds, logger);
        _apiClient = apiClient;
        var parseCache = new ParseCache(settings.CacheSize);

        var theme = ResolveTheme(settings.Theme);
        var styleResolver = new StyleResolver(
            theme, ThemePresets.Unparsed,
            settings.IPlusOneEnabled ? ThemePresets.IPlusOne : null,
            settings.FrequencyMarkingEnabled ? ThemePresets.Frequency : null,
            StyleResolver.BuildBlurStates(settings),
            settings.BlurStrength,
            PitchStyleBuilder.Build(settings));
        _styleResolver = styleResolver;

        var osd = new OsdState();
        var renderer = new OverlayRenderer(settings, styleResolver, osd);
        _renderer = renderer;

        var (iPlusOne, freqMarker) = BuildDetectors(settings);
        var colorizer = new SubtitleColorizer(apiClient, parseCache, renderer, iPlusOne, freqMarker, logger);
        _colorizer = colorizer;
        var timeline = new SubtitleTimeline();
        var preParser = new PreParseService(
            apiClient, parseCache, logger, settings.PreparseBatchSize, timeline, settings);
        var measurer = new SubtitleMeasurer(settings, osd);
        _measurer = measurer;
        var lineJoiner = new SubtitleLineJoiner(settings, osd);
        _lineJoiner = lineJoiner;

        var hitTest = new HitTestService();
        var blurManager = new BlurHoverManager(settings);
        _blurManager = blurManager;
        var statusOverlay = new StatusOverlay(settings);
        _statusOverlay = statusOverlay;
        var wordAction = new WordActionService(apiClient, parseCache, statusOverlay, logger);
        var reviewService = new InlineReviewService(apiClient, parseCache, statusOverlay, logger);
        var plusService = new JitenPlusService(apiClient, logger);
        _plusService = plusService;
        var pausingReview = reviewPresenter is null
            ? null
            : new PausingReviewPresenter(reviewPresenter, PausePlayback, ResumePlayback);
        var mediaCapture = new MediaCaptureCoordinator(
            apiClient, plusService, timeline, settings, logger, pausingReview, overwritePresenter);
        _mediaCapture = mediaCapture;
        var miningService = new MiningService(
            apiClient, parseCache, statusOverlay, settings, logger, mediaCapture);
        _miningService = miningService;
        var rotationService = new RotationService(settings);
        _rotationService = rotationService;
        var autopause = new AutopauseService(settings, logger);
        _autopause = autopause;

        await using var ipcClient = new MpvIpcClient(pipePath, logger);
        _ipcClient = ipcClient;

        try
        {
            await ipcClient.ConnectAsync(ct);
            logger.LogInformation("Connected to mpv");

            var dataBuilder = new PopupDataBuilder(settings, miningService, rotationService);
            _popupDataBuilder = dataBuilder;
            var popupManager = new PopupManager(dataBuilder, popupPresenter);

            // A window screenshot takes every OSD layer with it, so the dictionary popup and the
            // status line have to be off screen before the shutter and back after it.
            mediaCapture.PrepareWindowCapture = async token =>
            {
                await popupManager.HideAsync(token);
                await statusOverlay.HideAsync(ipcClient, token);
            };

            using var interaction = new InteractionHandler(
                ipcClient, hitTest, blurManager, popupManager, autopause,
                wordAction, reviewService, miningService, rotationService, colorizer,
                settings, osd, logger);
            _interactionHandler = interaction;
            interaction.UnderlineBarsChanged = (entry, underlines, ct) =>
                RenderUnderlineBarsAsync(entry, underlines, _lastLayout, ipcClient, ct);

            var keybindManager = new KeybindManager(ipcClient, logger);
            _keybindManager = keybindManager;

            popupManager.VisibilityChanged += visible =>
            {
                _ = RunSafe(async () =>
                {
                    // Claiming every click is what lets one dismiss a popup that nothing else will
                    // close, but it also takes clicks the OSC needs, so it is asked for only when
                    // the popup has no other way out. A click-triggered popup ignores pointer moves
                    // and so never releases on its own; a hovered one always does, delayed or not.
                    var clickToDismiss = visible
                        && _settings?.PopupTrigger == PopupTriggerMode.Click;
                    await ipcClient.SendScriptMessageAsync(
                        LuaScriptName, "jiten-popup-state",
                        visible ? "1" : "0", clickToDismiss ? "1" : "0", ct);

                    if (visible)
                        await keybindManager.EnableKeybindsAsync(ct);
                    else
                        await keybindManager.DisableKeybindsAsync(ct);
                });
            };

            ipcClient.SubtitleTextChanged += text =>
            {
                _currentSubtitleRaw = text;
                _currentSubtitleText = text;
                QueueSubtitleRender(text, ipcClient, colorizer,
                    measurer, interaction, lineJoiner, ct);
            };

            ipcClient.MouseEvent += e =>
            {
                _ = RunSafe(() => interaction.OnMouseEventAsync(e, ct));
            };

            ipcClient.PropertyChanged += (name, data) =>
            {
                if (name == "window-id")
                {
                    var windowId = data.ValueKind == JsonValueKind.Number
                                   && data.TryGetInt64(out var id) && id > 0
                        ? id
                        : (long?)null;
                    _mpvWindowId = windowId;
                    popupPresenter.UpdateWindowContext(
                        new PopupWindowContext(windowId, _mpvDisplayNames));
                    return;
                }

                if (name == "display-names")
                {
                    _mpvDisplayNames = data.ValueKind == JsonValueKind.Array
                        ? [.. data.EnumerateArray()
                            .Where(item => item.ValueKind == JsonValueKind.String)
                            .Select(item => item.GetString())
                            .OfType<string>()]
                        : [];
                    popupPresenter.UpdateWindowContext(
                        new PopupWindowContext(_mpvWindowId, _mpvDisplayNames));
                    return;
                }

                if (name == "sub-visibility")
                {
                    if (data.ValueKind == JsonValueKind.True && !_shuttingDown)
                        _ = RunSafe(() => ipcClient.SetPropertyAsync("sub-visibility", "no", ct));
                    return;
                }

                if (data.ValueKind != JsonValueKind.Number) return;
                int value = data.GetInt32();
                bool changed = name switch
                {
                    "osd-width" => osd.Update(value, osd.Height),
                    "osd-height" => osd.Update(osd.Width, value),
                    _ => false
                };
                if (!changed) return;

                renderer.RebuildPreamble();
                QueueSubtitleRender(_currentSubtitleRaw, ipcClient, colorizer,
                    measurer, interaction, lineJoiner, ct, OsdGeometrySettleDelay,
                    geometryOnly: true);
            };

            // Tracked whether or not pre-parsing is on: the cue timeline it builds is also what
            // subtitle navigation steps through, and that has to work either way.
            ipcClient.PropertyChanged += (name, _) =>
            {
                if (name is "sid" or "path")
                    ReloadSubtitleSource(ipcClient, preParser, timeline, ct);
            };

            ipcClient.ScriptMessageReceived += (name, args) =>
            {
                if (name == OpenSettingsMessage)
                {
                    logger.LogInformation("Received {Message}", OpenSettingsMessage);
                    OpenSettingsRequested?.Invoke();
                }
                else if (name == ToggleSubtitlesMessage)
                {
                    _ = RunSafe(() => ToggleSubtitlesAsync(ipcClient, ct));
                }
                else if (name == NavigateSubtitleMessage && args.Length >= 1)
                {
                    if (int.TryParse(args[0], out var delta))
                        _ = RunSafe(() => NavigateSubtitleAsync(ipcClient, timeline, delta, ct));
                }
                else if (name == "jiten-keybind-action" && args.Length >= 1)
                {
                    if (Enum.TryParse<PopupAction>(args[0], out var action))
                        _ = RunSafe(() => interaction.ExecutePopupActionAsync(action, ct));
                    else
                        logger.LogWarning("Unknown keybind action: {Action}", args[0]);
                }
            };

            var readLoop = ipcClient.RunAsync(ct);

            await ipcClient.ChangeListAsync("watch-later-options", "remove", "sub-visibility", ct);
            await ipcClient.SetPropertyAsync("sub-visibility", "no", ct);
            await ipcClient.ObservePropertyAsync("sub-text", 1, ct);
            await ipcClient.ObservePropertyAsync("osd-width", 2, ct);
            await ipcClient.ObservePropertyAsync("osd-height", 3, ct);
            await ipcClient.ObservePropertyAsync("window-id", 6, ct);
            await ipcClient.ObservePropertyAsync("display-names", 7, ct);
            await ipcClient.ObservePropertyAsync("sub-visibility", 8, ct);

            await ipcClient.ObservePropertyAsync("sid", 4, ct);
            await ipcClient.ObservePropertyAsync("path", 5, ct);

            var widthTask = ipcClient.GetPropertyAsync<int>("osd-width", ct);
            var heightTask = ipcClient.GetPropertyAsync<int>("osd-height", ct);
            osd.Update(await widthTask, await heightTask);
            renderer.RebuildPreamble();

            var clientName = await ipcClient.GetClientNameAsync(ct);
            logger.LogInformation("IPC client name: {Name}", clientName);
            if (clientName is not null)
            {
                await ipcClient.KeybindAsync("Ctrl+j",
                    $"script-message-to {clientName} {OpenSettingsMessage}", ct);

                // The Lua side addresses this process by name for the settings button, and treats
                // not knowing the name as "no plugin to open settings for".
                await ipcClient.SendScriptMessageAsync(LuaScriptName, "jiten-set-client", clientName, ct);
            }

            await ipcClient.SendScriptMessageAsync(LuaScriptName, "jiten-set-mouse-zone",
                settings.MouseZonePercent.ToString(), ct);
            await ipcClient.SendScriptMessageAsync(LuaScriptName, "jiten-set-buttons",
                settings.SettingsButtonEnabled ? "1" : "0",
                settings.SubtitleNavButtonsEnabled ? "1" : "0", ct);

            await SendNavKeysAsync(ipcClient, settings, ct);

            await keybindManager.ConfigureKeybindsAsync(settings, ct);

            if (settings.MiningEnabled || settings.PopupShowDeckMembership)
                _ = RunSafe(() => miningService.RefreshDecksAsync(ct));

            plusService.StartPeriodicRefresh(ct);
            MediaTempFiles.SweepStale(logger);

            ReloadSubtitleSource(ipcClient, preParser, timeline, ct);

            _ = RunSafe(() => ShowStartupNoticesAsync(ipcClient, settings, ct));

            logger.LogInformation("JitenMPV plugin running.");
            await readLoop;
            logger.LogWarning("Read loop exited");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("RunAsync cancelled");
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "IPC connection lost");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in RunAsync");
        }
        finally
        {
            _shuttingDown = true;
            try
            {
                using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var cct = cleanupCts.Token;
                await ipcClient.RemoveOverlayAsync(SubtitleOverlayId, cct);
                await ipcClient.RemoveOverlayAsync(UnderlineOverlayId, cct);
                await ipcClient.RemoveOverlayAsync(HitboxDebugOverlayId, cct);
                await ipcClient.RemoveOverlayAsync(StatusOverlay.StatusLayerId, cct);
                await ipcClient.SendScriptMessageAsync(
                    LuaScriptName, "jiten-set-client", "", cct);
                await ipcClient.SetPropertyAsync("sub-visibility", "yes", cct);
            }
            catch { }

            statusOverlay.Dispose();
            plusService.Dispose();
            TaskHelper.CancelAndDispose(ref _currentSubtitleCts);
            _subtitleVisibilityLock.Dispose();
        }
    }

    /// Drops the cues of the previous track before reading the new one: the timeline feeds the sentence
    /// on a mined card, so stale cues would put another language on it.
    private void ReloadSubtitleSource(
        MpvIpcClient ipc, PreParseService preParser, SubtitleTimeline timeline, CancellationToken ct)
    {
        TaskHelper.CancelAndDispose(ref _preParseCts);
        timeline.Clear();

        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _preParseCts = linked;
        _ = RunSafe(async () =>
        {
            try
            {
                await Task.Delay(SubtitleSourceSettleDelay, linked.Token);
                await StartPreParseAsync(ipc, preParser, linked.Token);
            }
            finally
            {
                linked.Dispose();
            }
        });
    }

    private async Task StartPreParseAsync(MpvIpcClient ipc, PreParseService preParser, CancellationToken ct)
    {
        string? subFile = null;
        try
        {
            subFile = await ipc.GetPropertyAsync<string>("current-tracks/sub/external-filename", ct);
        }
        catch { }

        var parseTexts = _settings?.PreparseEnabled ?? false;
        if (!string.IsNullOrEmpty(subFile))
            await preParser.PreParseFileAsync(subFile, parseTexts, ct);
        else
            await preParser.PreParseEmbeddedAsync(ipc, parseTexts, ct);
    }

    /// Steps the requested number of subtitles using the pre-parsed cue list, which covers the whole
    /// file rather than only what mpv has demuxed. Without a timeline there is nothing better than
    /// mpv's own sub-seek, which cannot reach a line it has not read yet.
    private async Task NavigateSubtitleAsync(
        MpvIpcClient ipc, SubtitleTimeline timeline, int delta, CancellationToken ct)
    {
        if (delta == 0) return;

        if (!timeline.IsLoaded)
        {
            await ipc.SubSeekAsync(delta, ct);
            return;
        }

        if (await ipc.GetPropertyAsync<double?>("time-pos", ct) is not { } timePos) return;

        // Cue timings are the subtitle file's own; mpv shows a cue at file time * sub-speed +
        // sub-delay, so a retimed track has to be mapped both ways around the lookup. Read per nav
        // rather than cached: both are live properties the user can change mid-playback.
        var delay = await ipc.GetPropertyAsync<double?>("sub-delay", ct) ?? 0;
        var speed = await ipc.GetPropertyAsync<double?>("sub-speed", ct) ?? 1;
        if (speed <= 0) speed = 1;

        var playhead = TimeSpan.FromSeconds((timePos - delay) / speed);
        if (timeline.Step(playhead, delta) is not { } step)
        {
            logger.LogDebug("Subtitle nav {Delta} ran off the end of the timeline", delta);
            return;
        }

        await ipc.SeekAbsoluteAsync(step.SeekTime.TotalSeconds * speed + delay, ct);
    }

    private static async Task SendNavKeysAsync(
        MpvIpcClient ipc, PluginSettings settings, CancellationToken ct)
    {
        await ipc.SendScriptMessageAsync(LuaScriptName, "jiten-set-nav-key",
            "prev_sub", settings.KeybindPrevSub, ct);
        await ipc.SendScriptMessageAsync(LuaScriptName, "jiten-set-nav-key",
            "next_sub", settings.KeybindNextSub, ct);
        await ipc.SendScriptMessageAsync(LuaScriptName, "jiten-set-nav-key",
            "loop_sub", settings.KeybindLoopSub, ct);
    }

    private async Task ToggleSubtitlesAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        await _subtitleVisibilityLock.WaitAsync(ct);
        try
        {
            var visible = !_subtitlesVisible;
            _subtitlesVisible = visible;

            await ipc.SetPropertyAsync("sub-visibility", "no", ct);

            if (visible)
            {
                if (_colorizer is { } colorizer
                    && _measurer is { } measurer
                    && _interactionHandler is { } interaction
                    && _lineJoiner is { } joiner)
                {
                    QueueSubtitleRender(_currentSubtitleRaw, ipc, colorizer,
                        measurer, interaction, joiner, ct);
                }
            }
            else
            {
                TaskHelper.CancelAndDispose(ref _currentSubtitleCts);
                if (_interactionHandler is { } interaction)
                    await interaction.OnSubtitleRenderedAsync(null, null, [], ct);
                await ipc.RemoveOverlayAsync(SubtitleOverlayId, ct);
                await ipc.RemoveOverlayAsync(UnderlineOverlayId, ct);
                await RenderDebugHitboxesAsync([], ipc, ct);
            }

            await ipc.ShowTextAsync(
                visible ? "JitenMPV subtitles visible" : "JitenMPV subtitles hidden",
                1000, ct);
        }
        finally
        {
            _subtitleVisibilityLock.Release();
        }
    }

    private Task RunSafe(Func<Task> action)
        => TaskHelper.RunSafe(action, logger);

    private const int NoticeDurationMs = 6000;

    /// mpv's show-text replaces whatever is already on screen, so these run one after another:
    /// fired together, only the last would ever be read.
    private async Task ShowStartupNoticesAsync(
        MpvIpcClient ipc, PluginSettings settings, CancellationToken ct)
    {
        // Nothing else is worth saying while no lookup can succeed, and queueing three notices
        // behind this one would bury it.
        if (await WarnIfNoApiKeyAsync(ipc, settings, ct)) return;

        Func<Task<bool>>[] notices =
        [
            () => WarnIfFfmpegMissingAsync(ipc, settings, ct),
            () => WarnIfWaylandAsync(ipc, ct),
            () => NotifyUpdateAsync(ipc, settings, ct)
        ];

        foreach (var notice in notices)
            if (await notice())
                await Task.Delay(NoticeDurationMs + 500, ct);
    }

    /// Without this a fresh install reads as a plugin that does nothing: the settings window is
    /// reachable, but nothing on screen says so.
    private static async Task<bool> WarnIfNoApiKeyAsync(
        MpvIpcClient ipc, PluginSettings settings, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(settings.ApiKey)) return false;

        await ipc.ShowTextAsync(
            "jiten-mpv: no API key set, so subtitles cannot be looked up yet. "
            + "Press Ctrl+J to paste your key from jiten.moe.", NoticeDurationMs, ct);
        return true;
    }

    /// Wayland exposes no global pointer position to an unfocused client and no way for a client to
    /// place its own toplevel, so the dictionary popup cannot follow the word under the cursor.
    /// Subtitle colouring is unaffected. Stated once up front rather than left to be discovered.
    private static async Task<bool> WarnIfWaylandAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) return false;

        var wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
                      || string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                          "wayland", StringComparison.OrdinalIgnoreCase);
        if (!wayland) return false;

        // A Wayland desktop is fine when mpv itself selected X11/XWayland: window-id is then an XID
        // and the popup can be positioned and parented through the same X display.
        if (await WaitForMpvWindowAsync(ipc, ct))
            return false;

        await ipc.ShowTextAsync(
            "jiten-mpv: native Wayland video detected. Subtitle colouring works, but precise "
            + "dictionary popup placement needs mpv to use X11/XWayland.", NoticeDurationMs, ct);
        return true;
    }

    /// mpv publishes window-id only once the video output window exists, so reading it once at startup
    /// reports "no window" for an XWayland session that is merely still coming up.
    private static async Task<bool> WaitForMpvWindowAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        const int attempts = 10;
        var interval = TimeSpan.FromMilliseconds(200);

        for (int i = 0; i < attempts; i++)
        {
            if (await ipc.GetPropertyAsync<long?>("window-id", ct) is > 0) return true;
            if (i < attempts - 1) await Task.Delay(interval, ct);
        }

        return false;
    }

    /// Audio and clip mining shell out to ffmpeg, so its absence is a half-broken install rather
    /// than a missing nicety. Without this the first symptom is a capture that silently produces
    /// no audio, long after there is anything to connect it to.
    private async Task<bool> WarnIfFfmpegMissingAsync(
        MpvIpcClient ipc, PluginSettings settings, CancellationToken ct)
    {
        if (settings.FfmpegPromptDismissed) return false;
        if (!settings.MediaCaptureEnabled && !settings.PreparseEnabled) return false;
        if (await FfmpegLocator.ResolveAsync(settings.FfmpegPath, ct) is not null) return false;

        await ipc.ShowTextAsync(
            "jiten-mpv: ffmpeg is missing, so audio and clips cannot be mined. Press Ctrl+J to set it up.",
            NoticeDurationMs, ct);
        return true;
    }

    private static async Task<bool> NotifyUpdateAsync(
        MpvIpcClient ipc, PluginSettings settings, CancellationToken ct)
    {
        if (await UpdateChecker.CheckAsync(settings.UpdateCheckEnabled, ct) is not { } update)
            return false;

        await ipc.ShowTextAsync(
            $"jiten-mpv {update.Version} is available. Press Ctrl+J to install it.",
            NoticeDurationMs, ct);
        return true;
    }

    private void QueueSubtitleRender(
        string? text, MpvIpcClient ipcClient,
        SubtitleColorizer colorizer, SubtitleMeasurer measurer,
        InteractionHandler interaction, SubtitleLineJoiner joiner,
        CancellationToken lifetimeToken, TimeSpan settleDelay = default,
        bool geometryOnly = false)
    {
        if (!_subtitlesVisible) return;

        TaskHelper.CancelAndDispose(ref _currentSubtitleCts);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        _currentSubtitleCts = linkedCts;
        var version = Interlocked.Increment(ref _subtitleRenderVersion);

        var token = linkedCts.Token;

        _ = TaskHelper.RunSafe(async () =>
            {
                if (settleDelay > TimeSpan.Zero)
                    await Task.Delay(settleDelay, token);
                await OnSubtitleChangedAsync(text, ipcClient, colorizer,
                    measurer, interaction, joiner, version, geometryOnly, token);
            }, logger, "Render subtitle")
            .ContinueWith(_ => linkedCts.Dispose(), TaskScheduler.Default);
    }

    private async Task OnSubtitleChangedAsync(
        string? text, MpvIpcClient ipcClient,
        SubtitleColorizer colorizer,
        SubtitleMeasurer measurer, InteractionHandler interaction,
        SubtitleLineJoiner joiner,
        int renderVersion,
        bool geometryOnly,
        CancellationToken ct)
    {
        // Cancellation is cooperative, so an already-dispatched overlay write can still land after a
        // newer render or a hide has taken over. Checked again after every round trip.
        bool Superseded() => renderVersion != Volatile.Read(ref _subtitleRenderVersion)
                             || !_subtitlesVisible;

        try
        {
            if (Superseded() || _currentSubtitleRaw != text) return;

            if (string.IsNullOrWhiteSpace(text))
            {
                await interaction.OnSubtitleRenderedAsync(null, null, [], ct);
                if (Superseded()) return;
                await ipcClient.RemoveOverlayAsync(SubtitleOverlayId, ct);
                await ipcClient.RemoveOverlayAsync(UnderlineOverlayId, ct);
                await RenderDebugHitboxesAsync([], ipcClient, ct);
                return;
            }

            var display = await joiner.ResolveAsync(text, ipcClient, ct);

            // The fit measurement is a round trip, so a newer line can already own these fields.
            if (Superseded() || _currentSubtitleRaw != text) return;
            _currentSubtitleText = display;

            var (ass, entry, underlines) = await colorizer.ColorizeAsync(display, ct);
            if (Superseded()) return;

            var showTask = ipcClient.ShowOverlayAsync(SubtitleOverlayId, ass, ct);
            var measureTask = entry is not null
                ? measurer.MeasureAsync(display, entry, ipcClient, ct)
                : Task.FromResult<List<WordRect>>([]);

            await Task.WhenAll(showTask, measureTask);
            if (Superseded()) return;
            var layout = measureTask.Result;
            _lastLayout = layout;

            await RenderUnderlineBarsAsync(entry, underlines, layout, ipcClient, ct);
            await RenderDebugHitboxesAsync(layout, ipcClient, ct);
            if (Superseded()) return;

            if (geometryOnly)
                await interaction.OnSubtitleLayoutChangedAsync(entry, layout, ct);
            else
                await interaction.OnSubtitleRenderedAsync(display, entry, layout, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing subtitle change");
        }
    }
}
