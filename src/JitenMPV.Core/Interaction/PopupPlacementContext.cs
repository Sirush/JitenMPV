namespace JitenMPV.Core.Interaction;

/// <summary>The pointer position reported by mpv, in its OSD/client coordinates.</summary>
public sealed record PopupPointerPosition(double X, double Y);

public enum MpvWindowBackend
{
    Unknown,
    X11,
    Wayland
}

public enum PopupSupportLevel
{
    Unknown,
    Full,
    Approximate,
    Unsupported
}

public static class MpvWindowBackendDetector
{
    public static MpvWindowBackend FromGpuContext(string? context) =>
        context?.ToLowerInvariant() switch
        {
            "x11" or "x11egl" or "x11vk" => MpvWindowBackend.X11,
            "wayland" or "waylandvk" => MpvWindowBackend.Wayland,
            _ => MpvWindowBackend.Unknown
        };
}

/// <summary>
/// Native window information reported by the mpv instance that owns this plugin process.
/// WindowId is unavailable for video outputs, such as native Wayland, that do not expose one.
/// </summary>
public sealed record PopupWindowContext(
    long? WindowId,
    int? ProcessId,
    IReadOnlyList<string> DisplayNames,
    bool IsFullscreen,
    MpvWindowBackend Backend = MpvWindowBackend.Unknown,
    string? AppId = null,
    string? Title = null)
{
    public static PopupWindowContext Empty { get; } =
        new(null, null, [], false, MpvWindowBackend.Unknown, null, null);
}
