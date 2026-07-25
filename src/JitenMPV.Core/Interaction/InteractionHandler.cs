using System.Diagnostics;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Rendering;
using JitenMPV.Core.Plugin;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Interaction;

public sealed class InteractionHandler : IDisposable
{
    private const int SubtitleOverlayId = PluginHost.SubtitleOverlayId;
    private const long DebounceMs = 16;

    private readonly MpvIpcClient _ipc;
    private readonly HitTestService _hitTest;
    private readonly BlurHoverManager _blur;
    private readonly PopupManager _popup;
    private readonly AutopauseService _autopause;
    private readonly WordActionService _wordAction;
    private readonly InlineReviewService _review;
    private readonly SubtitleColorizer _colorizer;
    private volatile PluginSettings _settings;
    private readonly ILogger _logger;
    private readonly OsdState _osd;

    private readonly Stopwatch _moveStopwatch = Stopwatch.StartNew();
    private readonly SemaphoreSlim _eventLock = new(1, 1);
    private long _lastMoveMs;

    private string? _currentText;
    private ParseCacheEntry? _currentEntry;

    private CancellationTokenSource? _hoverPopupCts;
    private CancellationTokenSource? _autoHideCts;

    public InteractionHandler(
        MpvIpcClient ipc, HitTestService hitTest,
        BlurHoverManager blur, PopupManager popup, AutopauseService autopause,
        WordActionService wordAction, InlineReviewService review,
        SubtitleColorizer colorizer,
        PluginSettings settings, OsdState osd, ILogger logger)
    {
        _ipc = ipc;
        _hitTest = hitTest;
        _blur = blur;
        _popup = popup;
        _autopause = autopause;
        _wordAction = wordAction;
        _review = review;
        _colorizer = colorizer;
        _settings = settings;
        _osd = osd;
        _logger = logger;

        _blur.WordUnrevealed += () => _ = ReRenderSubtitleAsync(CancellationToken.None);
        _popup.ActionClicked += action => _ = RunSafe(() => ExecutePopupActionAsync(action, CancellationToken.None));
    }

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    public void UpdateLayout(List<WordRect> layout) => _hitTest.UpdateLayout(layout);

    public async Task OnSubtitleRenderedAsync(string? text, ParseCacheEntry? entry,
                                    List<WordRect> layout, CancellationToken ct)
    {
        // Serialized against the mouse handlers via the same lock so shared state
        // (_currentEntry, autopause/blur internals, popup lifecycle) is never mutated concurrently.
        await _eventLock.WaitAsync(ct);
        try
        {
            bool textChanged = text != _currentText;
            _currentText = text;
            _currentEntry = entry;

            _blur.Reset();
            await _autopause.ResetAsync();
            TaskHelper.CancelAndDispose(ref _hoverPopupCts);
            TaskHelper.CancelAndDispose(ref _autoHideCts);

            await _popup.HideAsync(ct);

            if (textChanged)
                _hitTest.UpdateLayout(layout);
        }
        finally
        {
            _eventLock.Release();
        }
    }

    public async Task OnMouseEventAsync(MouseEventArgs e, CancellationToken ct)
    {
        if (!await _eventLock.WaitAsync(0, ct)) return;
        try
        {
            switch (e.Type)
            {
                case MouseEventType.Move:
                    await HandleMoveAsync(e.X, e.Y, ct);
                    break;
                case MouseEventType.LeftPress:
                    await HandleClickAsync(e.X, e.Y, ct);
                    break;
                case MouseEventType.DoubleClick:
                    await HandleDoubleClickAsync(e.X, e.Y, ct);
                    break;
                case MouseEventType.Leave:
                    await HandleLeaveAsync(ct);
                    break;
            }
        }
        finally
        {
            _eventLock.Release();
        }
    }

    private async Task HandleMoveAsync(double mx, double my, CancellationToken ct)
    {
        var now = _moveStopwatch.ElapsedMilliseconds;
        if (now - _lastMoveMs < DebounceMs) return;
        _lastMoveMs = now;

        if (_currentEntry is null || _osd.Height <= 0) return;

        var hit = _hitTest.HitTest(mx, my, _osd.Width, _osd.Height);
        bool overPopup = _popup.IsVisible && _popup.IsMouseOverPopup;

        if (hit is not null || overPopup)
            await _autopause.OnHoverEnterAsync(_ipc, ct);

        bool blurChanged = _blur.UpdateHover(hit, _currentEntry);
        if (blurChanged && _currentText is not null)
            await ReRenderSubtitleAsync(ct);

        if (hit is not null && _settings.PopupTrigger == PopupTriggerMode.Hover)
        {
            TaskHelper.CancelAndDispose(ref _autoHideCts);

            if (_popup.IsVisible)
            {
                await _popup.ShowAsync(hit, _currentEntry, ct);
            }
            else
            {
                TaskHelper.CancelAndDispose(ref _hoverPopupCts);
                _hoverPopupCts = new CancellationTokenSource();
                var linked = CancellationTokenSource.CreateLinkedTokenSource(_hoverPopupCts.Token, ct);
                _ = ShowPopupAfterDelayAsync(hit, linked);
            }
        }
        else if (hit is null && !overPopup)
        {
            TaskHelper.CancelAndDispose(ref _hoverPopupCts);
            await _autopause.OnHoverLeaveAsync(_ipc, ct);

            if (_popup.IsVisible)
            {
                if (_settings.PopupAutoHide && _settings.PopupAutoHideDelayMs > 0)
                {
                    TaskHelper.CancelAndDispose(ref _autoHideCts);
                    _autoHideCts = new CancellationTokenSource();
                    var linked = CancellationTokenSource.CreateLinkedTokenSource(_autoHideCts.Token, ct);
                    _ = HidePopupAfterDelayAsync(linked);
                }
                else
                {
                    await _popup.HideAsync(ct);
                }
            }
        }
        else if (overPopup)
        {
            TaskHelper.CancelAndDispose(ref _autoHideCts);
        }
    }

