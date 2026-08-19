using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using JitenMPV.App.Popup;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Platform;

/// <summary>
/// Operations which need both the Avalonia popup XID and the XID exposed by this mpv IPC
/// connection. Keeping them together avoids global window scans and cross-instance ownership.
/// </summary>
internal static class X11MpvWindowBridge
{
    public static bool TryGetClientGeometry(
        long? mpvWindowId,
        out GlobalLogicalPoint origin,
        out LogicalSize size)
    {
        origin = default;
        size = default;
        if (!OperatingSystem.IsLinux() || mpvWindowId is not > 0)
            return false;

        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                return false;

            var window = (nuint)mpvWindowId.Value;
            if (XGetGeometry(
                    display, window, out _, out _, out _,
                    out var width, out var height, out _, out _) == 0)
                return false;

            var root = XDefaultRootWindow(display);
            if (!XTranslateCoordinates(
                    display, window, root, 0, 0,
                    out var rootX, out var rootY, out _))
                return false;

            origin = new GlobalLogicalPoint(rootX, rootY);
            size = new LogicalSize(width, height);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (display != IntPtr.Zero)
                TryCloseDisplay(display);
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
    private static extern int XGetGeometry(
        IntPtr display,
        nuint drawable,
        out nuint root,
        out int x,
        out int y,
        out uint width,
        out uint height,
        out uint borderWidth,
        out uint depth);

    [DllImport("libX11.so.6")]
    private static extern int XSetTransientForHint(
        IntPtr display, nuint window, nuint transientFor);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);
}

internal sealed class X11MpvWindowGeometryProvider : IMpvWindowGeometryProvider
{
    private static readonly OutputInfo UnknownOutput = new(
        null, null, default, default, 1, null);

    public event Action? GeometryChanged
    {
        add { }
        remove { }
    }

    public GeometryProviderStatus Status => GeometryProviderStatus.Ready;

    public ValueTask<MpvWindowGeometry?> GetGeometryAsync(
        PopupWindowContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!X11MpvWindowBridge.TryGetClientGeometry(
                context.WindowId, out var origin, out var size))
            return ValueTask.FromResult<MpvWindowGeometry?>(null);

        return ValueTask.FromResult<MpvWindowGeometry?>(new MpvWindowGeometry(
            origin, size, UnknownOutput, context.IsFullscreen, 1));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class X11PopupSurfaceBackend : IPopupSurfaceBackend
{
    private long? _mpvWindowId;

    public PopupBackendCapabilities Capabilities =>
        PopupBackendCapabilities.ExactGlobalPosition
        | PopupBackendCapabilities.AboveFullscreen
        | PopupBackendCapabilities.MultiMonitor
        | PopupBackendCapabilities.NonActivating
        | PopupBackendCapabilities.Interactive;

    public void UpdateContext(PopupWindowContext context) =>
        _mpvWindowId = context.WindowId;

    public ValueTask PrepareAsync(Window window, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        X11MpvWindowBridge.SetTransientOwner(window, _mpvWindowId);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPositionAsync(
        Window window,
        GlobalLogicalPoint position,
        LogicalSize size,
        OutputInfo output,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        window.Position = new PixelPoint(
            checked((int)Math.Round(position.X)),
            checked((int)Math.Round(position.Y)));
        X11MpvWindowBridge.SetTransientOwner(window, _mpvWindowId);
        return ValueTask.CompletedTask;
    }

    public ValueTask DetachAsync(Window window, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
