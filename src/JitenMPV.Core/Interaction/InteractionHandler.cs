using System.Diagnostics;
using JitenMPV.Core.Api.Models;
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
    private const string LuaTarget = "jiten_mpv";
    private const string ClickPassthrough = "jiten-passthrough-click";
    private const string DoubleClickPassthrough = "jiten-passthrough-dbl";

    private readonly MpvIpcClient _ipc;
    private readonly HitTestService _hitTest;
    private readonly BlurHoverManager _blur;
    private readonly PopupManager _popup;
    private readonly AutopauseService _autopause;
    private readonly WordActionService _wordAction;
    private readonly InlineReviewService _review;
    private readonly MiningService _mining;
    private readonly RotationService _rotation;
    private readonly SubtitleColorizer _colorizer;
    private volatile PluginSettings _settings;
    private readonly ILogger _logger;
    private readonly OsdState _osd;

    private readonly Stopwatch _moveStopwatch = Stopwatch.StartNew();
    private readonly SemaphoreSlim _eventLock = new(1, 1);
    private long _lastMoveMs;

    private string? _currentText;
    private ParseCacheEntry? _currentEntry;

    private WordRect? _popupWord;
    private WordRect? _pendingWord;

    private CancellationTokenSource? _hoverPopupCts;
    private CancellationTokenSource? _autoHideCts;

    public InteractionHandler(
        MpvIpcClient ipc, HitTestService hitTest,
        BlurHoverManager blur, PopupManager popup, AutopauseService autopause,
        WordActionService wordAction, InlineReviewService review, MiningService mining,
        RotationService rotation, SubtitleColorizer colorizer,
        PluginSettings settings, OsdState osd, ILogger logger)
    {
        _ipc = ipc;
        _hitTest = hitTest;
        _blur = blur;
        _popup = popup;
        _autopause = autopause;
        _wordAction = wordAction;
        _review = review;
        _mining = mining;
        _rotation = rotation;
        _colorizer = colorizer;
        _settings = settings;
        _osd = osd;
        _logger = logger;

        _blur.WordUnrevealed += () => _ = ReRenderSubtitleAsync(CancellationToken.None);
        _popup.ActionClicked += action => _ = RunSafe(() => ExecutePopupActionAsync(action, CancellationToken.None));
        _popup.DeckSelected += deckId => _ = RunSafe(() => MineCurrentWordAsync(deckId, CancellationToken.None));
    }

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    /// A click-triggered popup is dismissed by a click, not by the pointer wandering off it.
    private bool StickyPopup => _settings.PopupTrigger == PopupTriggerMode.Click;

    /// mpv routes a key to exactly one binding, so the Lua side claims MBTN_LEFT and MBTN_LEFT_DBL
    /// unconditionally and only replays the command they displaced once a click is known to miss.
    private Task PassThroughAsync(string message, CancellationToken ct)
        => _ipc.SendScriptMessageAsync(LuaTarget, message, ct);

    public void UpdateLayout(List<WordRect> layout) => _hitTest.UpdateLayout(layout);

    public async Task OnSubtitleRenderedAsync(string? text, ParseCacheEntry? entry,
                                    List<WordRect> layout, CancellationToken ct)
    {
        // Serialized against the mouse handlers via the same lock so shared state
        // (_currentEntry, autopause/blur internals, popup lifecycle) is never mutated concurrently.
        await _eventLock.WaitAsync(ct);
        try
        {
            _currentText = text;
            _currentEntry = entry;

            _blur.Reset();
            await _autopause.ResetAsync();
            CancelPendingPopup();
            TaskHelper.CancelAndDispose(ref _autoHideCts);
            _popupWord = null;

            await _popup.HideAsync(ct);

            _hitTest.UpdateLayout(layout);
        }
        finally
        {
            _eventLock.Release();
        }
    }

    /// Re-measurement of the line already on screen, after the OSD changed size. The popup, autopause
    /// state and blur reveals belong to that same line, so they outlive it; only the rectangles move.
    public async Task OnSubtitleLayoutChangedAsync(
        ParseCacheEntry? entry, List<WordRect> layout, CancellationToken ct)
    {
        await _eventLock.WaitAsync(ct);
        try
        {
            _currentEntry = entry;
            _hitTest.UpdateLayout(layout);

            // The re-render that produced this layout was colourised without the reveal, so a word
            // the pointer had uncovered would silently blur back over while still counted as revealed.
            if (_blur.HasRevealed)
                await ReRenderSubtitleAsync(ct);
        }
        finally
        {
            _eventLock.Release();
        }
    }

    public async Task OnMouseEventAsync(MouseEventArgs e, CancellationToken ct)
    {
        // Clicks arriving mid-action are swallowed rather than passed through: mpv would otherwise
        // fullscreen or pause on the impatient second double-click of a word still being mined.
        if (!await _eventLock.WaitAsync(0, ct))
        {
            if (e.Type is MouseEventType.LeftPress or MouseEventType.DoubleClick)
                _logger.LogDebug("Dropped {Type}: another interaction is in flight", e.Type);
            return;
        }

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

        // A click-triggered popup owns the interaction until a click dismisses it: its word stays
        // revealed and the video stays paused however far the pointer wanders in the meantime.
        if (StickyPopup && _popup.IsVisible && hit is null && !overPopup) return;

        if (hit is not null || overPopup)
            await _autopause.OnHoverEnterAsync(_ipc, ct);

        bool blurChanged = _blur.UpdateHover(hit, _currentEntry);
        if (blurChanged && _currentText is not null)
            await ReRenderSubtitleAsync(ct);

        if (hit is not null && _settings.PopupTrigger == PopupTriggerMode.Hover)
        {
            TaskHelper.CancelAndDispose(ref _autoHideCts);

            if (_popup.IsVisible && _popupWord?.TokenIndex == hit.TokenIndex)
            {
                CancelPendingPopup();
                return;
            }

            // Re-arming on every move would restart the countdown for as long as the pointer keeps
            // drifting inside one word, so the timer is only replaced when it is aimed elsewhere.
            if (_pendingWord?.TokenIndex == hit.TokenIndex) return;

            CancelPendingPopup();
            _pendingWord = hit;
            _hoverPopupCts = new CancellationTokenSource();
            var linked = CancellationTokenSource.CreateLinkedTokenSource(_hoverPopupCts.Token, ct);
            _ = ShowPopupAfterDelayAsync(
                hit, new PopupPointerPosition(mx, my), HoverDelayFor(hit), linked);
        }
        else if (hit is null && !overPopup)
        {
            CancelPendingPopup();
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
        CancelPendingPopup();
        TaskHelper.CancelAndDispose(ref _autoHideCts);

        if (StickyPopup && _popup.IsVisible) return;

        if (_popup.IsVisible)
            await _popup.HideAsync(ct);

        if (_currentEntry is not null)
            _blur.UpdateHover(null, _currentEntry);

        await _autopause.OnHoverLeaveAsync(_ipc, ct);

        if (_currentText is not null && _blur.HasRevealed)
            await ReRenderSubtitleAsync(ct);
    }

    /// A popup rendered clear of the subtitle puts a whole other line between itself and the word it
    /// describes, so reaching it means sweeping over words nobody asked about. Only a word the
    /// pointer settles on for the switch delay takes the popup over.
    private int HoverDelayFor(WordRect hit)
        => _popup.IsVisible && _popupWord is { } shown && OnDifferentLine(shown, hit)
            ? _settings.PopupSwitchDelayMs
            : _settings.PopupHoverDelayMs;

    private static bool OnDifferentLine(WordRect a, WordRect b)
        => Math.Abs(a.Y - b.Y) > Math.Max(a.Height, b.Height) * 0.5f;

    private void CancelPendingPopup()
    {
        TaskHelper.CancelAndDispose(ref _hoverPopupCts);
        _pendingWord = null;
    }

    private async Task ShowPopupAfterDelayAsync(
        WordRect hit, PopupPointerPosition pointer, int delayMs, CancellationTokenSource linkedCts)
    {
        try
        {
            await Task.Delay(delayMs, linkedCts.Token);

            // The pointer can be inside the popup by the time a cross-line switch comes due, and
            // swapping the entry out from under it is what the delay exists to prevent.
            if (_popup.IsMouseOverPopup) return;

            if (_currentEntry is not null)
            {
                await _popup.ShowAsync(hit, _currentEntry, pointer, linkedCts.Token);
                _popupWord = hit;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_pendingWord?.TokenIndex == hit.TokenIndex) _pendingWord = null;
            linkedCts.Dispose();
        }
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
        var entry = _currentEntry;
        if (entry is null || _osd.Height <= 0)
        {
            await PassThroughAsync(ClickPassthrough, ct);
            return;
        }

        var hit = _hitTest.HitTest(mx, my, _osd.Width, _osd.Height);
        _logger.LogDebug("Click ({MX:F0},{MY:F0}) → {Result}",
            mx, my, hit is not null ? $"word {hit.WordId}" : "MISS");

        if (hit is not null)
        {
            if (_settings.PopupTrigger != PopupTriggerMode.Hover)
            {
                await _popup.ShowAsync(
                    hit, entry, new PopupPointerPosition(mx, my), ct);
                _popupWord = hit;
            }
            return;
        }

        bool dismissed = _popup.IsVisible && !_popup.IsMouseOverPopup;
        if (dismissed)
        {
            await _popup.HideAsync(ct);
            await _autopause.OnHoverLeaveAsync(_ipc, ct);

            // Pointer moves are ignored while a sticky popup is up, so the reveal it left behind
            // would outlive it until the next move if it were not undone here.
            if (_blur.UpdateHover(null, entry) && _currentText is not null)
                await ReRenderSubtitleAsync(ct);
        }

        // The click that closes a sticky popup is spent on closing it; passing it on as well would
        // pause or fullscreen behind the entry the user just dismissed.
        if (!dismissed || !StickyPopup)
            await PassThroughAsync(ClickPassthrough, ct);
    }

    private async Task HandleDoubleClickAsync(double mx, double my, CancellationToken ct)
    {
        var entry = _currentEntry;
        var text = _currentText;
        var action = _settings.DoubleClickAction;

        var hit = entry is not null && text is not null && action != DoubleClickAction.None
            ? _hitTest.HitTest(mx, my, _osd.Width, _osd.Height)
            : null;

        if (hit is null || entry is null || text is null)
        {
            await PassThroughAsync(DoubleClickPassthrough, ct);
            return;
        }

        if (action == DoubleClickAction.Mine)
        {
            await _mining.MineWithConfiguredDeckAsync(
                hit.WordId, hit.ReadingIndex, text, _ipc, ct);
        }
        else
        {
            var key = (hit.WordId, hit.ReadingIndex);
            var state = entry.VocabStates.GetValueOrDefault(key);
            if (state == KnownState.Redundant) return;

            await _wordAction.SetStateAsync(
                hit.WordId, hit.ReadingIndex, PopupAction.NeverForget,
                state, text, _ipc, ct);
        }

        if (_settings.PopupHideAfterAction && _popup.IsVisible)
            await _popup.HideAsync(ct);
        else if (_popup.IsVisible && _currentEntry is not null)
            await _popup.RefreshAsync(_currentEntry, ct);

        await ReRenderSubtitleAsync(ct);
    }

    private async Task MineCurrentWordAsync(int deckId, CancellationToken ct)
    {
        await _eventLock.WaitAsync(ct);
        try
        {
            if (_popup.CurrentWord is not { } key) return;
            await _mining.MineAsync(key.WordId, key.ReadingIndex, deckId, _currentText, _ipc, ct);

            if (_settings.PopupHideAfterAction)
                await _popup.HideAsync(ct);
            else if (_currentEntry is not null)
                await _popup.RefreshAsync(_currentEntry, ct);
        }
        finally
        {
            _eventLock.Release();
        }
    }

    public async Task ExecutePopupActionAsync(PopupAction action, CancellationToken ct)
    {
        if (_currentEntry is null || _currentText is null) return;

        // Keybinds are reconfigured asynchronously in the Lua process, so a grade can still arrive
        // for a short window after reviews are switched off.
        if (action.IsReview() && !_settings.ReviewsEnabled) return;

        var key = _popup.CurrentWord;
        if (key is null) return;

        int wordId = key.Value.WordId;
        byte readingIndex = key.Value.ReadingIndex;
        var state = _currentEntry.VocabStates.GetValueOrDefault((wordId, readingIndex));

        // A redundant word has no card of its own, so nothing can be graded or restated on it and
        // its rows are hidden. The keybinds stay bound regardless, so refuse here too. Mining falls
        // through to MiningService, which says why it was skipped instead of going silent.
        if (state == KnownState.Redundant && action != PopupAction.Mine) return;

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
            case PopupAction.Mine:
                await _mining.MineWithConfiguredDeckAsync(wordId, readingIndex, _currentText, _ipc, ct);
                break;
            case PopupAction.RotateForward:
                await RotateStateAsync(wordId, readingIndex, state, 1, ct);
                break;
            case PopupAction.RotateBackward:
                await RotateStateAsync(wordId, readingIndex, state, -1, ct);
                break;
        }

        // Silent when no deck is configured: auto-mining must not nag on every grade.
        if (action.IsReview() && _settings.MiningAutoOnReview
            && _mining.ResolveTargetDeck() is { } autoDeck)
        {
            await _mining.MineAsync(wordId, readingIndex, autoDeck, _currentText, _ipc, ct,
                reportSkip: false);
        }

        if (_settings.PopupHideAfterAction)
            await _popup.HideAsync(ct);
        else if (_currentEntry is not null)
            await _popup.RefreshAsync(_currentEntry, ct);

        await ReRenderSubtitleAsync(ct);
    }

    /// Moves the card to the next slot in the rotation cycle. SetStateAsync toggles against the
    /// state it is handed, so clearing passes the state that means "set" and setting passes New.
    private async Task RotateStateAsync(
        int wordId, byte readingIndex, KnownState state, int direction, CancellationToken ct)
    {
        if (!_rotation.TryGetNext(state, direction, out var target)) return;

        var current = RotationService.StateOf(state);
        if (current == target) return;

        if (current is { } clear)
            await _wordAction.SetStateAsync(
                wordId, readingIndex, clear, state, _currentText!, _ipc, ct);

        if (target is { } set)
            await _wordAction.SetStateAsync(
                wordId, readingIndex, set, KnownState.New, _currentText!, _ipc, ct);
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
