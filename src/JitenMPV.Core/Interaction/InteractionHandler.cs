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

    public void UpdateLayout(List<WordRect> layout) => SetLayout(layout);

    /// The Lua side short-circuits clicks outside this union straight to mpv's own binding, so it
    /// must be kept in step with every layout the hit test can answer for.
    private void SetLayout(List<WordRect> layout)
    {
        _hitTest.UpdateLayout(layout);
        _ = TaskHelper.RunSafe(
            () => PushHitBoundsAsync(layout, CancellationToken.None), _logger, "Push hit bounds");
    }

    private Task PushHitBoundsAsync(IReadOnlyList<WordRect> layout, CancellationToken ct)
    {
        if (layout.Count == 0)
            return _ipc.SendScriptMessageAsync(LuaTarget, "jiten-set-hit-bounds", "clear", ct);

        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var rect in layout)
        {
            x0 = Math.Min(x0, rect.HitX0);
            y0 = Math.Min(y0, rect.HitY0);
            x1 = Math.Max(x1, rect.HitX1);
            y1 = Math.Max(y1, rect.HitY1);
        }

        return _ipc.SendScriptMessageAsync(LuaTarget, "jiten-set-hit-bounds",
            [Inv(x0), Inv(y0), Inv(x1), Inv(y1)], ct);

        static string Inv(float v) => v.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
    }

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

            SetLayout(layout);
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
            SetLayout(layout);

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
        // Clicks arriving mid-action on a word are swallowed rather than passed through: mpv would
        // otherwise fullscreen or pause on the impatient second double-click of a word still being
        // mined. Clicks that miss every word are handed back to mpv even while busy, so a seek or
        // pause never silently disappears; the hit test reads an atomically-swapped list, so it is
        // safe without the lock.
        if (!await _eventLock.WaitAsync(0, ct))
        {
            if (e.Type is MouseEventType.LeftPress or MouseEventType.DoubleClick)
            {
                if (_hitTest.HitTest(e.X, e.Y, _osd.Width, _osd.Height) is null)
                {
                    _logger.LogDebug("Busy {Type} missed every word: passed through", e.Type);
                    await PassThroughAsync(
                        e.Type == MouseEventType.LeftPress ? ClickPassthrough : DoubleClickPassthrough, ct);
                }
                else
                {
                    _logger.LogDebug("Dropped {Type} on word: another interaction is in flight", e.Type);
                }
            }
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

        // A line that ends under the pointer leaves nothing to hit-test, so a hover still holding
        // the video paused has to be ended here: no later move can reach the code that would.
        if (_currentEntry is null || _osd.Height <= 0)
        {
            await ReleaseHoverAsync(ct);
            return;
        }

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
            await ReleaseHoverAsync(ct);
        }
        else if (overPopup)
        {
            TaskHelper.CancelAndDispose(ref _autoHideCts);
        }
    }

    private async Task HandleLeaveAsync(CancellationToken ct)
    {
        CancelPendingPopup();

        if (StickyPopup && _popup.IsVisible) return;

        if (!_popup.IsVisible)
            TaskHelper.CancelAndDispose(ref _autoHideCts);

        await ReleaseHoverAsync(ct);

        if (_currentEntry is not null)
            _blur.UpdateHover(null, _currentEntry);

        if (_currentText is not null && _blur.HasRevealed)
            await ReRenderSubtitleAsync(ct);
    }

    /// <summary>
    /// Ends the hover the pointer has moved off, or defers that end for as long as the word's entry
    /// is still on screen. The popup is a window of its own, so the pointer reaches it by leaving
    /// mpv's: a leave means "arrived at the entry" as often as "gone", and letting playback resume
    /// on either would carry the line the entry describes away mid-read.
    /// </summary>
    private async Task ReleaseHoverAsync(CancellationToken ct)
    {
        if (_popup.IsVisible)
        {
            if (_settings.PopupAutoHide && _settings.PopupAutoHideDelayMs > 0)
            {
                ArmAutoHide(ct);
                return;
            }

            if (_popup.IsMouseOverPopup) return;
            await _popup.HideAsync(ct);
        }

        await _autopause.OnHoverLeaveAsync(_ipc, ct);
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

    private void ArmAutoHide(CancellationToken ct)
    {
        TaskHelper.CancelAndDispose(ref _autoHideCts);
        _autoHideCts = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(_autoHideCts.Token, ct);
        _ = HidePopupAfterDelayAsync(linked);
    }

    private async Task HidePopupAfterDelayAsync(CancellationTokenSource linkedCts)
    {
        try
        {
            do
            {
                await Task.Delay(Math.Max(1, _settings.PopupAutoHideDelayMs), linkedCts.Token);
            }
            while (_popup.IsMouseOverPopup);

            if (_popup.IsVisible)
            {
                await _popup.HideAsync(linkedCts.Token);
                await _autopause.OnHoverLeaveAsync(_ipc, linkedCts.Token);
            }
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
