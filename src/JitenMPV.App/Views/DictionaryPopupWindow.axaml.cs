using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JitenMPV.App.Views;

public partial class DictionaryPopupWindow : Window
{
    public DictionaryPopupWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ApplyWin32NoActivate();
    }

    private void ApplyWin32NoActivate()
    {
        var handle = TryGetPlatformHandle();
        if (handle is null) return;

        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        var hwnd = handle.Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
