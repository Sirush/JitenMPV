using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using JitenMPV.App.Platform;
using JitenMPV.App.ViewModels;
using JitenMPV.App.Views;
using JitenMPV.Core.Config;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Popup;

public sealed class AvaloniaPopupPresenter : IPopupPresenter
{
    private DictionaryPopupWindow? _window;
    private PopupViewModel? _viewModel;
    private volatile bool _isVisible;
    private PixelPoint? _lastCursorPos;
    private PopupPositionMode _positionMode = PopupPositionMode.AboveSubtitle;
    private PopupAnchor _fixedAnchor = PopupAnchor.TopCenter;
    private int _offsetPx = 60;
    private double _lastFontScale = -1;
    private int _lastMaxWidth = -1;
    private volatile PopupWindowContext _windowContext = PopupWindowContext.Empty;
    private bool _repositionQueued;

    public bool IsVisible => _isVisible;

    public event Action<PopupAction>? ActionClicked;
    public event Action<int>? DeckSelected;
    public event Action? MouseEntered;
    public event Action? MouseLeft;

    public void UpdateWindowContext(PopupWindowContext context)
    {
        _windowContext = context;
        Dispatcher.UIThread.Post(() =>
        {
            if (_window?.IsVisible == true)
                X11MpvWindowBridge.SetTransientOwner(_window, _windowContext.WindowId);
        });
    }

    public Task ShowAsync(PopupData data, PopupPointerPosition pointer, CancellationToken ct)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            EnsureWindow();

            _positionMode = data.PositionMode;
            _fixedAnchor = data.FixedAnchor;
            _offsetPx = data.OffsetPx;
            _viewModel!.Update(data);
            ApplyFontScale(data.FontScale);
            ApplyMaxWidth(data.MaxWidthPx);
            _lastCursorPos = ResolveCursorPosition(pointer);

            PositionWindow(_lastCursorPos);

            if (!_window!.IsVisible)
                _window.Show();

            _isVisible = true;
            X11MpvWindowBridge.SetTransientOwner(_window, _windowContext.WindowId);
            QueuePositionWindow();
        }).GetTask();
    }

    public Task UpdateAsync(PopupData data, CancellationToken ct)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            EnsureWindow();
            _viewModel!.Update(data);
            ApplyFontScale(data.FontScale);
            ApplyMaxWidth(data.MaxWidthPx);
        }).GetTask();
    }

    public Task HideAsync(CancellationToken ct)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isVisible = false;
            _viewModel?.CloseDeckPicker();
            _window?.Hide();
        }).GetTask();
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;

        _viewModel = new PopupViewModel();
        _viewModel.ActionClicked += action => ActionClicked?.Invoke(action);
        _viewModel.DeckSelected += deckId => DeckSelected?.Invoke(deckId);

        _window = new DictionaryPopupWindow { DataContext = _viewModel };
        _lastFontScale = -1;

        _window.PointerEntered += (_, _) => MouseEntered?.Invoke();
        _window.PointerExited += (_, _) => MouseLeft?.Invoke();
        _window.SizeChanged += (_, _) => QueuePositionWindow();
    }

    private void QueuePositionWindow()
    {
        if (_repositionQueued) return;
        _repositionQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _repositionQueued = false;
            if (_isVisible) PositionWindow(_lastCursorPos);
        }, DispatcherPriority.Render);
    }

    private PixelPoint? ResolveCursorPosition(PopupPointerPosition pointer)
    {
        if (!OperatingSystem.IsLinux())
            return CursorPositionHelper.GetCursorPosition();

        // Native Wayland does not expose a global position to this XWayland process. An absent mpv
        // XID therefore means "anchor deterministically", not "reuse X11's last known pointer".
        var translated = X11MpvWindowBridge.TranslateToRoot(
            _windowContext.WindowId, pointer.X, pointer.Y);
        return translated ?? (_windowContext.WindowId is > 0
            ? CursorPositionHelper.GetCursorPosition()
            : null);
    }

    private void ApplyFontScale(double scale)
    {
        if (_window is null || scale == _lastFontScale) return;
        _lastFontScale = scale;
        var container = _window.FindControl<LayoutTransformControl>("ScaleContainer");
        if (container is not null)
            container.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void ApplyMaxWidth(int maxWidthPx)
    {
        if (_window is null || maxWidthPx == _lastMaxWidth || maxWidthPx <= 0) return;
        _lastMaxWidth = maxWidthPx;
        _window.MaxWidth = maxWidthPx;
    }

    private void PositionWindow(PixelPoint? cursorPos)
    {
        if (_window is null) return;

        var screen = cursorPos is { } known
            ? _window.Screens.ScreenFromPoint(known)
            : ScreenFromMpvDisplayName() ?? _window.Screens.Primary;
        if (screen is null) return;

        var workArea = screen.WorkingArea;
        var scaling = screen.Scaling;

        var bounds = _window.Bounds.Size;
        int windowWidth = bounds.Width > 0 ? (int)(bounds.Width * scaling) : 350;
        int windowHeight = bounds.Height > 0 ? (int)(bounds.Height * scaling) : 250;

        // Without a cursor there is nothing to be relative to, and the clamped result would pin the
        // popup to the top-left corner. Anchoring it near the subtitles keeps it usable on systems
        // that cannot report a global pointer position, such as a Wayland session.
        var (x, y) = _positionMode == PopupPositionMode.Fixed || cursorPos is null
            ? AnchoredPosition(workArea, windowWidth, windowHeight,
                cursorPos is null ? PopupAnchor.BottomCenter : _fixedAnchor)
            : CursorRelativePosition(cursorPos.Value, workArea, windowWidth, windowHeight);

        _window.Position = new PixelPoint(
            Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - windowWidth)),
            Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - windowHeight)));
    }

    private Avalonia.Platform.Screen? ScreenFromMpvDisplayName()
    {
        if (_window is null || _windowContext.DisplayNames.Count == 0) return null;

        return _window.Screens.All.FirstOrDefault(screen =>
            screen.DisplayName is { } name
            && _windowContext.DisplayNames.Any(display =>
                string.Equals(display, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// The pointer sits inside the subtitle line it is pointing at, so the offset has to clear the
    /// text rather than merely separate the popup from the cursor hotspot.
    private (int X, int Y) CursorRelativePosition(
        PixelPoint cursor, PixelRect workArea, int width, int height)
    {
        int x = cursor.X - width / 2;

        if (_positionMode == PopupPositionMode.BelowSubtitle)
        {
            int below = cursor.Y + _offsetPx;
            return (x, below + height > workArea.Bottom ? cursor.Y - height - _offsetPx : below);
        }

        int above = cursor.Y - height - _offsetPx;
        return (x, above < workArea.Y ? cursor.Y + _offsetPx : above);
    }

    private (int X, int Y) AnchoredPosition(
        PixelRect workArea, int width, int height, PopupAnchor anchor)
    {
        int x = anchor switch
        {
            PopupAnchor.TopLeft or PopupAnchor.BottomLeft => workArea.X + _offsetPx,
            PopupAnchor.TopRight or PopupAnchor.BottomRight => workArea.Right - width - _offsetPx,
            _ => workArea.X + (workArea.Width - width) / 2
        };

        bool top = anchor is PopupAnchor.TopLeft or PopupAnchor.TopCenter or PopupAnchor.TopRight;
        return (x, top ? workArea.Y + _offsetPx : workArea.Bottom - height - _offsetPx);
    }
}
