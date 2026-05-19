using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using JitenMPV.App.Platform;
using JitenMPV.App.ViewModels;
using JitenMPV.App.Views;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Popup;

public sealed class AvaloniaPopupPresenter : IPopupPresenter
{
    private DictionaryPopupWindow? _window;
    private PopupViewModel? _viewModel;
    private volatile bool _isVisible;
    private PixelPoint _lastCursorPos;

    public bool IsVisible => _isVisible;

    public event Action<PopupAction>? ActionClicked;
    public event Action? MouseEntered;
    public event Action? MouseLeft;

    public Task ShowAsync(PopupData data, CancellationToken ct)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            EnsureWindow();

            _viewModel!.Update(data);
            _lastCursorPos = CursorPositionHelper.GetCursorPosition();

            PositionWindow(_lastCursorPos);

            if (!_window!.IsVisible)
                _window.Show();

            _isVisible = true;

            Dispatcher.UIThread.Post(() => PositionWindow(_lastCursorPos), DispatcherPriority.Render);
        }).GetTask();
    }

    public Task HideAsync(CancellationToken ct)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isVisible = false;
            _window?.Hide();
        }).GetTask();
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;

        _viewModel = new PopupViewModel();
        _viewModel.ActionClicked += action => ActionClicked?.Invoke(action);

        _window = new DictionaryPopupWindow { DataContext = _viewModel };

        _window.PointerEntered += (_, _) => MouseEntered?.Invoke();
        _window.PointerExited += (_, _) => MouseLeft?.Invoke();
    }

    private void PositionWindow(PixelPoint cursorPos)
    {
        if (_window is null) return;

        var screen = _window.Screens.ScreenFromPoint(cursorPos);
        if (screen is null) return;

        var workArea = screen.WorkingArea;
        var scaling = screen.Scaling;

        var bounds = _window.Bounds.Size;
        int windowWidth = bounds.Width > 0 ? (int)(bounds.Width * scaling) : 350;
        int windowHeight = bounds.Height > 0 ? (int)(bounds.Height * scaling) : 250;

        int x = cursorPos.X - windowWidth / 2;
        int y = cursorPos.Y - windowHeight - 10;

        x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - windowWidth));
        if (y < workArea.Y)
            y = cursorPos.Y + 20;

        _window.Position = new PixelPoint(x, y);
    }
}
