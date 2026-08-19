using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Overlay;

/// Tracks mpv's HWND and keeps a click-through, mpv-owned overlay glued to its client area.
internal sealed class WindowsOverlayBackend : IMpvOverlayBackend
{
    /// The event hook covers moves and resizes; this only has to catch what the hook cannot see,
    /// such as a monitor scaling change or an mpv window replaced while the hook was reinstalling.
    private static readonly TimeSpan SafetyPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _poll;
    private readonly Win32.WinEventProc _hookCallback;
    private IntPtr _hook;
    private IntPtr _mpvWindow;
    private IntPtr _overlayWindow;
    private int _hookedProcessId;
    private bool _polling;
    private bool _disposed;

    public WindowsOverlayBackend()
    {
        _hookCallback = OnWinEvent;
        _poll = new DispatcherTimer(SafetyPollInterval, DispatcherPriority.Background,
            (_, _) => GeometryChanged?.Invoke());
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public event Action? GeometryChanged;

    public ValueTask PrepareAsync(Window window, PopupWindowContext context, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return ValueTask.CompletedTask;

        _overlayWindow = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_overlayWindow != IntPtr.Zero)
        {
            Win32.ConfigureClickThrough(_overlayWindow);
            if (ResolveMpvWindow(context) is { } mpv && mpv != IntPtr.Zero)
                Win32.SetOwner(_overlayWindow, mpv);
        }

        InstallHook(context);
        if (!_polling)
        {
            _polling = true;
            _poll.Start();
        }

        return ValueTask.CompletedTask;
    }

    public OverlayGeometry? TryGetGeometry(PopupWindowContext context)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (ResolveMpvWindow(context) is not { } mpv || mpv == IntPtr.Zero) return null;
        if (!Win32.IsWindowVisible(mpv) || Win32.IsIconic(mpv)) return null;
        if (!Win32.TryGetClientRect(mpv, out var geometry)) return null;

        if (_overlayWindow != IntPtr.Zero)
        {
            Win32.ConfigureClickThrough(_overlayWindow);
            Win32.SetOwner(_overlayWindow, mpv);
        }

