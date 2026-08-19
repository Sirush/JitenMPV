using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using JitenMPV.App.Platform;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Popup;

/// <summary>
/// Selects a geometry provider and surface adapter from their probed capabilities. The presenter
/// does not know about XIDs, Plasma protocols, screen scaling or Wayland positioning limitations.
/// </summary>
internal sealed class PopupBackendCoordinator : IAsyncDisposable
{
    private readonly PlasmaPopupSurfaceBackend? _plasmaSurface;
    private readonly KWinMpvWindowGeometryProvider? _kwinGeometry;
    private readonly X11MpvWindowGeometryProvider? _x11Geometry;
    private readonly X11PopupSurfaceBackend? _x11Surface;
    private readonly GenericPopupSurfaceBackend _genericSurface = new();

    private IPopupSurfaceBackend _surface;
    private IMpvWindowGeometryProvider? _geometry;
    private PixelPoint? _pointerAnchor;

    public PopupBackendCoordinator()
    {
        _surface = _genericSurface;
        if (!OperatingSystem.IsLinux())
            return;

        var plasmaConnections = new PlasmaWaylandConnectionStore();
        _plasmaSurface = new PlasmaPopupSurfaceBackend(plasmaConnections);
        _kwinGeometry = new KWinMpvWindowGeometryProvider(plasmaConnections);
        _x11Geometry = new X11MpvWindowGeometryProvider();
        _x11Surface = new X11PopupSurfaceBackend();
        _kwinGeometry.GeometryChanged += () => GeometryChanged?.Invoke();
    }

    public event Action? GeometryChanged;

    public PopupBackendCapabilities Capabilities =>
        _surface.Capabilities
        | (_geometry?.Status == GeometryProviderStatus.Ready
            ? PopupBackendCapabilities.WindowGeometry
            : PopupBackendCapabilities.None);

    public PopupSupportLevel SupportLevel { get; private set; } =
        PopupSupportLevel.Unknown;

    public bool UsesNativeWayland { get; private set; }

    // Until Avalonia exposes wl_surface destruction as a supported lifecycle hook, closing is the
    // only safe way to guarantee Plasma metadata dies before the surface it decorates.
    public bool RequiresWindowRecreationAfterHide =>
        UsesNativeWayland
        && _plasmaSurface is not null
        && ReferenceEquals(_surface, _plasmaSurface);

    /// <summary>
    /// Samples the anchor the popup is placed against, once per showing. A reposition triggered by
    /// a content resize or a window-context update must not move the popup to wherever the cursor
    /// has travelled since. Platforms that derive the anchor from mpv's own client geometry keep
    /// no sample.
    /// </summary>
    public void CapturePointerAnchor() =>
        _pointerAnchor = OperatingSystem.IsLinux()
            ? null
            : CursorPositionHelper.GetCursorPosition();

    public async ValueTask PrepareAsync(
        Window window,
        PopupWindowContext context,
        CancellationToken ct)
    {
        UsesNativeWayland = OperatingSystem.IsLinux()
                            && WaylandSurfaceInterop.IsNativeWayland(window);
        _x11Surface?.UpdateContext(context);

        if (UsesNativeWayland)
        {
            // Linux construction creates these as one matched protocol session.
            var plasmaSurface = _plasmaSurface!;
            var kwinGeometry = _kwinGeometry!;
            await plasmaSurface.PrepareAsync(window, ct);
            if (plasmaSurface.IsSupported
                && kwinGeometry.Status == GeometryProviderStatus.Ready)
            {
                _surface = plasmaSurface;
                _geometry = kwinGeometry;
                SupportLevel = PopupSupportLevel.Full;
                return;
            }

            _surface = _genericSurface;
            _geometry = null;
            SupportLevel = PopupSupportLevel.Approximate;
            return;
        }

        if (OperatingSystem.IsLinux()
            && (context.Backend == MpvWindowBackend.X11
                || context.WindowId is > 0))
        {
            _surface = _x11Surface!;
            _geometry = _x11Geometry;
            SupportLevel = PopupSupportLevel.Full;
            await _surface.PrepareAsync(window, ct);
            return;
        }

        _surface = _genericSurface;
        _geometry = null;
        SupportLevel = OperatingSystem.IsLinux()
            ? PopupSupportLevel.Approximate
            : PopupSupportLevel.Full;
        await _surface.PrepareAsync(window, ct);
    }

