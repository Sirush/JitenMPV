using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace JitenMPV.App.Platform;

public static class CursorPositionHelper
{
    public static PixelPoint GetCursorPosition()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetCursorPositionWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetCursorPositionLinux();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return GetCursorPositionMacOS();

        return default;
    }

    private static PixelPoint GetCursorPositionWindows()
    {
        if (GetCursorPos(out var point))
            return new PixelPoint(point.X, point.Y);
        return default;
    }

    private static PixelPoint GetCursorPositionLinux()
    {
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero) return default;

        try
        {
            var root = XDefaultRootWindow(display);
            XQueryPointer(display, root,
                out _, out _,
                out int rootX, out int rootY,
                out _, out _, out _);
            return new PixelPoint(rootX, rootY);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static PixelPoint GetCursorPositionMacOS()
    {
        var eventRef = CGEventCreate(IntPtr.Zero);
        if (eventRef == IntPtr.Zero) return default;

        try
        {
            var point = CGEventGetLocation(eventRef);
            return new PixelPoint((int)point.X, (int)point.Y);
        }
        finally
        {
            CFRelease(eventRef);
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
