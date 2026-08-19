using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Overlay;

/// mpv's client area. Units are the platform's own: physical pixels where window positions are
/// physical, logical points where the compositor deals only in those.
internal readonly record struct OverlayGeometry(
    double X, double Y, double Width, double Height);

internal readonly record struct OverlayLogicalSize(double Width, double Height);

/// Finds mpv's video window, keeps the overlay over it, and makes the overlay click-through.
internal interface IMpvOverlayBackend : IAsyncDisposable
{
    /// False where the platform cannot host the overlay at all, which is the caller's cue to leave
    /// the subtitle to mpv.
    bool IsSupported { get; }

    event Action? GeometryChanged;

    ValueTask PrepareAsync(Window window, PopupWindowContext context, CancellationToken ct);

    OverlayGeometry? TryGetGeometry(PopupWindowContext context);

    OverlayLogicalSize Apply(Window window, OverlayGeometry geometry);
}

internal static class OverlayGeometryApplier
{
    /// Window.Position is physical everywhere Avalonia exposes it, while Width/Height are logical.
    public static OverlayLogicalSize ApplyPhysical(Window window, OverlayGeometry geometry)
    {
        window.Position = new PixelPoint(
            (int)Math.Round(geometry.X), (int)Math.Round(geometry.Y));

        double scale = window.RenderScaling > 0 ? window.RenderScaling : 1;
        var size = new OverlayLogicalSize(geometry.Width / scale, geometry.Height / scale);
        window.Width = size.Width;
        window.Height = size.Height;
        return size;
    }
}

/// Stands in wherever no backend can place the overlay, so the caller falls back to mpv rendering
/// instead of drawing subtitles at a guessed position.
internal sealed class UnsupportedOverlayBackend : IMpvOverlayBackend
{
    public bool IsSupported => false;

    public event Action? GeometryChanged
    {
        add { }
        remove { }
    }

    public ValueTask PrepareAsync(Window window, PopupWindowContext context, CancellationToken ct)
        => ValueTask.CompletedTask;

    public OverlayGeometry? TryGetGeometry(PopupWindowContext context) => null;

    public OverlayLogicalSize Apply(Window window, OverlayGeometry geometry) => default;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
