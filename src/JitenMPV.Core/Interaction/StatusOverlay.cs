using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;

namespace JitenMPV.Core.Interaction;

public sealed class StatusOverlay(PluginSettings settings) : IDisposable
{
    private volatile PluginSettings _settings = settings;

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    public const int StatusLayerId = 5;
    private CancellationTokenSource? _hideCts;

    public async Task ShowAsync(MpvIpcClient ipc, string message, int durationMs, CancellationToken ct)
    {
        if (!_settings.StatusOverlayEnabled) return;
        TaskHelper.CancelAndDispose(ref _hideCts);

        var ass = $@"{{\an9\fs{(int)(_settings.FontSize * 0.6)}\fn{_settings.FontFamily}\bord2\1c&HFFFFFF&\3c&H000000&}}{message}";
        await ipc.ShowOverlayAsync(StatusLayerId, ass, ct);

        _hideCts = new CancellationTokenSource();
        var cts = _hideCts;

        _ = HideAfterDelayAsync(ipc, durationMs, cts);
    }

    private async Task HideAfterDelayAsync(MpvIpcClient ipc, int durationMs, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(durationMs, cts.Token);
            await ipc.RemoveOverlayAsync(StatusLayerId, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (Interlocked.CompareExchange(ref _hideCts, null, cts) == cts)
                cts.Dispose();
        }
    }

    public async Task HideAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        TaskHelper.CancelAndDispose(ref _hideCts);
        await ipc.RemoveOverlayAsync(StatusLayerId, ct);
    }

    public void Dispose()
    {
        TaskHelper.CancelAndDispose(ref _hideCts);
    }
}