        return geometry;
    }

    public OverlayLogicalSize Apply(Window window, OverlayGeometry geometry)
        => OverlayGeometryApplier.ApplyPhysical(window, geometry);

    private IntPtr? ResolveMpvWindow(PopupWindowContext context)
    {
        if (context.ProcessId is not { } processId) return null;

        if (_mpvWindow != IntPtr.Zero
            && Win32.IsWindow(_mpvWindow)
            && Win32.ProcessIdOf(_mpvWindow) == processId)
            return _mpvWindow;

        _mpvWindow = Win32.FindMainWindow(processId);
        if (_mpvWindow != IntPtr.Zero && _hookedProcessId != processId)
            InstallHook(context);
        return _mpvWindow;
    }

    private void InstallHook(PopupWindowContext context)
    {
        if (!OperatingSystem.IsWindows() || context.ProcessId is not { } processId) return;
        if (_hook != IntPtr.Zero && _hookedProcessId == processId) return;

        RemoveHook();
        _hook = Win32.InstallWindowHook(processId, _hookCallback);
        _hookedProcessId = _hook != IntPtr.Zero ? processId : 0;
    }

    private void OnWinEvent(
        IntPtr hook, uint eventType, IntPtr window,
        int objectId, int childId, uint thread, uint time)
    {
        if (_disposed || objectId != Win32.ObjidWindow) return;
        GeometryChanged?.Invoke();
    }

    private void RemoveHook()
    {
        if (_hook == IntPtr.Zero) return;
        Win32.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _hookedProcessId = 0;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _polling = false;
        _poll.Stop();
        RemoveHook();
        return ValueTask.CompletedTask;
    }

    private static class Win32
    {
        public const int ObjidWindow = 0;

        private const int GwlExStyle = -20;
        private const int GwlpHwndParent = -8;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const uint LayeredAlpha = 0x00000002;

        /// EVENT_SYSTEM_FOREGROUND through EVENT_OBJECT_LOCATIONCHANGE, which spans the
        /// minimize, show/hide and move/resize transitions the overlay has to follow. The hook is
        /// filtered to mpv's process, so the wider range costs nothing.
        private const uint EventSystemForeground = 0x0003;
        private const uint EventObjectLocationChange = 0x800B;

        private const uint WinEventOutOfContext = 0x0000;
        private const uint WinEventSkipOwnProcess = 0x0002;

        public delegate void WinEventProc(
            IntPtr hook, uint eventType, IntPtr window,
            int objectId, int childId, uint thread, uint time);

        /// WS_EX_TRANSPARENT only leaves a window out of mouse hit testing while WS_EX_LAYERED is
        /// set too, and a freshly layered window stays blank until its attributes are set, which is
        /// what the opaque LWA_ALPHA call is for. Avalonia clears WS_EX_LAYERED again whenever it
        /// rewrites the extended style, so the tracker re-checks this rather than setting it once.
        public static void ConfigureClickThrough(IntPtr window)
        {
            const long wanted = WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate;
            try
            {
                var style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
                if ((style & wanted) == wanted) return;

                SetWindowLongPtr(window, GwlExStyle, (IntPtr)(style | wanted));
                SetLayeredWindowAttributes(window, 0, 255, LayeredAlpha);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
            }
        }

        /// Owning the overlay to mpv keeps it above that window without making it topmost over
        /// everything else on the desktop.
        public static void SetOwner(IntPtr overlay, IntPtr owner)
        {
            try
            {
                if (GetWindowLongPtr(overlay, GwlpHwndParent) != owner)
                    SetWindowLongPtr(overlay, GwlpHwndParent, owner);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
            }
        }

        public static IntPtr InstallWindowHook(int processId, WinEventProc callback)
        {
            try
            {
                return SetWinEventHook(
                    EventSystemForeground, EventObjectLocationChange, IntPtr.Zero, callback,
                    (uint)processId, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return IntPtr.Zero;
            }
        }

        public static void UnhookWinEvent(IntPtr hook)
        {
            try { UnhookWinEventNative(hook); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }

        public static bool TryGetClientRect(IntPtr window, out OverlayGeometry geometry)
        {
            geometry = default;
            try
            {
                if (!GetClientRect(window, out var rect)) return false;
                var origin = new Point { X = 0, Y = 0 };
                if (!ClientToScreen(window, ref origin)) return false;

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0) return false;

                geometry = new OverlayGeometry(origin.X, origin.Y, width, height);
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }

        public static int ProcessIdOf(IntPtr window)
        {
            GetWindowThreadProcessId(window, out var processId);
            return (int)processId;
        }

        /// mpv's video window carries the class name "mpv"; the largest visible top-level of the
        /// process is the fallback for builds that register another one.
        public static IntPtr FindMainWindow(int processId)
        {
            var candidates = new List<(IntPtr Window, long Area, bool IsMpvClass)>();
            try
            {
                EnumWindows((window, _) =>
                {
                    if (!IsWindowVisible(window)) return true;
                    if (GetWindow(window, 4) != IntPtr.Zero) return true;
                    if (ProcessIdOf(window) != processId) return true;
                    if (!GetClientRect(window, out var rect)) return true;

                    long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                    if (area <= 0) return true;

                    candidates.Add((window, area, ClassNameOf(window) == "mpv"));
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return IntPtr.Zero;
            }

            var best = IntPtr.Zero;
            long bestArea = -1;
            bool bestIsMpv = false;
            foreach (var (window, area, isMpv) in candidates)
            {
                bool better = isMpv != bestIsMpv ? isMpv : area > bestArea;
                if (!better) continue;

                best = window;
                bestArea = area;
                bestIsMpv = isMpv;
            }

            return best;
        }

        private static string ClassNameOf(IntPtr window)
        {
            var buffer = new char[64];
            int length = GetClassName(window, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : "";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, char[] buffer, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr window, ref Point point);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr window, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
            uint processId, uint threadId, uint flags);

        [DllImport("user32.dll", EntryPoint = "UnhookWinEvent")]
        private static extern bool UnhookWinEventNative(IntPtr hook);
    }
}
