namespace JitenMPV.Core.Media;

/// Everything ffmpeg does is expressed in input time - seconds from the start of the file, which is
/// what <c>-ss</c> and <c>-t</c> take. Everything mpv reports is playback time. This owns the mapping
/// so no other component has to reason about the delays.
public sealed record MediaTimebase(
    string VideoPath,
    int? AudioTrackIndex,
    int? SubtitleTrackIndex,
    string? ExternalSubtitlePath,
    double SubDelay,
    double AudioDelay,
    double DemuxerStartTime,
    double Duration,
    bool IsSeekableFile)
{
    /// <summary>
    /// True when the subtitle range came from the parsed subtitle file rather than from mpv's
    /// sub-start/sub-end. File timestamps predate --sub-delay, so that shift still has to be applied.
    /// </summary>
    public bool RangeIsFileTime { get; init; }

    /// Input time for a subtitle boundary. mpv reports sub-start/sub-end on the display timeline
    /// with sub-delay already folded in; a cue read from the subtitle file has not been shifted yet.
    public double SubtitleToVideoTime(double subTime)
        => RangeIsFileTime ? subTime + SubDelay : subTime;

    /// Input time on the audio stream. --audio-delay shifts audio playback later, so the sample
    /// heard at a given display moment sits that much earlier in the file.
    public double SubtitleToAudioTime(double subTime)
        => SubtitleToVideoTime(subTime) - AudioDelay;

    /// time-pos is already seconds from the start of the file.
    public double PlaybackToVideoTime(double playbackTime) => playbackTime;

    /// <c>-copyts</c> keeps the container's original timestamps, so <c>-to</c> is absolute and must
    /// carry the start offset a non-zero-based container (MPEG-TS) reports.
    public double ToAbsolute(double inputTime) => inputTime + DemuxerStartTime;

    public double Clamp(double inputTime)
        => Duration > 0 ? Math.Clamp(inputTime, 0, Duration) : Math.Max(0, inputTime);
}
