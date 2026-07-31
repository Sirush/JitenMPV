using JitenMPV.Core.Mpv;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

public sealed record MpvCaptureProps(
    string VideoPath,
    string? ExternalSubtitlePath,
    int? AudioTrackIndex,
    int? SubtitleTrackIndex,
    double TimePos,
    double? SubStart,
    double? SubEnd,
    double SubDelay,
    double SubSpeed,
    double AudioDelay,
    double DemuxerStartTime,
    double Duration,
    bool IsSeekableFile)
{
    public MediaTimebase ToTimebase() => new(
        VideoPath, AudioTrackIndex, SubtitleTrackIndex, ExternalSubtitlePath,
        SubDelay, SubSpeed, AudioDelay, DemuxerStartTime, Duration, IsSeekableFile);
}

/// Snapshots every mpv property the capture depends on in one pass, so the whole capture is bound
/// to one moment even though playback may continue.
public static class MpvCaptureProbe
{
    public static async Task<MpvCaptureProps?> ReadAsync(
        MpvIpcClient ipc, ILogger logger, CancellationToken ct)
    {
        try
        {
            var path = await ipc.GetPropertyAsync<string>("path", ct);
            if (string.IsNullOrEmpty(path)) return null;

            if (!Path.IsPathRooted(path) && !IsStream(path))
            {
                var workDir = await ipc.GetPropertyAsync<string>("working-directory", ct);
                if (!string.IsNullOrEmpty(workDir))
                    path = Path.Combine(workDir, path);
            }

            var timePos = await ipc.GetPropertyAsync<double?>("time-pos", ct) ?? 0;
            var subStart = await ipc.GetPropertyAsync<double?>("sub-start", ct);
            var subEnd = await ipc.GetPropertyAsync<double?>("sub-end", ct);
            var subDelay = await ipc.GetPropertyAsync<double?>("sub-delay", ct) ?? 0;
            var subSpeed = await ipc.GetPropertyAsync<double?>("sub-speed", ct) ?? 1;
            if (subSpeed <= 0) subSpeed = 1;
            var audioDelay = await ipc.GetPropertyAsync<double?>("audio-delay", ct) ?? 0;
            var demuxerStart = await ipc.GetPropertyAsync<double?>("demuxer-start-time", ct) ?? 0;
            var duration = await ipc.GetPropertyAsync<double?>("duration", ct) ?? 0;
            var externalSub = await ipc.GetPropertyAsync<string>(
                "current-tracks/sub/external-filename", ct);

            var audioIndex = await TrackIndexResolver.FindAsync(ipc, "audio", ct);
            var subIndex = await TrackIndexResolver.FindAsync(ipc, "sub", ct);

            // ffmpeg seeks are unreliable on a stream, so animation and audio stay off for it while
            // the mpv screenshot keeps working.
            var seekable = !IsStream(path) && File.Exists(path);

            return new MpvCaptureProps(
                path, string.IsNullOrEmpty(externalSub) ? null : externalSub,
                audioIndex, subIndex, timePos, subStart, subEnd,
                subDelay, subSpeed, audioDelay, demuxerStart, duration, seekable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read mpv capture properties");
            return null;
        }
    }

    private static bool IsStream(string path)
        => Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile;
}
