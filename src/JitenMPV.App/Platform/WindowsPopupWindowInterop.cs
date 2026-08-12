using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace JitenMPV.App.Platform;

internal static class WindowsPopupWindowInterop
{
    public static void ConfigureNoActivate(Window window)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = window.TryGetPlatformHandle();
        if (handle is null)
            return;

        const int extendedStyle = -20;
        const int noActivate = 0x08000000;
        const int toolWindow = 0x00000080;
        var style = GetWindowLong(handle.Handle, extendedStyle);
        SetWindowLong(
            handle.Handle,
            extendedStyle,
            style | noActivate | toolWindow);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(
        IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(
        IntPtr window, int index, int newValue);
}
