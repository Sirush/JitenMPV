namespace JitenMPV.Core.Interaction;

/// <summary>The pointer position reported by mpv, in its OSD/client coordinates.</summary>
public sealed record PopupPointerPosition(double X, double Y);

/// <summary>
/// Native window information reported by the mpv instance that owns this plugin process.
/// WindowId is unavailable for video outputs, such as native Wayland, that do not expose one.
/// </summary>
public sealed record PopupWindowContext(long? WindowId, IReadOnlyList<string> DisplayNames)
{
    public static PopupWindowContext Empty { get; } = new(null, []);
}
