using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;

namespace JitenMPV.Core.Interaction;

public sealed class AutopauseService(PluginSettings settings)
{
    private bool _isPausedByUs;
    private bool _wasAlreadyPaused;
    private bool _isHovering;
    private CancellationTokenSource? _delayCts;

    public async Task OnHoverEnterAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        if (!settings.AutopauseEnabled || _isPausedByUs || _isHovering) return;
        _isHovering = true;

        TaskHelper.CancelAndDispose(ref _delayCts);

        var alreadyPaused = await ipc.GetPropertyAsync<bool>("pause", ct);
        if (alreadyPaused)
        {
            _wasAlreadyPaused = true;
            return;
        }

        _wasAlreadyPaused = false;

        if (settings.AutopauseDelayMs > 0)
        {
            _delayCts = new CancellationTokenSource();
            var cts = _delayCts;
            try
            {
                await Task.Delay(settings.AutopauseDelayMs, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        await ipc.SetPropertyAsync("pause", true, ct);
        _isPausedByUs = true;
    }

    public async Task OnHoverLeaveAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        _isHovering = false;
        TaskHelper.CancelAndDispose(ref _delayCts);

        if (!_isPausedByUs || _wasAlreadyPaused) return;

        await ipc.SetPropertyAsync("pause", false, ct);
        _isPausedByUs = false;
    }

    public void Reset()
    {
        _isHovering = false;
        TaskHelper.CancelAndDispose(ref _delayCts);
        _isPausedByUs = false;
        _wasAlreadyPaused = false;
    }
}
