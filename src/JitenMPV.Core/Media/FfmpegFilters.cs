using System.Globalization;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Media;

public static class FfmpegFilters
{
    /// Fits inside a square box without ever upscaling, matching the Greater semantics of the
    /// server's ImageMagick resize so its own pass is a no-op.
    public static string Scale(int maxEdge)
        => $"scale=w='min({maxEdge},iw)':h='min({maxEdge},ih)'"
           + ":force_original_aspect_ratio=decrease:flags=lanczos";

    /// The stderr signature of an ffmpeg built without libass, so the fallback latches instead of
    /// retrying a filter that can never load.
    public static bool IsMissingSubtitlesFilter(string stderr)
        => stderr.Contains("No such filter: 'subtitles'", StringComparison.OrdinalIgnoreCase)
           || stderr.Contains("Unknown filter 'subtitles'", StringComparison.OrdinalIgnoreCase);

    public static string Seconds(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
