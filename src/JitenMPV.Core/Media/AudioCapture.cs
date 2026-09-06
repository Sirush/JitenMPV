using JitenMPV.Core.Config;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

public sealed class AudioCapture(
    FfmpegRunner ffmpeg, MediaTempFiles temp, PluginSettings settings, ILogger logger)
{
    public const int OutputSampleRate = 48_000;

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private static readonly int[] BitrateLadder = [48, 40, 32];

    /// One decode feeds the waveform display, the auto-trim and the in-app preview.
    public async Task<WaveformData> DecodeWindowAsync(
        MediaTimebase timebase, double windowStart, double windowEnd, CancellationToken ct)
    {
        if (timebase.AudioTrackIndex is not { } track) return WaveformData.Empty;

        var duration = windowEnd - windowStart;
        if (duration <= 0) return WaveformData.Empty;

        var (result, bytes) = await ffmpeg.RunCaptureStdoutAsync(
        [
            "-ss", FfmpegFilters.Seconds(windowStart),
            "-t", FfmpegFilters.Seconds(duration),
            "-i", timebase.VideoPath,
            "-map", $"0:a:{track}",
            "-vn", "-sn", "-dn",
            "-ac", "1",
            "-ar", WaveformSampler.SampleRate.ToString(),
            "-f", "s16le", "-"
        ], Timeout, ct);

        if (!result.Succeeded)
        {
            logger.LogWarning("Waveform decode of audio track {Track} from {Path} failed (exit {Code}): {Error}",
                track, timebase.VideoPath, result.ExitCode, result.ErrorTail);
            return WaveformData.Empty;
        }

        return WaveformSampler.FromPcm(bytes, windowStart);
    }

    /// Steps the bitrate down, then clamps the selection back toward the subtitle span, rather than
    /// uploading something the server would refuse. Fallback to the next codec if it' smissing
    public async Task<CapturedAudio?> CaptureAsync(
        MediaTimebase timebase, AudioCodec codec, double start, double end,
        double subtitleStart, double subtitleEnd, CancellationToken ct)
    {
        if (timebase.AudioTrackIndex is not { } track) return null;
        if (end - start <= 0) return null;

        var current = codec;
        while (true)
        {
            var captured = await CaptureWithCodecAsync(timebase, current, track, start, end, subtitleStart, subtitleEnd, ct);
            if (captured.Audio is not null || !captured.EncoderMissing) return captured.Audio;

            if (current.Next() is not { } next) return null;
            logger.LogInformation("Falling back from {From} to {To} for the audio clip", current.Label, next.Label);
            current = next;
        }
    }

    private async Task<(CapturedAudio? Audio, bool EncoderMissing)> CaptureWithCodecAsync(
        MediaTimebase timebase, AudioCodec codec, int track, double start, double end,
        double subtitleStart, double subtitleEnd, CancellationToken ct)
    {
        IEnumerable<int> bitrates = codec.IsLossless
            ? [settings.MediaAudioBitrateKbps]
            : BitrateLadder.Where(b => b < settings.MediaAudioBitrateKbps)
                           .Prepend(settings.MediaAudioBitrateKbps);

        foreach (var bitrate in bitrates)
        {
            var encoded = await EncodeAsync(timebase, codec, track, start, end, bitrate, ct);
            if (encoded.EncoderMissing) return (null, true);
            if (encoded.Bytes is not { } bytes) return (null, false);

            if (bytes.Length <= settings.MediaAudioMaxBytes)
                return (new CapturedAudio(bytes, codec.ContentType, $"capture.{codec.Extension}", start, end, codec), false);

            logger.LogDebug("{Codec} audio at {Bitrate}k was {Size} KB, over the cap",
                codec.Label, bitrate, bytes.Length / 1024);
        }

        var minStart = Math.Max(start, subtitleStart - settings.MediaAudioPadLeadMs / 1000.0);
        var minEnd = Math.Min(end, subtitleEnd + settings.MediaAudioPadTailMs / 1000.0);
        if (minEnd - minStart <= 0 || (minStart <= start && minEnd >= end))
        {
            logger.LogWarning("Audio clip of {Seconds:0.0}s does not fit the {Cap} KB cap as {Codec}",
                end - start, settings.MediaAudioMaxBytes / 1024, codec.Label);
            return (null, false);
        }

        var floorBitrate = Math.Min(BitrateLadder[^1], settings.MediaAudioBitrateKbps);
        var clamped = await EncodeAsync(timebase, codec, track, minStart, minEnd, floorBitrate, ct);
        if (clamped.EncoderMissing) return (null, true);

        if (clamped.Bytes is { Length: <= MediaLimits.UploadHardLimitBytes } fit)
            return (new CapturedAudio(fit, codec.ContentType, $"capture.{codec.Extension}", minStart, minEnd, codec), false);

        logger.LogWarning("Audio clip clamped to the subtitle span still exceeds the upload limit as {Codec}", codec.Label);
        return (null, false);
    }

    private async Task<(byte[]? Bytes, bool EncoderMissing)> EncodeAsync(
        MediaTimebase timebase, AudioCodec codec, int track, double start, double end,
        int opusBitrateKbps, CancellationToken ct)
    {
        var output = temp.PathFor($"audio-{Guid.NewGuid():N}.{codec.Extension}");

        var args = new List<string>
        {
            "-ss", FfmpegFilters.Seconds(start),
            "-t", FfmpegFilters.Seconds(end - start),
            "-i", timebase.VideoPath,
            "-map", $"0:a:{track}",
            "-vn", "-sn", "-dn",
            "-ac", settings.MediaAudioStereo ? "2" : "1",
            "-ar", OutputSampleRate.ToString(),
            "-c:a", codec.Encoder
        };

        if (!codec.IsLossless)
            args.AddRange(["-b:a", $"{codec.ScaledBitrate(opusBitrateKbps)}k"]);

        args.AddRange(codec.ExtraArgs);
        args.AddRange(["-f", codec.Format, output]);

        var result = await ffmpeg.RunAsync(args, Timeout, ct);

        if (result.Succeeded && File.Exists(output))
            return (await File.ReadAllBytesAsync(output, ct), false);

        if (FfmpegEncoders.IsMissingEncoderError(result.Stderr, codec.Encoder))
        {
            FfmpegEncoders.MarkMissing(codec.Encoder, logger);
            return (null, true);
        }

        logger.LogWarning("{Codec} audio encode failed (exit {Code}): {Error}",
            codec.Label, result.ExitCode, result.ErrorTail);
        return (null, false);
    }
}
