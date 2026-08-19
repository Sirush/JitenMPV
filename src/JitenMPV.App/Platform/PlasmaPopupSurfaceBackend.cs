using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using JitenMPV.App.Popup;

namespace JitenMPV.App.Platform;

/// <summary>
/// KDE surface adapter. It owns only popup placement/stacking; KWin foreign-window geometry is a
/// separate provider sharing the same connection-scoped protocol session.
/// </summary>
internal sealed class PlasmaPopupSurfaceBackend(
    PlasmaWaylandConnectionStore connections) : IPopupSurfaceBackend
{
    private bool _reportedFailure;

    public PopupBackendCapabilities Capabilities =>
        PopupBackendCapabilities.ExactGlobalPosition
        | PopupBackendCapabilities.AboveFullscreen
        | PopupBackendCapabilities.MultiMonitor
        | PopupBackendCapabilities.NonActivating
        | PopupBackendCapabilities.Interactive;

    public bool IsSupported => connections.Current?.HasPopupSurface == true;

    public async ValueTask PrepareAsync(Window window, CancellationToken ct)
    {
        var interop = WaylandSurfaceInterop.TryCreate(
            window, createSurfaceIfMissing: true);
        if (interop is null)
            return;

        try
        {
            await interop.InvokeAsync(context =>
            {
                connections.Get(context.Globals).Attach(context.Surface);
            }, WaylandCommitBehavior.WithNextFrame, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportFailure(ex);
        }
    }

    public async ValueTask SetPositionAsync(
        Window window,
        GlobalLogicalPoint position,
        LogicalSize size,
        OutputInfo output,
        CancellationToken ct)
    {
        var interop = WaylandSurfaceInterop.TryCreate(
            window, createSurfaceIfMissing: false);
        if (interop is null)
            return;

        try
        {
            await interop.InvokeAsync(context =>
            {
                connections.Get(context.Globals).SetPosition(
                    context.Surface,
                    checked((int)Math.Round(position.X)),
                    checked((int)Math.Round(position.Y)),
                    output);
            }, WaylandCommitBehavior.WithNextFrame, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportFailure(ex);
        }
    }

    public async ValueTask DetachAsync(Window window, CancellationToken ct)
    {
        var interop = WaylandSurfaceInterop.TryCreate(
            window, createSurfaceIfMissing: false);
        if (interop is null)
            return;

        try
        {
            await interop.InvokeAsync(context =>
            {
                connections.Get(context.Globals).Detach(context.Surface);
            }, WaylandCommitBehavior.NoCommit, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportFailure(ex);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void ReportFailure(Exception exception)
    {
        if (_reportedFailure)
            return;
        _reportedFailure = true;
        Trace.TraceWarning(
            "Plasma Wayland popup surface integration is unavailable: {0}",
            exception.GetBaseException().Message);
    }
}