    public async ValueTask PositionAsync(
        Window window,
        PopupWindowContext context,
        PopupPointerPosition pointer,
        PopupPlacementRequest request,
        LogicalSize logicalPopupSize,
        IPopupPositionCalculator calculator,
        CancellationToken ct)
    {
        var discovered = _geometry is null
            ? null
            : await _geometry.GetGeometryAsync(context, ct);

        var globalCursor = _pointerAnchor;
        var screen = SelectScreen(
            window, context, discovered, globalCursor);
        if (screen is null)
            return;

        var output = NormalizeOutput(screen, context);
        var geometry = discovered is null
            ? FallbackGeometry(context, output)
            : discovered with
            {
                Output = output,
                Scale = output.Scale
            };

        var globalPointer = ResolvePointer(
            context,
            new SurfacePoint(pointer.X, pointer.Y),
            geometry,
            UsesNativeWayland,
            discovered is not null,
            globalCursor);
        var popupSize = UsesNativeWayland
            ? logicalPopupSize
            : new LogicalSize(
                logicalPopupSize.Width * output.Scale,
                logicalPopupSize.Height * output.Scale);

        var placement = calculator.Calculate(
            request with { Pointer = globalPointer },
            geometry,
            popupSize);
        await _surface.SetPositionAsync(
            window, placement.Position, popupSize, placement.Output, ct);
    }

    public ValueTask DetachAsync(Window window, CancellationToken ct) =>
        _surface.DetachAsync(window, ct);

    public async ValueTask DisposeAsync()
    {
        if (_kwinGeometry is not null)
            await _kwinGeometry.DisposeAsync();
        if (_plasmaSurface is not null)
            await _plasmaSurface.DisposeAsync();
        if (_x11Geometry is not null)
            await _x11Geometry.DisposeAsync();
        if (_x11Surface is not null)
            await _x11Surface.DisposeAsync();
        await _genericSurface.DisposeAsync();
    }

    private static Screen? SelectScreen(
        Window window,
        PopupWindowContext context,
        MpvWindowGeometry? geometry,
        PixelPoint? globalCursor)
    {
        if (geometry is not null)
        {
            var center = new PixelPoint(
                checked((int)Math.Round(
                    geometry.ClientOrigin.X + geometry.ClientSize.Width / 2)),
                checked((int)Math.Round(
                    geometry.ClientOrigin.Y + geometry.ClientSize.Height / 2)));
            var byGeometry = window.Screens.ScreenFromPoint(center);
            if (byGeometry is not null)
                return byGeometry;
        }

        if (globalCursor is { } cursor
            && window.Screens.ScreenFromPoint(cursor) is { } byCursor)
            return byCursor;

        var byName = window.Screens.All.FirstOrDefault(screen =>
            screen.DisplayName is { } name
            && context.DisplayNames.Any(display =>
                string.Equals(display, name, StringComparison.OrdinalIgnoreCase)));
        if (byName is not null)
            return byName;

        return window.Screens.Primary;
    }

    private static OutputInfo NormalizeOutput(
        Screen screen,
        PopupWindowContext context)
    {
        var bounds = screen.Bounds;
        var workingArea = screen.WorkingArea;
        return new OutputInfo(
            context.DisplayNames.FirstOrDefault(display =>
                string.Equals(
                    display,
                    screen.DisplayName,
                    StringComparison.OrdinalIgnoreCase))
            ?? screen.DisplayName,
            screen.DisplayName,
            new LogicalRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            new LogicalRect(
                workingArea.X, workingArea.Y,
                workingArea.Width, workingArea.Height),
            screen.Scaling,
            null);
    }

    private static MpvWindowGeometry FallbackGeometry(
        PopupWindowContext context,
        OutputInfo output) =>
        new(
            new GlobalLogicalPoint(output.Bounds.X, output.Bounds.Y),
            new LogicalSize(output.Bounds.Width, output.Bounds.Height),
            output,
            context.IsFullscreen,
            output.Scale);

    private static GlobalLogicalPoint? ResolvePointer(
        PopupWindowContext context,
        SurfacePoint pointer,
        MpvWindowGeometry geometry,
        bool nativeWayland,
        bool hasDiscoveredGeometry,
        PixelPoint? globalCursor)
    {
        if (hasDiscoveredGeometry
            && (nativeWayland
                || context.Backend == MpvWindowBackend.X11
                || context.WindowId is > 0))
        {
            return new GlobalLogicalPoint(
                geometry.ClientOrigin.X + pointer.X,
                geometry.ClientOrigin.Y + pointer.Y);
        }

        if (globalCursor is { } cursor)
            return new GlobalLogicalPoint(cursor.X, cursor.Y);

        return null;
    }

    private sealed class GenericPopupSurfaceBackend : IPopupSurfaceBackend
    {
        public PopupBackendCapabilities Capabilities =>
            PopupBackendCapabilities.NonActivating
            | PopupBackendCapabilities.Interactive;

        public ValueTask PrepareAsync(Window window, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
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
            if (!WaylandSurfaceInterop.IsNativeWayland(window))
            {
                window.Position = new PixelPoint(
                    checked((int)Math.Round(position.X)),
                    checked((int)Math.Round(position.Y)));
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DetachAsync(Window window, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
