using System.Text.Json;
using JitenMPV.Core.Api;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Interaction;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Rendering;
using JitenMPV.Core.Theming;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plugin;

public sealed class PluginHost(string pipePath, ILogger logger, IPopupPresenter popupPresenter)
{
    internal const int SubtitleOverlayId = 1;

    private CancellationTokenSource? _currentSubtitleCts;

    public async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("JitenMPV starting, pipe: {Path}", pipePath);

        var settings = await SettingsManager.LoadAsync();

        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            Console.Error.WriteLine("ERROR: No API key configured.");
            Console.Error.WriteLine($"Set api_key in: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jiten-mpv", "config.json")}");
            await SettingsManager.SaveAsync(settings);
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri(settings.ApiBaseUrl) };
        var apiClient = new JitenApiClient(http, settings.ApiKey, logger);
        var parseCache = new ParseCache();

        if (!ThemePresets.All.TryGetValue(settings.Theme, out var theme))
        {
            logger.LogWarning("Unknown theme '{Theme}', falling back to Default", settings.Theme);
            theme = ThemePresets.Default;
        }
        var styleResolver = new StyleResolver(
            theme, ThemePresets.Unparsed,
            settings.IPlusOneEnabled ? ThemePresets.IPlusOne : null,
            settings.FrequencyMarkingEnabled ? ThemePresets.Frequency : null);

        var osd = new OsdState();
        var renderer = new OverlayRenderer(settings, styleResolver, osd);
        var iPlusOne = settings.IPlusOneEnabled
            ? new IPlusOneDetector(settings.IPlusOneMinTokens, settings.IPlusOneMaxFrequencyRank)
            : null;
        var freqMarker = settings.FrequencyMarkingEnabled
            ? new FrequencyMarker(settings.FrequencyTopN, settings.FrequencyMarkAllStates)
            : null;

        var colorizer = new SubtitleColorizer(apiClient, parseCache, renderer, iPlusOne, freqMarker, logger);
        var preParser = new PreParseService(apiClient, parseCache, logger);
        var measurer = new SubtitleMeasurer(settings, osd);

        var hitTest = new HitTestService();
        var blurManager = new BlurHoverManager(settings);
        var statusOverlay = new StatusOverlay(settings);
        var wordAction = new WordActionService(apiClient, parseCache, statusOverlay, logger);
        var reviewService = new InlineReviewService(apiClient, parseCache, statusOverlay, logger);
        var autopause = new AutopauseService(settings);

        await using var ipcClient = new MpvIpcClient(pipePath, logger);

        try
        {
            await ipcClient.ConnectAsync(ct);
            logger.LogInformation("Connected to mpv");

            var dataBuilder = new PopupDataBuilder(settings);
            var popupManager = new PopupManager(dataBuilder, popupPresenter);

            using var interaction = new InteractionHandler(
                ipcClient, hitTest, blurManager, popupManager, autopause,
                wordAction, reviewService, colorizer, settings, osd, logger);

            ipcClient.SubtitleTextChanged += text =>
            {
                TaskHelper.CancelAndDispose(ref _currentSubtitleCts);
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _currentSubtitleCts = linkedCts;
                _ = OnSubtitleChangedAsync(text, ipcClient, colorizer,
                    measurer, interaction, linkedCts.Token)
                    .ContinueWith(_ => linkedCts.Dispose(), TaskScheduler.Default);
            };

            ipcClient.MouseEvent += e =>
            {
                _ = RunSafe(() => interaction.OnMouseEventAsync(e, ct));
            };

            ipcClient.PropertyChanged += (name, data) =>
            {
                if (data.ValueKind != JsonValueKind.Number) return;
                int value = data.GetInt32();
                bool changed = name switch
                {
                    "osd-width" => osd.Update(value, osd.Height),
                    "osd-height" => osd.Update(osd.Width, value),
                    _ => false
                };
                if (changed) renderer.RebuildPreamble();
            };

            var readLoop = ipcClient.RunAsync(ct);

            await ipcClient.SetPropertyAsync("sub-visibility", "no", ct);
            await ipcClient.ObservePropertyAsync("sub-text", 1, ct);
            await ipcClient.ObservePropertyAsync("osd-width", 2, ct);
            await ipcClient.ObservePropertyAsync("osd-height", 3, ct);

            var widthTask = ipcClient.GetPropertyAsync<int>("osd-width", ct);
            var heightTask = ipcClient.GetPropertyAsync<int>("osd-height", ct);
            osd.Update(await widthTask, await heightTask);
            renderer.RebuildPreamble();

            _ = RunSafe(() => StartPreParseAsync(ipcClient, preParser, ct));

            logger.LogInformation("JitenMPV plugin running.");
            await readLoop;
        }
        catch (OperationCanceledException) { }
        catch (IOException ex)
        {
            logger.LogError(ex, "IPC connection lost");
        }
        finally
        {
            try
            {
                using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var cct = cleanupCts.Token;
                await ipcClient.RemoveOverlayAsync(SubtitleOverlayId, cct);
                await ipcClient.RemoveOverlayAsync(StatusOverlay.StatusLayerId, cct);
                await ipcClient.SetPropertyAsync("sub-visibility", "yes", cct);
            }
            catch { }

            statusOverlay.Dispose();
            http.Dispose();
        }
    }

    private async Task StartPreParseAsync(MpvIpcClient ipc, PreParseService preParser, CancellationToken ct)
    {
        string? subFile = null;
        try
        {
            subFile = await ipc.GetPropertyAsync<string>("current-tracks/sub/external-filename", ct);
        }
        catch { }

        if (!string.IsNullOrEmpty(subFile))
            await preParser.PreParseFileAsync(subFile, ct);
        else
            await preParser.PreParseEmbeddedAsync(ipc, ct);
    }

    private Task RunSafe(Func<Task> action)
        => TaskHelper.RunSafe(action, logger);

    private async Task OnSubtitleChangedAsync(
        string? text, MpvIpcClient ipcClient,
        SubtitleColorizer colorizer,
        SubtitleMeasurer measurer, InteractionHandler interaction,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                await interaction.OnSubtitleRenderedAsync(null, null, [], ct);
                await ipcClient.RemoveOverlayAsync(SubtitleOverlayId, ct);
                return;
            }

            var (ass, entry) = await colorizer.ColorizeAsync(text, ct);
            await ipcClient.ShowOverlayAsync(SubtitleOverlayId, ass, ct);

            List<WordRect> layout = [];
            if (entry is not null)
                layout = await measurer.MeasureAsync(text, entry, ipcClient, ct);

            await interaction.OnSubtitleRenderedAsync(text, entry, layout, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing subtitle change");
        }
    }
}
