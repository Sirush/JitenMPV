using JitenMPV.Core.Config;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

public sealed class AnimationCapture(
    FfmpegRunner ffmpeg, MediaTempFiles temp, PluginSettings settings, ILogger logger)
{
    private const int MaxAttempts = 4;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    /// Encodes the subtitle's span as an animated WebP, stepping down the size ladder until it fits.
    /// Returns null when even the smallest rung exceeds the hard limit; the caller then falls back
    /// to a still.
    /// <param name="subtitleFilter">
    /// Null leaves the frames clean. Colored has no window-capture equivalent for a moving image,
    /// so it burns the original styling here just as Original does.
    /// </param>
    public async Task<CapturedImage?> CaptureAsync(
        MediaTimebase timebase, MediaSubtitleBurn burn, string? subtitleFilter,
        double start, double end, CancellationToken ct)
    {
        var plan = AnimationBudget.Solve(end - start, settings);
        var burnIn = burn != MediaSubtitleBurn.None
                     && subtitleFilter is not null
                     && !ScreenshotCapture.SubtitlesFilterUnavailable;

        CapturedImage? smallest = null;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var rung = AnimationBudget.Step(plan, attempt, Math.Max(1, settings.MediaAnimMinFps));
            var bytes = await EncodeAsync(timebase, rung, burnIn ? subtitleFilter : null, start, ct);
            if (bytes is null) return smallest;

            var candidate = new CapturedImage(
                bytes, "image/webp", "capture.webp", rung.Frames, rung.Duration);

            if (smallest is null || bytes.Length < smallest.Bytes.Length)
                smallest = candidate;

            if (bytes.Length <= settings.MediaAnimMaxBytes)
            {
                logger.LogInformation("Animation encoded at {Fps} fps / {Edge}px / q{Quality}: {Size} KB",
                    rung.Fps, rung.MaxEdge, rung.Quality, bytes.Length / 1024);
                return candidate;
            }

            logger.LogDebug("Animation attempt {Attempt} was {Size} KB, over the {Cap} KB cap",
                attempt + 1, bytes.Length / 1024, settings.MediaAnimMaxBytes / 1024);
        }

        return smallest is { } best && best.Bytes.Length <= MediaLimits.UploadHardLimitBytes ? best : null;
    }

    /// <summary>
    /// Sizes the clip by encoding two short samples at the real settings. Content decides a clip's
    /// size far more than any formula can predict - the same settings span 25 KB to 1.1 MB for four
    /// seconds depending on how much moves - so this measures instead of guessing. Two samples rather
    /// than one because a clip costs a fixed amount for the opening picture plus a per-picture amount
    /// for what changes after it; a single sample cannot tell those apart and badly overstates a
    /// scene that barely moves.
    /// </summary>
    public async Task<long?> EstimateBytesAsync(
        MediaTimebase timebase, MediaSubtitleBurn burn, string? subtitleFilter,
        double start, double end, CancellationToken ct)
    {
        var plan = AnimationBudget.Solve(end - start, settings);
        var burnIn = burn != MediaSubtitleBurn.None
                     && subtitleFilter is not null
                     && !ScreenshotCapture.SubtitlesFilterUnavailable;
        var filter = burnIn ? subtitleFilter : null;

        var shortSample = await SampleAsync(timebase, plan, filter, start, AnimationBudget.ProbeSeconds, ct);
        if (shortSample is not { } small) return null;

        // The clip is no longer than the sample, so the sample is the clip.
        if (plan.Duration <= AnimationBudget.ProbeSeconds) return small.Bytes;

        var longSample = await SampleAsync(timebase, plan, filter, start, AnimationBudget.ProbeSeconds * 2, ct);
        if (longSample is not { } large || large.Frames <= small.Frames) return small.Bytes;

        var perFrame = Math.Max(0, (large.Bytes - small.Bytes) / (double)(large.Frames - small.Frames));
        var fixedCost = Math.Max(0, small.Bytes - small.Frames * perFrame);

        return Math.Max(large.Bytes, (long)(fixedCost + plan.Frames * perFrame));
    }

    private async Task<(long Bytes, int Frames)?> SampleAsync(
        MediaTimebase timebase, AnimationPlan plan, string? filter,
        double start, double seconds, CancellationToken ct)
    {
        var duration = Math.Min(seconds, plan.Duration);
        var frames = Math.Max(1, (int)Math.Ceiling(duration * plan.Fps));

        var bytes = await EncodeAsync(
            timebase, plan with { Duration = duration, Frames = frames }, filter, start, ct);

        return bytes is null ? null : (bytes.Length, frames);
    }

    private async Task<byte[]?> EncodeAsync(
        MediaTimebase timebase, AnimationPlan plan, string? subtitleFilter,
        double start, CancellationToken ct)
    {
        var output = temp.PathFor($"anim-{Guid.NewGuid():N}.webp");
        var chain = new List<string>();
        var args = new List<string>();

        if (subtitleFilter is not null)
        {
            // The subtitles filter matches the file's own timestamps, which -ss alone would have
            // already rebased to zero; -copyts preserves them and -to becomes absolute in turn.
            args.AddRange(["-ss", FfmpegFilters.Seconds(start), "-copyts", "-i", timebase.VideoPath]);
            args.AddRange(["-to", FfmpegFilters.Seconds(timebase.ToAbsolute(start + plan.Duration))]);
            chain.Add(subtitleFilter);
            chain.Add("setpts=PTS-STARTPTS");
        }
        else
        {
            args.AddRange(["-ss", FfmpegFilters.Seconds(start), "-t", FfmpegFilters.Seconds(plan.Duration)]);
            args.AddRange(["-i", timebase.VideoPath]);
        }

        chain.Add($"fps={plan.Fps}");
        chain.Add(FfmpegFilters.Scale(plan.MaxEdge));

        args.AddRange(["-an", "-sn", "-dn", "-vf", string.Join(',', chain)]);
        args.AddRange(
        [
            "-c:v", "libwebp_anim",
            "-lossless", "0",
            "-quality", plan.Quality.ToString(),
            "-compression_level", "4",
            "-loop", "0",
            output
        ]);

        var result = await ffmpeg.RunAsync(args, Timeout, ct);
        if (result.Succeeded && File.Exists(output))
            return await File.ReadAllBytesAsync(output, ct);

        if (subtitleFilter is not null && FfmpegFilters.IsMissingSubtitlesFilter(result.Stderr))
        {
            ScreenshotCapture.MarkSubtitlesFilterUnavailable(logger);
            return await EncodeAsync(timebase, plan, subtitleFilter: null, start, ct);
        }

        logger.LogWarning("Animation encode failed (exit {Code}): {Error}", result.ExitCode, result.ErrorTail);
        return null;
    }
}
