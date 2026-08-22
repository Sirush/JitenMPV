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
using Microsoft.Extensions.Logging;

namespace JitenMPV.App.Overlay;

/// Draws the subtitle into a transparent window of this process, positioned over mpv's client
/// area. Everything below the interface runs on the UI thread; the plugin host calls in from its
/// own render loop.
public sealed class AvaloniaSubtitleOverlaySurface : ISubtitleOverlaySurface
{
    /// Consecutive frames that may fail to lease Skia before the surface gives up for good.
    private const int LeaseFailureLimit = 3;

    private readonly SubtitleFontStore _fonts = new();
    private readonly SubtitleLayoutEngine _engine;
    private readonly ILogger _logger;

    private volatile PluginSettings? _settings;
    private volatile PopupWindowContext _context = PopupWindowContext.Empty;
    private volatile int _osdWidth;
    private volatile int _osdHeight;
    private volatile bool _unavailable;

    private SubtitleOverlayWindow? _window;
    private SubtitleOverlayControl? _control;
    private IMpvOverlayBackend? _backend;
    private SubtitleFrame? _lastFrame;
    private (int? ProcessId, long? WindowId, bool Fullscreen)? _preparedFor;
    private OverlayGeometry? _appliedGeometry;
    private double _appliedScaling;
    private OverlayLogicalSize _appliedSize;
    private bool _windowFound;
    private bool _revealed;
    private int _leaseFailures;
    private long _revision;

