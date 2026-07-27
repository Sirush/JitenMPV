using JitenMPV.Core.Config;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

public sealed class AudioCapture(
    FfmpegRunner ffmpeg, MediaTempFiles temp, PluginSettings settings, ILogger logger)
{
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
            logger.LogWarning("Waveform decode failed (exit {Code}): {Error}",
                result.ExitCode, result.ErrorTail);
            return WaveformData.Empty;
        }

        return WaveformSampler.FromPcm(bytes, windowStart);
    }

    /// Steps the bitrate down, then clamps the selection back toward the subtitle span, rather than
    /// uploading something the server would refuse.
    public async Task<CapturedAudio?> CaptureAsync(
        MediaTimebase timebase, double start, double end,
        double subtitleStart, double subtitleEnd, CancellationToken ct)
    {
        if (timebase.AudioTrackIndex is not { } track) return null;
        if (end - start <= 0) return null;

        foreach (var bitrate in BitrateLadder.Where(b => b < settings.MediaAudioBitrateKbps)
                                             .Prepend(settings.MediaAudioBitrateKbps))
        {
            var bytes = await EncodeAsync(timebase, track, start, end, bitrate, ct);
            if (bytes is null) return null;

            if (bytes.Length <= settings.MediaAudioMaxBytes)
                return new CapturedAudio(bytes, "audio/ogg", "capture.ogg", start, end);

            logger.LogDebug("Audio at {Bitrate}k was {Size} KB, over the cap", bitrate, bytes.Length / 1024);
        }

        var minStart = Math.Max(start, subtitleStart - settings.MediaAudioPadLeadMs / 1000.0);
        var minEnd = Math.Min(end, subtitleEnd + settings.MediaAudioPadTailMs / 1000.0);
        if (minEnd - minStart <= 0 || (minStart <= start && minEnd >= end)) return null;

        var floorBitrate = Math.Min(BitrateLadder[^1], settings.MediaAudioBitrateKbps);
        var clamped = await EncodeAsync(timebase, track, minStart, minEnd, floorBitrate, ct);
        return clamped is not null && clamped.Length <= MediaLimits.UploadHardLimitBytes
            ? new CapturedAudio(clamped, "audio/ogg", "capture.ogg", minStart, minEnd)
            : null;
    }

    private async Task<byte[]?> EncodeAsync(
        MediaTimebase timebase, int track, double start, double end, int bitrateKbps, CancellationToken ct)
    {
        var output = temp.PathFor($"audio-{Guid.NewGuid():N}.ogg");

        var result = await ffmpeg.RunAsync(
        [
            "-ss", FfmpegFilters.Seconds(start),
            "-t", FfmpegFilters.Seconds(end - start),
            "-i", timebase.VideoPath,
            "-map", $"0:a:{track}",
            "-vn", "-sn", "-dn",
            "-ac", settings.MediaAudioStereo ? "2" : "1",
            "-ar", "48000",
            "-c:a", "libopus",
            "-b:a", $"{bitrateKbps}k",
            "-vbr", "on",
            "-compression_level", "10",
            // Not "voip": that optimises for speech alone and mangles the music and effects behind
            // the dialogue, which immersion cards are partly there to carry.
            "-application", "audio",
            "-f", "ogg",
            output
        ], Timeout, ct);

        if (result.Succeeded && File.Exists(output))
            return await File.ReadAllBytesAsync(output, ct);

        logger.LogWarning("Audio encode failed (exit {Code}): {Error}", result.ExitCode, result.ErrorTail);
        return null;
    }
}
