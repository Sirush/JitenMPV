using JitenMPV.Core.Api;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Rendering;
using JitenMPV.Core.Theming;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plugin;

public sealed class PluginHost(string pipePath, ILogger logger)
{
    private const int SubtitleOverlayId = 1;

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

        var renderer = new OverlayRenderer(settings, styleResolver);
        var iPlusOne = settings.IPlusOneEnabled
            ? new IPlusOneDetector(settings.IPlusOneMinTokens, settings.IPlusOneMaxFrequencyRank)
            : null;
        var freqMarker = settings.FrequencyMarkingEnabled
            ? new FrequencyMarker(settings.FrequencyTopN, settings.FrequencyMarkAllStates)
            : null;

        var colorizer = new SubtitleColorizer(apiClient, parseCache, renderer, iPlusOne, freqMarker, logger);
        var preParser = new PreParseService(apiClient, parseCache, logger);

        await using var ipcClient = new MpvIpcClient(pipePath, logger);

        try
        {
            await ipcClient.ConnectAsync(ct);
            logger.LogInformation("Connected to mpv");

            ipcClient.SubtitleTextChanged += text =>
            {
                var prev = _currentSubtitleCts;
                prev?.Cancel();
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _currentSubtitleCts = linkedCts;
                _ = OnSubtitleChangedAsync(text, ipcClient, colorizer, linkedCts.Token);
                prev?.Dispose();
            };

            var readLoop = ipcClient.RunAsync(ct);

            await ipcClient.SetPropertyAsync("sub-visibility", "no", ct);
            await ipcClient.ObservePropertyAsync("sub-text", 1, ct);

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
                await ipcClient.RemoveOverlayAsync(SubtitleOverlayId, cleanupCts.Token);
                await ipcClient.SetPropertyAsync("sub-visibility", "yes", cleanupCts.Token);
            }
            catch
            {
                // best-effort cleanup
            }

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
        catch { /* property may not exist */ }

        if (!string.IsNullOrEmpty(subFile))
            await preParser.PreParseFileAsync(subFile, ct);
        else
            await preParser.PreParseEmbeddedAsync(ipc, ct);
    }

    private async Task RunSafe(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background task failed");
        }
    }

    private async Task OnSubtitleChangedAsync(
        string? text,
        MpvIpcClient ipcClient,
        SubtitleColorizer colorizer,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                await ipcClient.RemoveOverlayAsync(SubtitleOverlayId, ct);
                return;
            }

            var ass = await colorizer.ColorizeAsync(text, ct);
            await ipcClient.ShowOverlayAsync(SubtitleOverlayId, ass, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing subtitle change");
        }
    }
}