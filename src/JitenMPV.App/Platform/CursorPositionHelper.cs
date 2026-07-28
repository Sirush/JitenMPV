using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace JitenMPV.App.Platform;

public static class CursorPositionHelper
{
    /// <returns>Null when the platform cannot report a global pointer position. Distinct from
    /// (0,0), which is a real cursor location at the top-left pixel.</returns>
    public static PixelPoint? GetCursorPosition()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetCursorPositionWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetCursorPositionLinux();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return GetCursorPositionMacOS();

        return null;
    }

    private static PixelPoint? GetCursorPositionWindows()
    {
        try
        {
            return GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    /// The P/Invoke itself is inside the try: a Wayland-only or headless system may have no
    /// libX11.so.6 at all, and the resulting DllNotFoundException is raised at the call site,
    /// where it would otherwise take down the whole popup path.
    private static PixelPoint? GetCursorPositionLinux()
    {
        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;

            var root = XDefaultRootWindow(display);
            if (!XQueryPointer(display, root,
                    out _, out _,
                    out int rootX, out int rootY,
                    out _, out _, out _))
                return null;

            return new PixelPoint(rootX, rootY);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (display != IntPtr.Zero)
                TryCloseDisplay(display);
        }
    }

    private static void TryCloseDisplay(IntPtr display)
    {
        try
        {
            XCloseDisplay(display);
        }
        catch (DllNotFoundException)
        {
            // Unreachable in practice: opening it already succeeded.
        }
    }

    private static PixelPoint? GetCursorPositionMacOS()
    {
        IntPtr eventRef = IntPtr.Zero;
        try
        {
            eventRef = CGEventCreate(IntPtr.Zero);
            if (eventRef == IntPtr.Zero) return null;

            var point = CGEventGetLocation(eventRef);
            return new PixelPoint((int)point.X, (int)point.Y);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (eventRef != IntPtr.Zero) CFRelease(eventRef);
        }
    }

    // Windows
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    // Linux X11
    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern bool XQueryPointer(
        IntPtr display, IntPtr window,
        out IntPtr rootReturn, out IntPtr childReturn,
        out int rootXReturn, out int rootYReturn,
        out int winXReturn, out int winYReturn,
        out uint maskReturn);

    // macOS
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr eventRef);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr obj);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X; public double Y; }
}
