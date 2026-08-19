using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using JitenMPV.App.Platform;
using JitenMPV.Core.Config;
using JitenMPV.Core.Interaction;
using JitenMPV.Core.Rendering;

namespace JitenMPV.App.Overlay;

/// Draws the subtitle into a transparent window of this process, positioned over mpv's client
/// area. Everything below the interface runs on the UI thread; the plugin host calls in from its
/// own render loop.
public sealed class AvaloniaSubtitleOverlaySurface : ISubtitleOverlaySurface
{
    private readonly SubtitleFontStore _fonts = new();
    private readonly SubtitleLayoutEngine _engine;

    private volatile PluginSettings? _settings;
    private volatile PopupWindowContext _context = PopupWindowContext.Empty;
    private volatile int _osdWidth;
    private volatile int _osdHeight;
    private volatile bool _unavailable;

    private SubtitleOverlayWindow? _window;
    private SubtitleOverlayControl? _control;
    private IMpvOverlayBackend? _backend;
    private (int? ProcessId, long? WindowId)? _preparedFor;
    private OverlayGeometry? _appliedGeometry;
    private double _appliedScaling;
    private OverlayLogicalSize _appliedSize;
    private bool _windowFound;
    private long _revision;

    public AvaloniaSubtitleOverlaySurface()
    {
        _engine = new SubtitleLayoutEngine(_fonts);
    }

    public bool IsAvailable => !_unavailable;

    public event Action? AvailabilityChanged;

    public Task<IReadOnlyList<WordRect>?> ShowAsync(SubtitleFrame frame, CancellationToken ct)
    {
        long revision = Interlocked.Increment(ref _revision);
        return Dispatcher.UIThread.InvokeAsync(() => ShowCoreAsync(frame, revision, ct));
    }

