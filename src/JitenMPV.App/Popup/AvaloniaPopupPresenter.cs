using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using JitenMPV.App.ViewModels;
using JitenMPV.App.Views;
using JitenMPV.Core.Config;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Popup;

public sealed class AvaloniaPopupPresenter : IPopupPresenter
{
    private readonly PopupBackendCoordinator _backend = new();
    private readonly IPopupPositionCalculator _positionCalculator =
        new PopupPositionCalculator();

    private DictionaryPopupWindow? _window;
    private PopupViewModel? _viewModel;
    private volatile bool _isVisible;
    private PopupPointerPosition? _lastPointer;
    private PopupPositionMode _positionMode = PopupPositionMode.AboveSubtitle;
    private PopupAnchor _fixedAnchor = PopupAnchor.TopCenter;
    private int _offsetPx = 60;
    private double _lastFontScale = -1;
    private int _lastMaxWidth = -1;
    private bool _pointerInside;
    private volatile PopupWindowContext _windowContext = PopupWindowContext.Empty;

    private long _revision;
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _positionCts;
    private Task _positionTask = Task.CompletedTask;
    private PopupSupportLevel _reportedSupportLevel = PopupSupportLevel.Unknown;

    public AvaloniaPopupPresenter()
    {
        _backend.GeometryChanged += QueuePositionWindow;
    }

    public bool IsVisible => _isVisible;
    public PopupSupportLevel SupportLevel => _backend.SupportLevel;
    public bool RequiresPointerTransferGrace => _backend.UsesNativeWayland;

    public event Action<PopupAction>? ActionClicked;
    public event Action<int>? DeckSelected;
    public event Action? MouseEntered;
    public event Action? MouseLeft;
    public event Action<PopupSupportLevel>? SupportLevelChanged;

    public void UpdateWindowContext(PopupWindowContext context)
    {
        _windowContext = context;
        QueuePositionWindow();
    }

    public Task ShowAsync(
        PopupData data,
        PopupPointerPosition pointer,
        CancellationToken ct) =>
        Dispatcher.UIThread.InvokeAsync(
            () => ShowOnUiThreadAsync(data, pointer, ct));

    private async Task ShowOnUiThreadAsync(
        PopupData data,
        PopupPointerPosition pointer,
        CancellationToken ct)
    {
        var (revision, operation) = BeginPopupOperation(ct);
        var operationToken = operation.Token;
        var provisionalShow = false;
        CancelQueuedPosition();

        try
        {
            operationToken.ThrowIfCancellationRequested();
            await AwaitQueuedPositionAsync();
            if (!IsCurrent(revision, _window, operationToken))
                return;

            var window = EnsureWindow();
            provisionalShow = OperatingSystem.IsMacOS()
                              && window.IsVisible
                              && window.Opacity == 0
                              && !_isVisible;
            _positionMode = data.PositionMode;
            _fixedAnchor = data.FixedAnchor;
            _offsetPx = data.OffsetPx;
            _lastPointer = pointer;
            _backend.CapturePointerAnchor();

            _viewModel!.Update(data);
            ApplyFontScale(data.FontScale);
            ApplyMaxWidth(data.MaxWidthPx);
            var needsNativeLayout = OperatingSystem.IsMacOS()
                                    && !window.IsVisible;
            if (!needsNativeLayout)
                MeasurePopup(window);

            await _backend.PrepareAsync(
                window, _windowContext, operationToken);
            ReportSupportLevel();
            if (!IsCurrent(revision, window, operationToken))
                return;

            if (needsNativeLayout)
            {
                window.Opacity = 0;
                window.Show();
                provisionalShow = true;

                // Cocoa determines a SizeToContent top-level's native bounds when it is mapped.
                // Keep the provisional native window transparent until it has been placed using
                // the resulting size, so its default mapping position never flashes onscreen.
                await Dispatcher.UIThread.InvokeAsync(
                    static () => { }, DispatcherPriority.Render);
            }

            if (!IsCurrent(revision, window, operationToken))
                return;

            await PositionWindowAsync(window, pointer, operationToken);

            if (!IsCurrent(revision, window, operationToken))
                return;

            if (!window.IsVisible)
                window.Show();
            window.Opacity = 1;

            _isVisible = true;

            // X11 can only apply transient-for after the native handle has been mapped. This does
            // not recalculate or move the popup and leaves Wayland's one-pass positioning intact.
            if (!_backend.UsesNativeWayland)
                await _backend.PrepareAsync(
                    window, _windowContext, operationToken);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // A newer show/hide operation superseded this one.
        }
        finally
        {
            if (provisionalShow
                && !_isVisible
                && _window is { } window
                && (ReferenceEquals(_operationCts, operation)
                    || _operationCts is null))
            {
                // No newer show adopted the reusable Cocoa window, so undo this operation's
                // transparent provisional map even when its caller canceled the external token.
                window.Hide();
                window.Opacity = 1;
            }

            EndPopupOperation(operation);
        }
    }

