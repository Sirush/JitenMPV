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

    public static void EnsureTopmost(Window window)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = window.TryGetPlatformHandle();
        if (handle is null)
            return;

        const int noSize = 0x0001;
        const int noMove = 0x0002;
        const int noActivate = 0x0010;
        SetWindowPos(
            handle.Handle,
            TopmostInsertAfter,
            0, 0, 0, 0,
            noSize | noMove | noActivate);
    }

    private static readonly IntPtr TopmostInsertAfter = new(-1);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(
        IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(
        IntPtr window, int index, int newValue);
}
