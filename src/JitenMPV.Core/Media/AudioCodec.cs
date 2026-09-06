using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

/// <param name="BitrateScale">Multiplier on the configured Opus bitrate that gives this codec a
/// comparable quality; zero for a lossless codec whose size does not follow a bitrate.</param>
public sealed record AudioCodec(
    string Label,
    string Encoder,
    string Format,
    string Extension,
    string ContentType,
    double BitrateScale,
    IReadOnlyList<string> ExtraArgs)
{
    public bool IsLossless => BitrateScale <= 0;

    public int ScaledBitrate(int opusKbps) => Math.Max(8, (int)Math.Round(opusKbps * BitrateScale));

    /// Bytes per second at the given Opus-equivalent bitrate, for the size readout before encoding.
    public double BytesPerSecond(int opusKbps, bool stereo)
        => IsLossless
            ? AudioCapture.OutputSampleRate * 2 * (stereo ? 2 : 1)
            : ScaledBitrate(opusKbps) * 1000 / 8.0;

    public static readonly AudioCodec Opus = new(
                                                 "Opus", "libopus", "ogg", "ogg", "audio/ogg", 1.0,
                                                 ["-vbr", "on", "-compression_level", "10", "-application", "audio"]);

    public static readonly AudioCodec Aac = new(
                                                "AAC", "aac", "mp4", "m4a", "audio/mp4", 1.5, ["-movflags", "+faststart"]);

    public static readonly AudioCodec Wav = new(
                                                "WAV", "pcm_s16le", "wav", "wav", "audio/wav", 0, []);

    /// Preference order
    public static readonly IReadOnlyList<AudioCodec> Ladder = [Opus, Aac, Wav];

    public static async Task<AudioCodec> SelectAsync(FfmpegRunner ffmpeg, ILogger logger, CancellationToken ct)
    {
        var encoders = await FfmpegEncoders.ListAsync(ffmpeg, logger, ct);
        foreach (var codec in Ladder)
        {
            if (FfmpegEncoders.IsLatchedMissing(codec.Encoder)) continue;
            if (encoders is null || encoders.Contains(codec.Encoder)) return codec;
        }

        return Wav;
    }

    public AudioCodec? Next()
    {
        for (var i = 0; i < Ladder.Count - 1; i++)
            if (Ladder[i].Label == Label)
                return Ladder[i + 1];
        return null;
    }
}

/// The encoders one ffmpeg build offers, read once per binary. Encoders that fail at run time
/// despite being listed are latched off for the rest of the session.
public static class FfmpegEncoders
{
    private static readonly SemaphoreSlim ProbeLock = new(1, 1);
    private static readonly HashSet<string> LatchedMissing = [];
    private static readonly Lock LatchLock = new();
    private static string? _probedPath;
    private static HashSet<string>? _encoders;

    public static async Task<IReadOnlySet<string>?> ListAsync(FfmpegRunner ffmpeg, ILogger logger, CancellationToken ct)
    {
        if (_probedPath == ffmpeg.ExecutablePath) return _encoders;

        await ProbeLock.WaitAsync(ct);
        try
        {
            if (_probedPath == ffmpeg.ExecutablePath) return _encoders;

            var (result, bytes) = await ffmpeg.RunCaptureStdoutAsync(
                                                                     ["-encoders"], TimeSpan.FromSeconds(10), ct);

            if (!result.Succeeded)
            {
                logger.LogWarning("Could not list ffmpeg encoders (exit {Code}): {Error}",
                                  result.ExitCode, result.ErrorTail);
                _encoders = null;
            }
            else
            {
                _encoders = Parse(System.Text.Encoding.UTF8.GetString(bytes));
                var audio = AudioCodec.Ladder.Select(c => $"{c.Encoder}={(_encoders.Contains(c.Encoder) ? "yes" : "no")}");
                logger.LogInformation("ffmpeg audio encoders: {Encoders}", string.Join(", ", audio));
            }

            _probedPath = ffmpeg.ExecutablePath;
            return _encoders;
        }
        finally
        {
            ProbeLock.Release();
        }
    }

    internal static HashSet<string> Parse(string output)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pastHeader = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!pastHeader)
            {
                pastHeader = line.TrimStart().StartsWith("------");
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 6)
                names.Add(parts[1]);
        }

        return names;
    }

    public static bool IsLatchedMissing(string encoder)
    {
        lock (LatchLock) return LatchedMissing.Contains(encoder);
    }

    public static void MarkMissing(string encoder, ILogger logger)
    {
        lock (LatchLock)
        {
            if (!LatchedMissing.Add(encoder)) return;
        }

        logger.LogWarning("ffmpeg cannot encode with {Encoder}; disabled for this session", encoder);
    }

    public static bool IsMissingEncoderError(string stderr, string encoder)
        => stderr.Contains($"Unknown encoder '{encoder}'", StringComparison.OrdinalIgnoreCase)
           || stderr.Contains("Encoder (codec", StringComparison.OrdinalIgnoreCase)
           && stderr.Contains("not found", StringComparison.OrdinalIgnoreCase)
           || stderr.Contains("Automatic encoder selection failed", StringComparison.OrdinalIgnoreCase);
}