    public Task ClearAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _revision);
        return Dispatcher.UIThread.InvokeAsync(() => _control?.SetFrame(null)).GetTask();
    }

    public void UpdateSettings(PluginSettings settings)
    {
        var previous = _settings;
        _settings = settings;

        if (previous?.ExperimentalAvaloniaRenderer == true && !settings.ExperimentalAvaloniaRenderer)
            _ = Dispatcher.UIThread.InvokeAsync(TearDownAsync);
    }

    public void UpdateOsd(int width, int height)
    {
        _osdWidth = width;
        _osdHeight = height;
    }

    public void UpdateWindowContext(PopupWindowContext context) => _context = context;

    public Task ShutdownAsync() => Dispatcher.UIThread.InvokeAsync(TearDownAsync);

    private async Task<IReadOnlyList<WordRect>?> ShowCoreAsync(
        SubtitleFrame frame, long revision, CancellationToken ct)
    {
        var settings = _settings;
        if (settings is null || _unavailable || ct.IsCancellationRequested) return null;

        int osdWidth = _osdWidth;
        int osdHeight = _osdHeight;
        if (osdWidth <= 0 || osdHeight <= 0) return null;

        var context = _context;
        var window = EnsureWindow();
        var backend = await EnsureBackendAsync(window, context, ct);
        if (!backend.IsSupported)
        {
            _unavailable = true;
            return null;
        }

        if (backend.TryGetGeometry(context) is not { } geometry)
        {
            _windowFound = false;
            _control!.SetFrame(null);
            return null;
        }

        _windowFound = true;
        var size = ApplyGeometry(backend, window, geometry);
        if (size.Height <= 0) return null;

        // A slower frame must never overwrite a newer one, and the marshal to this thread is where
        // the two can cross.
        if (revision != Volatile.Read(ref _revision) || ct.IsCancellationRequested) return null;

        var laidOut = _engine.Layout(frame, settings, osdWidth, osdHeight, size.Height);
        if (laidOut is null) return null;

        _control!.SetFrame(laidOut);
        if (_control.LeaseUnavailable)
        {
            _unavailable = true;
            return null;
        }

        return laidOut.Rects;
    }

    private SubtitleOverlayWindow EnsureWindow()
    {
        if (_window is not null) return _window;

        _control = new SubtitleOverlayControl(_fonts);
        _window = new SubtitleOverlayWindow { Content = _control };

        // The platform handle every backend configures only exists once the window has been mapped.
        _window.Show();
        return _window;
    }

    /// Preparation attaches hooks and window-manager state, so it is redone only when mpv's window
    /// identity changes rather than once per subtitle line.
    private async ValueTask<IMpvOverlayBackend> EnsureBackendAsync(
        Window window, PopupWindowContext context, CancellationToken ct)
    {
        var key = (context.ProcessId, context.WindowId);
        if (_backend is { } existing)
        {
            if (_preparedFor != key)
            {
                _preparedFor = key;
                await existing.PrepareAsync(window, context, ct);
            }

            return existing;
        }

        _preparedFor = key;
        var backend = await CreateBackendAsync(window, context, ct);
        _backend = backend;
        backend.GeometryChanged += OnBackendGeometryChanged;
        return backend;
    }

    private static async ValueTask<IMpvOverlayBackend> CreateBackendAsync(
        Window window, PopupWindowContext context, CancellationToken ct)
    {
        IMpvOverlayBackend backend = OperatingSystem.IsWindows()
            ? new WindowsOverlayBackend()
            : OperatingSystem.IsMacOS()
                ? new MacOsOverlayBackend()
                : OperatingSystem.IsLinux()
                    ? LinuxBackend(window, context)
                    : new UnsupportedOverlayBackend();

        await backend.PrepareAsync(window, context, ct);
        if (backend.IsSupported) return backend;

        await backend.DisposeAsync();
        return new UnsupportedOverlayBackend();
    }

    /// Every other native-Wayland compositor is left to mpv: without foreign-window geometry the
    /// overlay could only be placed by guessing, and a subtitle in the wrong place is worse than
    /// one drawn by mpv.
    private static IMpvOverlayBackend LinuxBackend(Window window, PopupWindowContext context)
    {
        if (WaylandSurfaceInterop.IsNativeWayland(window))
            return new PlasmaOverlayBackend();

        return context.Backend == MpvWindowBackend.X11 || context.WindowId is > 0
            ? new X11OverlayBackend()
            : new UnsupportedOverlayBackend();
    }

    /// The trackers report far more often than mpv's window actually moves, and a re-apply costs a
    /// round trip to the window manager on every platform.
    private OverlayLogicalSize ApplyGeometry(
        IMpvOverlayBackend backend, Window window, OverlayGeometry geometry)
    {
        double scaling = window.RenderScaling;
        if (_appliedGeometry == geometry && _appliedScaling == scaling && _appliedSize.Height > 0)
            return _appliedSize;

        _appliedSize = backend.Apply(window, geometry);
        _appliedGeometry = geometry;
        _appliedScaling = scaling;
        return _appliedSize;
    }

    private void OnBackendGeometryChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnBackendGeometryChanged);
            return;
        }

        if (_window is not { } window || _backend is not { } backend) return;
        if (backend.TryGetGeometry(_context) is not { } geometry)
        {
            if (!_windowFound) return;
            _windowFound = false;
            _control?.SetFrame(null);
            return;
        }

        ApplyGeometry(backend, window, geometry);

        if (_windowFound) return;
        _windowFound = true;
        AvailabilityChanged?.Invoke();
    }

    private async Task TearDownAsync()
    {
        Interlocked.Increment(ref _revision);
        _windowFound = false;
        _preparedFor = null;
        _appliedGeometry = null;
        _appliedSize = default;

        if (_backend is { } backend)
        {
            backend.GeometryChanged -= OnBackendGeometryChanged;
            await backend.DisposeAsync();
            _backend = null;
        }

        _control?.SetFrame(null);
        _control?.DisposePainter();
        _control = null;

        _window?.Close();
        _window = null;

        _fonts.Dispose();
    }
}
