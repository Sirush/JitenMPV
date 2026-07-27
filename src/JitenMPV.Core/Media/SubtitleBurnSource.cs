using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

/// Produces the <c>subtitles=</c> filtergraph fragment libass renders from, resolved once per
/// capture and shared by the still and the animation so both burn identically.
public sealed class SubtitleBurnSource(FfmpegRunner ffmpeg, MediaTempFiles temp, ILogger logger)
{
    private static readonly TimeSpan ExtractTimeout = TimeSpan.FromMinutes(2);

    private string? _resolved;
    private bool _attempted;

    public async Task<string?> ResolveAsync(MediaTimebase timebase, CancellationToken ct)
    {
        if (_attempted) return _resolved;
        _attempted = true;

        _resolved = await BuildAsync(timebase, ct);
        return _resolved;
    }

    private async Task<string?> BuildAsync(MediaTimebase timebase, CancellationToken ct)
    {
        if (timebase.ExternalSubtitlePath is { } external)
        {
            return FfmpegFilterPath.IsEscapable(external)
                ? $"subtitles={FfmpegFilterPath.Escape(external)}"
                : CopyExternal(external);
        }

        if (timebase.SubtitleTrackIndex is not { } index) return null;

        if (FfmpegFilterPath.IsEscapable(timebase.VideoPath))
            return $"subtitles={FfmpegFilterPath.Escape(timebase.VideoPath)}:si={index}";

        return await ExtractTrackAsync(timebase, index, ct);
    }

    private string? CopyExternal(string path)
    {
        var target = temp.PathFor("subs" + Path.GetExtension(path));
        try
        {
            File.Copy(path, target, overwrite: true);
            return $"subtitles={FfmpegFilterPath.Escape(target)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not stage subtitle file for burn-in");
            return null;
        }
    }

    /// The filter reads the styling out of this copy, so attached fonts in the original container
    /// are not available to it and libass falls back to a system font.
    private async Task<string?> ExtractTrackAsync(
        MediaTimebase timebase, int index, CancellationToken ct)
    {
        var target = temp.PathFor("subs.ass");
        var result = await ffmpeg.RunAsync(
            ["-i", timebase.VideoPath, "-map", $"0:s:{index}", "-c:s", "ass", target],
            ExtractTimeout, ct);

        if (result.Succeeded && File.Exists(target))
            return $"subtitles={FfmpegFilterPath.Escape(target)}";

        logger.LogWarning("Could not extract subtitles for burn-in (exit {Code}): {Error}",
            result.ExitCode, result.ErrorTail);
        return null;
    }
}