    public AvaloniaSubtitleOverlaySurface(ILogger logger)
    {
        _logger = logger;
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
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            _lastFrame = null;
            _control?.SetFrame(null);
        }).GetTask();
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
            // An identity mpv has not reported yet looks exactly like a platform that cannot host
            // the overlay, and only the second of those is permanent.
            if (context.WindowId is not null || context.ProcessId is not null)
                _unavailable = true;
            return null;
        }

        if (backend.TryGetGeometry(context) is not { } geometry)
        {
            if (_windowFound)
                _logger.LogDebug("mpv window not found; clearing the overlay");
            _windowFound = false;
            _control!.SetFrame(null);
            return null;
        }

        _windowFound = true;
        ApplyStacking(window, context);
        var size = ApplyGeometry(backend, window, geometry);
        if (size.Height <= 0) return null;

        // A slower frame must never overwrite a newer one, and the marshal to this thread is where
        // the two can cross.
        if (revision != Volatile.Read(ref _revision) || ct.IsCancellationRequested) return null;

        var laidOut = _engine.Layout(frame, settings, osdWidth, osdHeight, size.Height);
        if (laidOut is null) return null;

        _lastFrame = frame;
        _control!.SetFrame(laidOut);
        Reveal();
        _logger.LogDebug(
            "Overlay drew {Chars} chars as {Rects} hit regions at scale {Scale}",
            frame.Text.Length, laidOut.Rects.Count, laidOut.Scale);

        // Reported one frame late, so this judges the previous pass. A resize or reparent can cost
        // a single pass its lease, so only a run of them means the platform cannot draw.
        if (_control.LeaseUnavailable)
        {
            _control.ClearLeaseFailure();
            _leaseFailures++;
            _logger.LogDebug("Overlay render found no Skia lease ({Count} in a row)", _leaseFailures);
            if (_leaseFailures < LeaseFailureLimit) return null;

            _unavailable = true;
            return null;
        }

        _leaseFailures = 0;
        return laidOut.Rects;
    }

    private SubtitleOverlayWindow EnsureWindow()
    {
        if (_window is not null) return _window;

        _control = new SubtitleOverlayControl(_fonts);
        _window = new SubtitleOverlayWindow { Content = _control, Opacity = 0 };

        // The platform handle every backend configures only exists once the window has been mapped.
        _window.Show();
        return _window;
    }

    /// Preparation attaches hooks and window-manager state, so it is redone only when mpv's window
    /// identity changes rather than once per subtitle line.
    private async ValueTask<IMpvOverlayBackend> EnsureBackendAsync(
        Window window, PopupWindowContext context, CancellationToken ct)
    {
        // Fullscreen is part of the key because entering or leaving it makes the window manager
        // reparent the overlay, dropping the window-manager state PrepareAsync installed.
        var key = (context.ProcessId, context.WindowId, context.IsFullscreen);
        if (_backend is { } existing)
        {
            if (_preparedFor != key)
            {
                _preparedFor = key;
                await existing.PrepareAsync(window, context, ct);
            }

            return existing;
        }

        var backend = await CreateBackendAsync(window, context, ct);
        if (!backend.IsSupported) return backend;

        _preparedFor = key;
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
        if (_appliedGeometry == geometry && _appliedScaling == scaling && _appliedSize.Height > 0
            && !backend.NeedsReapply(window))
            return _appliedSize;

        _appliedSize = backend.Apply(window, geometry);

        // The output lookup in Apply comes back empty for a point no monitor covers, leaving it on
        // the window's own scaling; settling that here keeps the layout off the wrong size.
        if (window.RenderScaling != scaling)
        {
            scaling = window.RenderScaling;
            _appliedSize = backend.Apply(window, geometry);
        }

        _appliedGeometry = geometry;
        _appliedScaling = scaling;
        _logger.LogDebug(
            "Overlay applied at ({X},{Y}) {W}x{H} physical, {LW}x{LH} logical, scaling {Scaling}",
            geometry.X, geometry.Y, geometry.Width, geometry.Height,
            _appliedSize.Width, _appliedSize.Height, scaling);
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

        ApplyStacking(window, _context);
        var previousSize = _appliedSize;
        ApplyGeometry(backend, window, geometry);
        if (_appliedSize != previousSize) Resized();

        if (_windowFound) return;
        _windowFound = true;

        // The frame was cleared when the window went missing, so finding it again has to put the
        // line back. Without this the overlay stays blank until mpv happens to send a new one.
        _logger.LogDebug("mpv window found again; restoring the overlay");
        RelayoutLastFrame();
        AvailabilityChanged?.Invoke();
    }

    /// A fullscreen mpv is promoted above ordinary windows and being its transient does not lift the
    /// overlay with it; the dictionary popup clears the same bar only by also asking to be kept
    /// above. Dropped again when windowed, so the overlay never floats over other applications.
    private void ApplyStacking(Window window, PopupWindowContext context)
    {
        if (window.Topmost == context.IsFullscreen) return;

        window.Topmost = context.IsFullscreen;
        _logger.LogDebug("Overlay keep-above set to {Topmost}", context.IsFullscreen);
    }

    /// A resize invalidates the overlay either way: a held frame was measured against the old size,
    /// and holding none means mpv's current line was never drawn here. The second case stays blank
    /// until mpv sends the next line unless the host is asked for one.
    private void Resized()
    {
        if (_lastFrame is not null)
        {
            RelayoutLastFrame();
            return;
        }

        _logger.LogDebug("Overlay resized with no frame to redraw; asking for one");
        AvailabilityChanged?.Invoke();
    }

    /// A laid-out frame carries the scale it was measured at, so a window whose height has changed
    /// keeps drawing at the old one. Only the drawing depends on it; hit regions are overlay-space.
    private void RelayoutLastFrame()
    {
        if (_lastFrame is not { } frame || _settings is not { } settings) return;
        if (_control is not { } control || _appliedSize.Height <= 0) return;

        int osdWidth = _osdWidth;
        int osdHeight = _osdHeight;
        if (osdWidth <= 0 || osdHeight <= 0) return;

        if (_engine.Layout(frame, settings, osdWidth, osdHeight, _appliedSize.Height) is { } laidOut)
        {
            _logger.LogDebug("Overlay re-laid out for height {Height}", _appliedSize.Height);
            control.SetFrame(laidOut);
        }
    }

    /// The window is mapped before its first placement so the backends can reach its platform
    /// handle, leaving it briefly wherever the window manager put it. Revealing it only once a
    /// frame is laid out at the settled geometry keeps that from being seen as a subtitle sliding in.
    private void Reveal()
    {
        if (_revealed || _window is null) return;
        _revealed = true;
        Dispatcher.UIThread.Post(
            () => { if (_window is { } window) window.Opacity = 1; }, DispatcherPriority.Render);
    }

    private async Task TearDownAsync()
    {
        Interlocked.Increment(ref _revision);
        _lastFrame = null;
        _revealed = false;
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
