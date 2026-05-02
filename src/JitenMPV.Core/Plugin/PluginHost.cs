using JitenMPV.Core.Api;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Rendering;
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
        var renderer = new OverlayRenderer(settings);
        var colorizer = new SubtitleColorizer(apiClient, parseCache, renderer, logger);

        await using var ipcClient = new MpvIpcClient(pipePath, logger);

        try
        {
            logger.LogInformation("Connecting to mpv...");
            await ipcClient.ConnectAsync(ct);
            logger.LogInformation("Connected.");

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

            logger.LogInformation("Disabling native subs and observing sub-text.");
            await ipcClient.SetPropertyAsync("sub-visibility", "no", ct);
            await ipcClient.ObservePropertyAsync("sub-text", 1, ct);

            logger.LogInformation("JitenMPV plugin running.");
            await readLoop;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Plugin shutting down");
        }
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