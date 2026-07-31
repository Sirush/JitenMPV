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
    double SubSpeed,
    double AudioDelay,
    double DemuxerStartTime,
    double Duration,
    bool IsSeekableFile)
{
    /// <summary>
    /// Input time for a subtitle boundary. Every subtitle timestamp is the subtitle's own -
    /// mpv's sub-start/sub-end report the file's timings unretimed, exactly as the pre-parsed cue
    /// list holds them - so both need the shift mpv applies when it displays them.
    /// </summary>
    public double SubtitleToVideoTime(double subTime) => subTime * SubSpeed + SubDelay;

    /// The subtitle timestamp mpv is displaying at a playback moment, for finding the line being
    /// watched in a cue list that is stored on the subtitle's own clock.
    public double VideoToSubtitleTime(double videoTime) => (videoTime - SubDelay) / SubSpeed;

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
