using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.Popup;

[Flags]
public enum PopupBackendCapabilities
{
    None = 0,
    ExactGlobalPosition = 1 << 0,
    AboveFullscreen = 1 << 1,
    WindowGeometry = 1 << 2,
    MultiMonitor = 1 << 3,
    NonActivating = 1 << 4,
    Interactive = 1 << 5
}

public enum GeometryProviderStatus
{
    Initializing,
    Ready,
    Unsupported,
    Disconnected,
    Faulted
}

public interface IPopupSurfaceBackend : IAsyncDisposable
{
    PopupBackendCapabilities Capabilities { get; }

    ValueTask PrepareAsync(Window window, CancellationToken ct);

    ValueTask SetPositionAsync(
        Window window,
        GlobalLogicalPoint position,
        LogicalSize size,
        OutputInfo output,
        CancellationToken ct);

    ValueTask DetachAsync(Window window, CancellationToken ct);
}

public interface IMpvWindowGeometryProvider : IAsyncDisposable
{
    event Action? GeometryChanged;

    GeometryProviderStatus Status { get; }

    ValueTask<MpvWindowGeometry?> GetGeometryAsync(
        PopupWindowContext context,
        CancellationToken ct);
}
