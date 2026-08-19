using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using JitenMPV.App.Platform;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Overlay;

/// X11 and XWayland: geometry read straight off mpv's XID, the overlay shaped out of the input
/// path with XFixes and kept above mpv by transient-for.
internal sealed class X11OverlayBackend : IMpvOverlayBackend
{
    /// X11 geometry is poll-only in this codebase; anything faster than this buys smoother window
    /// dragging at the cost of a round trip per tick for the whole session.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _poll;
    private long? _mpvWindowId;
    private bool _inputRegionCleared;
    private bool _polling;

    public X11OverlayBackend()
    {
        _poll = new DispatcherTimer(PollInterval, DispatcherPriority.Background,
            (_, _) => GeometryChanged?.Invoke());
    }

    public bool IsSupported => _inputRegionCleared;

    public event Action? GeometryChanged;

    public ValueTask PrepareAsync(Window window, PopupWindowContext context, CancellationToken ct)
    {
        _mpvWindowId = context.WindowId;
        _inputRegionCleared = X11MpvWindowBridge.SetEmptyInputRegion(window);
        if (!_inputRegionCleared) return ValueTask.CompletedTask;

        X11MpvWindowBridge.SetTransientOwner(window, _mpvWindowId);
        if (!_polling)
        {
            _polling = true;
            _poll.Start();
        }

        return ValueTask.CompletedTask;
    }

    public OverlayGeometry? TryGetGeometry(PopupWindowContext context)
    {
        _mpvWindowId = context.WindowId;
        if (!X11MpvWindowBridge.TryGetClientGeometry(_mpvWindowId, out var origin, out var size))
            return null;
        if (size.Width <= 0 || size.Height <= 0) return null;

        return new OverlayGeometry(origin.X, origin.Y, size.Width, size.Height);
    }

    public OverlayLogicalSize Apply(Window window, OverlayGeometry geometry)
    {
        var size = OverlayGeometryApplier.ApplyPhysical(window, geometry);

        // X11 drops transient-for whenever the window manager re-parents the overlay, which a move
        // between monitors or a fullscreen toggle does.
        X11MpvWindowBridge.SetTransientOwner(window, _mpvWindowId);
        return size;
    }

    public ValueTask DisposeAsync()
    {
        _polling = false;
        _poll.Stop();
        return ValueTask.CompletedTask;
    }
}
