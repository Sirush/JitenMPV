using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using JitenMPV.App.Platform;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Overlay;

/// macOS: mpv's window is found through the window server's on-screen list, filtered to mpv's pid.
/// Quartz reports those bounds in points, which is also what Cocoa positions windows in.
internal sealed class MacOsOverlayBackend : IMpvOverlayBackend
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _poll;
    private bool _ignoresMouseEvents;
    private bool _polling;

    public MacOsOverlayBackend()
    {
        _poll = new DispatcherTimer(PollInterval, DispatcherPriority.Background,
            (_, _) => GeometryChanged?.Invoke());
    }

    public bool IsSupported => _ignoresMouseEvents;

    public event Action? GeometryChanged;

    public ValueTask PrepareAsync(Window window, PopupWindowContext context, CancellationToken ct)
    {
        _ignoresMouseEvents = MacOsPluginIntegration.SetIgnoresMouseEvents(window);
        if (!_ignoresMouseEvents) return ValueTask.CompletedTask;

        MacOsPluginIntegration.ConfigurePopupWindow(window);
        if (!_polling)
        {
            _polling = true;
            _poll.Start();
        }

        return ValueTask.CompletedTask;
    }

    public OverlayGeometry? TryGetGeometry(PopupWindowContext context)
        => context.ProcessId is { } processId ? Quartz.LargestWindowBounds(processId) : null;

    public OverlayLogicalSize Apply(Window window, OverlayGeometry geometry)
    {
        double scale = window.DesktopScaling > 0 ? window.DesktopScaling : 1;
        window.Position = new PixelPoint(
            (int)Math.Round(geometry.X * scale), (int)Math.Round(geometry.Y * scale));
        window.Width = geometry.Width;
        window.Height = geometry.Height;
        return new OverlayLogicalSize(geometry.Width, geometry.Height);
    }

    public ValueTask DisposeAsync()
    {
        _polling = false;
        _poll.Stop();
        return ValueTask.CompletedTask;
    }

    private static class Quartz
    {
        private const string CoreGraphics =
            "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        private const uint OnScreenOnly = 1 << 0;
        private const uint ExcludeDesktopElements = 1 << 4;
        private const int NumberDoubleType = 13;

        public static OverlayGeometry? LargestWindowBounds(int processId)
        {
            IntPtr list = IntPtr.Zero;
            try
            {
                list = CGWindowListCopyWindowInfo(
                    OnScreenOnly | ExcludeDesktopElements, IntPtr.Zero);
                if (list == IntPtr.Zero) return null;

                OverlayGeometry? best = null;
                double bestArea = 0;
                long count = CFArrayGetCount(list);
                for (long i = 0; i < count; i++)
                {
                    var window = CFArrayGetValueAtIndex(list, i);
                    if (window == IntPtr.Zero) continue;
                    if (Number(window, "kCGWindowOwnerPID") is not { } owner
                        || (int)owner != processId)
                        continue;

                    if (Dictionary(window, "kCGWindowBounds") is not { } bounds) continue;
                    if (Number(bounds, "X") is not { } x
                        || Number(bounds, "Y") is not { } y
                        || Number(bounds, "Width") is not { } width
                        || Number(bounds, "Height") is not { } height)
                        continue;

                    double area = width * height;
                    if (area <= bestArea) continue;

                    bestArea = area;
                    best = new OverlayGeometry(x, y, width, height);
                }

                return best;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return null;
            }
            finally
            {
                if (list != IntPtr.Zero) CFRelease(list);
            }
        }

        private static double? Number(IntPtr dictionary, string key)
        {
            var keyRef = IntPtr.Zero;
            try
            {
                keyRef = CFStringCreateWithCString(IntPtr.Zero, key, 0x08000100);
                if (keyRef == IntPtr.Zero) return null;
                if (!CFDictionaryGetValueIfPresent(dictionary, keyRef, out var value)
                    || value == IntPtr.Zero)
                    return null;

                return CFNumberGetValue(value, NumberDoubleType, out double result) ? result : null;
            }
            finally
            {
                if (keyRef != IntPtr.Zero) CFRelease(keyRef);
            }
        }

        private static IntPtr? Dictionary(IntPtr dictionary, string key)
        {
            var keyRef = IntPtr.Zero;
            try
            {
                keyRef = CFStringCreateWithCString(IntPtr.Zero, key, 0x08000100);
                if (keyRef == IntPtr.Zero) return null;
                return CFDictionaryGetValueIfPresent(dictionary, keyRef, out var value)
                       && value != IntPtr.Zero
                    ? value
                    : null;
            }
            finally
            {
                if (keyRef != IntPtr.Zero) CFRelease(keyRef);
            }
        }

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGWindowListCopyWindowInfo(uint option, IntPtr relativeTo);

        [DllImport(CoreFoundation)]
        private static extern long CFArrayGetCount(IntPtr array);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);

        [DllImport(CoreFoundation)]
        private static extern bool CFDictionaryGetValueIfPresent(
            IntPtr dictionary, IntPtr key, out IntPtr value);

        [DllImport(CoreFoundation)]
        private static extern bool CFNumberGetValue(IntPtr number, int type, out double value);

        [DllImport(CoreFoundation, CharSet = CharSet.Ansi)]
        private static extern IntPtr CFStringCreateWithCString(
            IntPtr allocator, string value, uint encoding);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr reference);
    }
}
