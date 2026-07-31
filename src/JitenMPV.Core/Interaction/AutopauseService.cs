using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Interaction;

public sealed class AutopauseService(PluginSettings settings, ILogger logger)
{
    private volatile PluginSettings _settings = settings;

    /// Serializes the pause decision against the delayed pause task, so a hover-leave and the
    /// pause it is meant to cancel can never both take effect.
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    private bool _isPausedByUs;
    private bool _wasAlreadyPaused;
    private bool _isHovering;
    private CancellationTokenSource? _delayCts;

    public async Task OnHoverEnterAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        if (!_settings.AutopauseEnabled) return;

        await _stateLock.WaitAsync(ct);
        try
        {
            if (_isPausedByUs || _isHovering) return;
            _isHovering = true;

            TaskHelper.CancelAndDispose(ref _delayCts);

            if (await ipc.GetPropertyAsync<bool>("pause", ct))
            {
                _wasAlreadyPaused = true;
                return;
            }

            _wasAlreadyPaused = false;

            int delayMs = _settings.AutopauseDelayMs;
            if (delayMs <= 0)
            {
                await ipc.SetPropertyAsync("pause", true, ct);
                _isPausedByUs = true;
                return;
            }

            _delayCts = new CancellationTokenSource();
            // Detached: the caller holds the interaction lock, and awaiting the delay under it
            // would drop the mouse-leave event that cancels this pause.
            _ = PauseAfterDelayAsync(ipc, delayMs, _delayCts.Token);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task PauseAfterDelayAsync(MpvIpcClient ipc, int delayMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMs, token);

            await _stateLock.WaitAsync(token);
            try
            {
                if (!_isHovering || _isPausedByUs) return;
                token.ThrowIfCancellationRequested();

                // Uncancellable once decided: a pause that lands without _isPausedByUs being set
                // would leave mpv paused with nothing left to unpause it.
                await ipc.SetPropertyAsync("pause", true, CancellationToken.None);
                _isPausedByUs = true;
            }
            finally
            {
                _stateLock.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delayed autopause failed");
        }
    }

    public async Task OnHoverLeaveAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            _isHovering = false;
            TaskHelper.CancelAndDispose(ref _delayCts);

            if (!_isPausedByUs || _wasAlreadyPaused) return;

            await ipc.SetPropertyAsync("pause", false, CancellationToken.None);
            _isPausedByUs = false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Ends the hover a new subtitle line supersedes. A pause this service owns deliberately
    /// outlives it: the line can change while the pause is still in flight, or under a seek, and
    /// forgetting it there would strand mpv paused with nothing left that is allowed to resume it.
    /// </summary>
    public async Task ResetAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            _isHovering = false;
            TaskHelper.CancelAndDispose(ref _delayCts);
        }
        finally
        {
            _stateLock.Release();
        }
    }
}
