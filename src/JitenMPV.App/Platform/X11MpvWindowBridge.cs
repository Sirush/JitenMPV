using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace JitenMPV.App.Platform;

/// <summary>
/// Operations which need both the Avalonia popup XID and the XID exposed by this mpv IPC
/// connection. Keeping them together avoids global window scans and cross-instance ownership.
/// </summary>
internal static class X11MpvWindowBridge
{
    public static PixelPoint? TranslateToRoot(long? mpvWindowId, double x, double y)
    {
        if (!OperatingSystem.IsLinux() || mpvWindowId is not > 0) return null;

        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;

            var root = XDefaultRootWindow(display);
            return XTranslateCoordinates(
                display, (nuint)mpvWindowId.Value, root,
                (int)Math.Round(x), (int)Math.Round(y),
                out var rootX, out var rootY, out _)
                ? new PixelPoint(rootX, rootY)
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (display != IntPtr.Zero) TryCloseDisplay(display);
        }
    }

    public static bool SetTransientOwner(Window popup, long? mpvWindowId)
    {
        if (!OperatingSystem.IsLinux() || mpvWindowId is not > 0) return false;

        var handle = popup.TryGetPlatformHandle();
        if (handle is null || !string.Equals(handle.HandleDescriptor, "XID",
                StringComparison.OrdinalIgnoreCase))
            return false;

        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return false;

            var result = XSetTransientForHint(
                display, (nuint)handle.Handle, (nuint)mpvWindowId.Value);
            XFlush(display);
            return result != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (display != IntPtr.Zero) TryCloseDisplay(display);
        }
    }

    private static void TryCloseDisplay(IntPtr display)
    {
        try { XCloseDisplay(display); }
        catch (DllNotFoundException) { }
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern nuint XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern bool XTranslateCoordinates(
        IntPtr display, nuint sourceWindow, nuint destinationWindow,
        int sourceX, int sourceY,
        out int destinationX, out int destinationY, out nuint child);

    [DllImport("libX11.so.6")]
    private static extern int XSetTransientForHint(
        IntPtr display, nuint window, nuint transientFor);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);
}
