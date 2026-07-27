using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

public sealed record ScreenshotRequest(
    MediaTimebase Timebase,
    MediaSubtitleBurn Burn,
    MediaImageSource Source,
    double SubtitleStart,
    double SubtitleEnd,
    double PlaybackPosition,
    string? SubtitleFilter);

/// <param name="ffmpeg">
/// Null when ffmpeg could not be resolved. The mpv screenshot still works; the PNG is then uploaded
/// as-is and the server's own normalization does the WebP conversion.
/// </param>
public sealed class ScreenshotCapture(
    FfmpegRunner? ffmpeg, MediaTempFiles temp, PluginSettings settings, ILogger logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    /// CardMediaImageProcessor.WebpQuality: the quality the server re-encodes to, and so the floor
    /// worth sending at.
    private const int ServerQuality = 82;

    /// Latched once a burn attempt proves this ffmpeg cannot load libass, so the fallback is not
    /// re-attempted on every capture for the rest of the session.
    private static volatile bool _subtitlesFilterUnavailable;

    public static bool SubtitlesFilterUnavailable => _subtitlesFilterUnavailable;

    public static void MarkSubtitlesFilterUnavailable(ILogger logger)
    {
        if (_subtitlesFilterUnavailable) return;
        _subtitlesFilterUnavailable = true;
        logger.LogWarning("ffmpeg has no subtitles filter; burn-in disabled for this session");
    }

    public async Task<CapturedImage?> CaptureAsync(
        ScreenshotRequest request, MpvIpcClient ipc, CancellationToken ct)
    {
        var png = await AcquireFrameAsync(request, ipc, ct);
        if (png is null) return null;

        var bytes = ffmpeg is null ? null : await EncodeWebpAsync(png, ct);
        if (bytes is not null)
            return new CapturedImage(bytes, "image/webp", "capture.webp", Frames: 1, Duration: 0);

        var raw = await File.ReadAllBytesAsync(png, ct);
        return raw.Length <= MediaLimits.UploadHardLimitBytes
            ? new CapturedImage(raw, "image/png", "capture.png", Frames: 1, Duration: 0)
            : null;
    }

    private async Task<string?> AcquireFrameAsync(
        ScreenshotRequest request, MpvIpcClient ipc, CancellationToken ct)
    {
        var burnWanted = ffmpeg is not null
                         && request.Burn == MediaSubtitleBurn.Original
                         && request.SubtitleFilter is not null
                         && !_subtitlesFilterUnavailable
                         && request.Timebase.IsSeekableFile;

        if (ffmpeg is null || (!burnWanted && request.Source == MediaImageSource.MpvFrame))
        {
            var flags = request.Burn == MediaSubtitleBurn.Colored ? "window" : "video";
            return await MpvScreenshotAsync(ipc, flags, ct);
        }

        // Original burn-in and the midpoint source both need a decode ffmpeg controls; a window
        // capture cannot be seeked, so Colored + midpoint falls back to the video frame.
        var seek = request.Source == MediaImageSource.SubtitleMidpoint
            ? (request.SubtitleStart + request.SubtitleEnd) / 2
            : request.Timebase.PlaybackToVideoTime(request.PlaybackPosition);

        var frame = await FfmpegFrameAsync(request, request.Timebase.Clamp(seek), burnWanted, ct);
        if (frame is not null) return frame;

        return await MpvScreenshotAsync(ipc, request.Burn == MediaSubtitleBurn.Colored ? "window" : "video", ct);
    }

    private async Task<string?> MpvScreenshotAsync(MpvIpcClient ipc, string flags, CancellationToken ct)
    {
        var path = temp.PathFor($"frame-{Guid.NewGuid():N}.png");
        try
        {
            await ipc.ScreenshotToFileAsync(path, flags, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "mpv screenshot ({Flags}) failed", flags);
            return null;
        }

        return File.Exists(path) ? path : null;
    }

    private async Task<string?> FfmpegFrameAsync(
        ScreenshotRequest request, double seek, bool burn, CancellationToken ct)
    {
        var output = temp.PathFor($"frame-{Guid.NewGuid():N}.png");
        var args = new List<string>();

        if (burn)
        {
            // -copyts keeps the file's own clock so libass matches its cues; setpts after the
            // subtitles filter rebases the single output frame back to zero.
            args.AddRange(["-ss", FfmpegFilters.Seconds(seek), "-copyts", "-i", request.Timebase.VideoPath]);
            args.AddRange(["-vf", $"{request.SubtitleFilter},setpts=PTS-STARTPTS"]);
        }
        else
        {
            args.AddRange(["-ss", FfmpegFilters.Seconds(seek), "-i", request.Timebase.VideoPath]);
        }

        args.AddRange(["-frames:v", "1", "-an", "-sn", "-dn", "-c:v", "png", output]);

        var result = await ffmpeg!.RunAsync(args, Timeout, ct);
        if (result.Succeeded && File.Exists(output))
            return output;

        if (burn && FfmpegFilters.IsMissingSubtitlesFilter(result.Stderr))
            MarkSubtitlesFilterUnavailable(logger);
        else
            logger.LogWarning("Frame grab failed (exit {Code}): {Error}", result.ExitCode, result.ErrorTail);

        return null;
    }

    /// <summary>
    /// Resizes to the box the server uses so its own resize is a no-op, and encodes above the
    /// server's own quality: it re-encodes every image to WebP q82 unconditionally, so matching that
    /// here would compress twice at the same setting and lose detail for nothing. The quota counts
    /// the server's output rather than the upload, so the larger file costs bandwidth only.
    /// </summary>
    private async Task<byte[]?> EncodeWebpAsync(string pngPath, CancellationToken ct)
    {
        var bytes = await RunWebpAsync(pngPath, settings.MediaImageQuality, ct);
        if (bytes is null || bytes.Length <= MediaLimits.UploadHardLimitBytes)
            return bytes;

        logger.LogInformation("Screenshot was {Size} KB at quality {Quality}; retrying at {Fallback}",
            bytes.Length / 1024, settings.MediaImageQuality, ServerQuality);

        var smaller = await RunWebpAsync(pngPath, ServerQuality, ct);
        return smaller is { Length: <= MediaLimits.UploadHardLimitBytes } ? smaller : null;
    }

    private async Task<byte[]?> RunWebpAsync(string pngPath, int quality, CancellationToken ct)
    {
        var output = temp.PathFor($"image-{Guid.NewGuid():N}.webp");
        var args = new List<string>
        {
            "-i", pngPath,
            "-vf", FfmpegFilters.Scale(settings.MediaImageMaxEdge),
            "-c:v", "libwebp"
        };

        // libwebp's own -quality 100 is still lossy, so the top of the range is mapped to the
        // lossless mode that actually keeps the server's re-encode the only lossy step.
        if (quality >= 100)
            args.AddRange(["-lossless", "1"]);
        else
            args.AddRange(["-lossless", "0", "-quality", quality.ToString(), "-preset", "picture"]);

        args.AddRange(["-compression_level", "6", "-an", "-sn", "-dn", output]);

        var result = await ffmpeg!.RunAsync(args, Timeout, ct);
        if (result.Succeeded && File.Exists(output))
            return await File.ReadAllBytesAsync(output, ct);

        logger.LogWarning("WebP encode failed (exit {Code}): {Error}", result.ExitCode, result.ErrorTail);
        return null;
    }
}
