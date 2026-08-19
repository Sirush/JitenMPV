using JitenMPV.Core.Config;
using JitenMPV.Core.Interaction;

namespace JitenMPV.Core.Rendering;

/// <param name="Text">The line as mpv gave it, carrying only the file's own breaks. The surface
/// owns joining and wrapping, so nothing here is escaped or pre-broken for ASS.</param>
/// <param name="Runs">Covers <paramref name="Text"/> in order.</param>
public sealed record SubtitleFrame(
    string Text,
    IReadOnlyList<SubtitleRun> Runs,
    IReadOnlyDictionary<(int WordId, byte ReadingIndex), IReadOnlyList<UnderlineBar>>? UnderlineBars,
    bool ShowDebugHitboxes);

/// Draws subtitles outside mpv, in a window of the host toolkit's own. Implemented in the UI layer.
public interface ISubtitleOverlaySurface
{
    /// False whenever the platform cannot host the overlay at all, so the caller never tries.
    bool IsAvailable { get; }

    /// Raised when the mpv window is found again after a lookup failed, so a caller that fell back
    /// to mpv rendering can retry.
    event Action? AvailabilityChanged;

    /// Null means the surface could not draw this frame and the caller must render it through mpv.
    /// A non-null result is in the 720-tall overlay space with hit regions already assigned.
    Task<IReadOnlyList<WordRect>?> ShowAsync(SubtitleFrame frame, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);

    void UpdateSettings(PluginSettings settings);
    void UpdateOsd(int width, int height);
    void UpdateWindowContext(PopupWindowContext context);

    Task ShutdownAsync();
}
