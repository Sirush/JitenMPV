using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using JitenMPV.App.Platform;
using JitenMPV.App.Popup;
using JitenMPV.Core.Interaction;
using NWayland.Protocols.Wayland;

namespace JitenMPV.App.Overlay;

/// Plasma is the only native-Wayland session that exposes both a foreign-window geometry protocol
/// and absolute surface placement, so it is the only one the overlay can be positioned on.
internal sealed class PlasmaOverlayBackend : IMpvOverlayBackend
{
    private static readonly OutputInfo AnyOutput = new(null, null, default, default, 1, null);

    private readonly PlasmaWaylandConnectionStore _connections = new();
    private PlasmaWaylandConnection? _connection;
    private bool _inputRegionCleared;
    private bool _reportedFailure;

    public bool IsSupported =>
        _inputRegionCleared
        && _connection is { HasPopupSurface: true, GeometryStatus: GeometryProviderStatus.Ready };

    public event Action? GeometryChanged;

    public async ValueTask PrepareAsync(
        Window window, PopupWindowContext context, CancellationToken ct)
    {
        var interop = WaylandSurfaceInterop.TryCreate(window, createSurfaceIfMissing: true);
        if (interop is null) return;

        try
        {
            await interop.InvokeAsync(surface =>
            {
                var connection = _connections.Get(surface.Globals);
                if (!ReferenceEquals(connection, _connection))
                {
                    if (_connection is not null)
                        _connection.GeometryChanged -= OnGeometryChanged;
                    _connection = connection;
                    connection.GeometryChanged += OnGeometryChanged;
                }

                connection.Attach(surface.Surface);
                _inputRegionCleared = ClearInputRegion(surface);
            }, WaylandCommitBehavior.WithNextFrame, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportFailure(ex);
        }
    }

    public OverlayGeometry? TryGetGeometry(PopupWindowContext context)
    {
        if (_connection is not { } connection) return null;
        if (!connection.TryGetGeometry(context, out var geometry)) return null;
        if (geometry.Size.Width <= 0 || geometry.Size.Height <= 0) return null;

        return new OverlayGeometry(
            geometry.Origin.X, geometry.Origin.Y, geometry.Size.Width, geometry.Size.Height);
    }

    /// KWin already works in logical units, so the size goes straight onto the window and only the
    /// position needs the Plasma protocol; Wayland gives a client no way to place itself otherwise.
    public OverlayLogicalSize Apply(Window window, OverlayGeometry geometry)
    {
        window.Width = geometry.Width;
        window.Height = geometry.Height;

        var interop = WaylandSurfaceInterop.TryCreate(window, createSurfaceIfMissing: false);
        if (interop is not null && _connection is not null)
        {
            _ = interop.InvokeAsync(surface =>
                    _connections.Get(surface.Globals).SetPosition(
                        surface.Surface,
                        (int)Math.Round(geometry.X), (int)Math.Round(geometry.Y), AnyOutput),
                WaylandCommitBehavior.WithNextFrame, CancellationToken.None);
        }

        return new OverlayLogicalSize(geometry.Width, geometry.Height);
    }

    /// A region with no rectangles added is the empty one, which is how a Wayland client says it
    /// wants no pointer input at all.
    private static bool ClearInputRegion(WaylandSurfaceContext surface)
    {
        try
        {
            var compositor = PlasmaWaylandConnection.BindGlobal<WlCompositor>(
                surface.Globals, 1, 6, null);
            if (compositor is null) return false;

            var region = compositor.CreateRegion();
            surface.Surface.SetInputRegion(region);
            region.Destroy();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnGeometryChanged() => GeometryChanged?.Invoke();

    private void ReportFailure(Exception exception)
    {
        if (_reportedFailure) return;
        _reportedFailure = true;
        Trace.TraceWarning(
            "Plasma Wayland subtitle overlay integration is unavailable: {0}",
            exception.GetBaseException().Message);
    }

    public ValueTask DisposeAsync()
    {
        if (_connection is not null)
            _connection.GeometryChanged -= OnGeometryChanged;
        _connection = null;
        return ValueTask.CompletedTask;
    }
}