    private async Task HandleLeaveAsync(CancellationToken ct)
    {
        TaskHelper.CancelAndDispose(ref _hoverPopupCts);
        TaskHelper.CancelAndDispose(ref _autoHideCts);

        if (_popup.IsVisible)
            await _popup.HideAsync(ct);

        if (_currentEntry is not null)
            _blur.UpdateHover(null, _currentEntry);

        await _autopause.OnHoverLeaveAsync(_ipc, ct);

        if (_currentText is not null && _blur.HasRevealed)
            await ReRenderSubtitleAsync(ct);
    }

    private async Task ShowPopupAfterDelayAsync(WordRect hit, CancellationTokenSource linkedCts)
    {
        try
        {
            await Task.Delay(_settings.PopupHoverDelayMs, linkedCts.Token);
            if (_currentEntry is not null)
                await _popup.ShowAsync(hit, _currentEntry, linkedCts.Token);
        }
        catch (OperationCanceledException) { }
        finally { linkedCts.Dispose(); }
    }

    private async Task HidePopupAfterDelayAsync(CancellationTokenSource linkedCts)
    {
        try
        {
            await Task.Delay(_settings.PopupAutoHideDelayMs, linkedCts.Token);
            if (_popup.IsVisible)
                await _popup.HideAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) { }
        finally { linkedCts.Dispose(); }
    }

    private async Task HandleClickAsync(double mx, double my, CancellationToken ct)
    {
        if (_currentEntry is null || _osd.Height <= 0) return;

        if (_popup.IsVisible && !_popup.IsMouseOverPopup)
            await _popup.HideAsync(ct);

        var hit = _hitTest.HitTest(mx, my, _osd.Width, _osd.Height);
        _logger.LogDebug("Click ({MX:F0},{MY:F0}) → {Result}",
            mx, my, hit is not null ? $"word {hit.WordId}" : "MISS");
        if (hit is null) return;

        if (_settings.PopupTrigger != PopupTriggerMode.Hover)
            await _popup.ShowAsync(hit, _currentEntry, ct);
    }

    private async Task HandleDoubleClickAsync(double mx, double my, CancellationToken ct)
    {
        if (_currentEntry is null || _currentText is null) return;

        var hit = _hitTest.HitTest(mx, my, _osd.Width, _osd.Height);
        if (hit is null) return;

        var key = (hit.WordId, hit.ReadingIndex);
        var state = _currentEntry.VocabStates.GetValueOrDefault(key);
        await _wordAction.SetStateAsync(
            hit.WordId, hit.ReadingIndex, PopupAction.NeverForget,
            state, _currentText, _ipc, ct);

        if (_settings.PopupHideAfterAction && _popup.IsVisible)
            await _popup.HideAsync(ct);

        await ReRenderSubtitleAsync(ct);
    }

    public async Task ExecutePopupActionAsync(PopupAction action, CancellationToken ct)
    {
        if (_currentEntry is null || _currentText is null) return;

        var key = _popup.CurrentWord;
        if (key is null) return;

        int wordId = key.Value.WordId;
        byte readingIndex = key.Value.ReadingIndex;
        var state = _currentEntry.VocabStates.GetValueOrDefault((wordId, readingIndex));

        switch (action)
        {
            case PopupAction.NeverForget:
            case PopupAction.Blacklist:
            case PopupAction.Suspend:
            case PopupAction.Forget:
                await _wordAction.SetStateAsync(
                    wordId, readingIndex, action, state, _currentText, _ipc, ct);
                break;
            case PopupAction.ReviewAgain:
                await _review.ReviewAsync(wordId, readingIndex, 1, _ipc, ct);
                break;
            case PopupAction.ReviewHard:
                await _review.ReviewAsync(wordId, readingIndex, 2, _ipc, ct);
                break;
            case PopupAction.ReviewGood:
                await _review.ReviewAsync(wordId, readingIndex, 3, _ipc, ct);
                break;
            case PopupAction.ReviewEasy:
                await _review.ReviewAsync(wordId, readingIndex, 4, _ipc, ct);
                break;
        }

        if (_settings.PopupHideAfterAction)
            await _popup.HideAsync(ct);
        else if (_currentEntry is not null)
            await _popup.RefreshAsync(_currentEntry, ct);

        await ReRenderSubtitleAsync(ct);
    }

    private async Task ReRenderSubtitleAsync(CancellationToken ct)
    {
        if (_currentText is null) return;

        try
        {
            var (ass, _) = await _colorizer.ColorizeWithRevealAsync(
                _currentText, _blur.GetRevealedSnapshot(), ct);
            await _ipc.ShowOverlayAsync(SubtitleOverlayId, ass, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to re-render subtitle");
        }
    }

    private Task RunSafe(Func<Task> action)
        => TaskHelper.RunSafe(action, _logger, "Popup action");

    public void Dispose()
    {
        TaskHelper.CancelAndDispose(ref _hoverPopupCts);
        TaskHelper.CancelAndDispose(ref _autoHideCts);
        _eventLock.Dispose();
    }
}