    public Task UpdateAsync(PopupData data, CancellationToken ct) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ct.IsCancellationRequested)
                return;
            var window = EnsureWindow();
            _viewModel!.Update(data);
            ApplyFontScale(data.FontScale);
            ApplyMaxWidth(data.MaxWidthPx);
            MeasurePopup(window);
            QueuePositionWindow();
        }).GetTask();

    public Task HideAsync(CancellationToken ct) =>
        Dispatcher.UIThread.InvokeAsync(() => HideOnUiThreadAsync(ct));

    private async Task HideOnUiThreadAsync(CancellationToken ct)
    {
        CancelActivePopupOperation();
        _isVisible = false;
        _pointerInside = false;
        _viewModel?.CloseDeckPicker();
        CancelQueuedPosition();

        var window = _window;
        if (window is null)
            return;

        await AwaitQueuedPositionAsync();
        await _backend.DetachAsync(window, ct);

        if (_backend.RequiresWindowRecreationAfterHide)
        {
            window.Close();
            _window = null;
            _viewModel = null;
            _lastPointer = null;
        }
        else
        {
            window.Hide();
        }
    }

    private DictionaryPopupWindow EnsureWindow()
    {
        if (_window is not null)
            return _window;

        _viewModel = new PopupViewModel();
        _viewModel.ActionClicked += action => ActionClicked?.Invoke(action);
        _viewModel.DeckSelected += deckId => DeckSelected?.Invoke(deckId);

        var window = new DictionaryPopupWindow { DataContext = _viewModel };
        _window = window;
        _lastFontScale = -1;
        _lastMaxWidth = -1;

        window.PointerEntered += (_, _) =>
        {
            _pointerInside = true;
            MouseEntered?.Invoke();
        };
        window.PointerExited += (_, _) =>
        {
            _pointerInside = false;
            MouseLeft?.Invoke();
        };

        window.SizeChanged += (_, _) =>
        {
            if (!_pointerInside)
                QueuePositionWindow();
        };
        return window;
    }

    private void QueuePositionWindow()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(QueuePositionWindow);
            return;
        }

        if (!_isVisible || _window is null || _lastPointer is null)
            return;

        _positionCts?.Cancel();
        _positionCts?.Dispose();
        var cts = _positionCts = new CancellationTokenSource();
        var previous = _positionTask;
        var revision = Volatile.Read(ref _revision);
        var window = _window;
        var pointer = _lastPointer;
        _positionTask = RunSerializedPositionAsync(
            previous, revision, window, pointer, cts.Token);
    }

    private async Task RunSerializedPositionAsync(
        Task previous,
        long revision,
        DictionaryPopupWindow window,
        PopupPointerPosition pointer,
        CancellationToken ct)
    {
        try
        {
            try
            {
                await previous;
            }
            catch (OperationCanceledException)
            {
            }

            ct.ThrowIfCancellationRequested();
            if (!IsCurrent(revision, window, ct) || !_isVisible)
                return;

            MeasurePopup(window);
            await PositionWindowAsync(window, pointer, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PositionWindowAsync(
        DictionaryPopupWindow window,
        PopupPointerPosition pointer,
        CancellationToken ct)
    {
        var size = PopupSize(window);
        var request = new PopupPlacementRequest(
            _positionMode,
            _fixedAnchor,
            _offsetPx,
            null);
        await _backend.PositionAsync(
            window,
            _windowContext,
            pointer,
            request,
            size,
            _positionCalculator,
            ct);
        ReportSupportLevel();
    }

    private static void MeasurePopup(DictionaryPopupWindow window)
    {
        window.Measure(Size.Infinity);
        if (!OperatingSystem.IsMacOS()
            && window.DesiredSize.Width > 0
            && window.DesiredSize.Height > 0)
        {
            window.Arrange(new Rect(window.DesiredSize));
        }
    }

    private static LogicalSize PopupSize(DictionaryPopupWindow window)
    {
        var size = OperatingSystem.IsMacOS() && window.IsVisible
            ? window.Bounds.Size
            : window.DesiredSize;
        if (size.Width <= 0 || size.Height <= 0)
            size = window.Bounds.Size;
        return new LogicalSize(
            size.Width > 0 ? size.Width : 350,
            size.Height > 0 ? size.Height : 250);
    }

    private void ApplyFontScale(double scale)
    {
        if (_window is null || scale == _lastFontScale)
            return;
        _lastFontScale = scale;
        var container = _window.FindControl<LayoutTransformControl>(
            "ScaleContainer");
        if (container is not null)
            container.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void ReportSupportLevel()
    {
        var current = _backend.SupportLevel;
        if (current == _reportedSupportLevel)
            return;
        _reportedSupportLevel = current;
        SupportLevelChanged?.Invoke(current);
    }

    private void ApplyMaxWidth(int maxWidthPx)
    {
        if (_window is null || maxWidthPx == _lastMaxWidth || maxWidthPx <= 0)
            return;
        _lastMaxWidth = maxWidthPx;
        _window.MaxWidth = maxWidthPx;
    }

    private (long Revision, CancellationTokenSource Cancellation)
        BeginPopupOperation(CancellationToken externalToken)
    {
        var revision = Interlocked.Increment(ref _revision);
        var next = CancellationTokenSource.CreateLinkedTokenSource(
            externalToken);
        var previous = Interlocked.Exchange(ref _operationCts, next);
        previous?.Cancel();
        previous?.Dispose();
        return (revision, next);
    }

    private void CancelActivePopupOperation()
    {
        Interlocked.Increment(ref _revision);
        var operation = Interlocked.Exchange(ref _operationCts, null);
        operation?.Cancel();
        operation?.Dispose();
    }

    private void EndPopupOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(
                Interlocked.CompareExchange(
                    ref _operationCts, null, operation),
                operation))
            operation.Dispose();
    }

    private bool IsCurrent(
        long revision,
        DictionaryPopupWindow? window,
        CancellationToken ct) =>
        !ct.IsCancellationRequested
        && revision == Volatile.Read(ref _revision)
        && ReferenceEquals(window, _window);

    private void CancelQueuedPosition()
    {
        _positionCts?.Cancel();
        _positionCts?.Dispose();
        _positionCts = null;
    }

    private async Task AwaitQueuedPositionAsync()
    {
        try
        {
            await _positionTask;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
