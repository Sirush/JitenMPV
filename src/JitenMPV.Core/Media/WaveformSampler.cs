using JitenMPV.Core.Config;

namespace JitenMPV.Core.Media;

public sealed record WaveformData(
    double WindowStart,
    double WindowDuration,
    int SampleRate,
    float[] Peaks,
    short[] Pcm)
{
    public double WindowEnd => WindowStart + WindowDuration;

    public static WaveformData Empty { get; } = new(0, 0, WaveformSampler.SampleRate, [], []);
    public bool IsEmpty => Pcm.Length == 0;
}

public static class WaveformSampler
{
    /// 24 kHz mono is enough for both the display and the in-app preview, and keeps one decode
    /// serving all three consumers.
    public const int SampleRate = 24_000;

    public const int DefaultBuckets = 900;

    /// Matches WaveformControl's own floor, so a trim can never produce a selection the popup's
    /// handles would immediately widen.
    private const double MinSelectionSeconds = 0.20;

    private const double FrameSeconds = 0.020;
    private const double OutwardCapSeconds = 0.75;
    private const double InwardCapSeconds = 0.50;
    private const double FloorPercentile = 0.20;
    private const double FloorMultiplier = 3.0;
    private const double PeakFraction = 0.06;

    public static WaveformData FromPcm(byte[] s16le, double windowStart, int buckets = DefaultBuckets)
    {
        var sampleCount = s16le.Length / 2;
        if (sampleCount == 0) return WaveformData.Empty;

        var pcm = new short[sampleCount];
        Buffer.BlockCopy(s16le, 0, pcm, 0, sampleCount * 2);

        buckets = Math.Clamp(buckets, 1, sampleCount);
        var peaks = new float[buckets];
        var perBucket = (double)sampleCount / buckets;

        for (var b = 0; b < buckets; b++)
        {
            var from = (int)(b * perBucket);
            var to = Math.Min(sampleCount, (int)((b + 1) * perBucket));
            var peak = 0;
            for (var i = from; i < to; i++)
            {
                var magnitude = Math.Abs((int)pcm[i]);
                if (magnitude > peak) peak = magnitude;
            }
            peaks[b] = peak / (float)short.MaxValue;
        }

        return new WaveformData(windowStart, sampleCount / (double)SampleRate, SampleRate, peaks, pcm);
    }

    /// Expands outward while speech continues and contracts past leading/trailing silence, then
    /// applies the fixed pads. Both walks are capped so a noisy track cannot widen the clip freely.
    public static (double Start, double End) AutoTrim(
        WaveformData wave, double subStart, double subEnd, PluginSettings settings)
    {
        var lead = settings.MediaAudioPadLeadMs / 1000.0;
        var tail = settings.MediaAudioPadTailMs / 1000.0;

        if (!settings.MediaAudioAutoTrim || wave.IsEmpty)
            return Clamp(wave, subStart - lead, subEnd + tail);

        var frameSize = Math.Max(1, (int)(wave.SampleRate * FrameSeconds));
        var rms = ComputeRms(wave.Pcm, frameSize);
        if (rms.Length == 0)
            return Clamp(wave, subStart - lead, subEnd + tail);

        var threshold = Threshold(rms);

        var startFrame = ToFrame(wave, subStart, frameSize, rms.Length);
        var endFrame = ToFrame(wave, subEnd, frameSize, rms.Length);

        var start = WalkStart(rms, threshold, startFrame, frameSize, wave);
        var end = WalkEnd(rms, threshold, endFrame, frameSize, wave);

        // A contradictory walk (silence everywhere, or a cue shorter than one frame) collapses the
        // range; the untrimmed cue is the safer answer.
        if (end - start < FrameSeconds)
            return Clamp(wave, subStart - lead, subEnd + tail);

        return Clamp(wave, start - lead, end + tail);
    }

    private static double WalkStart(
        float[] rms, double threshold, int frame, int frameSize, WaveformData wave)
    {
        // A word that begins before its cue: walk back while sound continues.
        var outwardCap = (int)(OutwardCapSeconds / FrameSeconds);
        var i = frame;
        var moved = 0;
        while (i > 0 && moved < outwardCap && rms[Math.Min(i, rms.Length - 1)] > threshold)
        {
            i--;
            moved++;
        }

        if (moved > 0) return FromFrame(wave, i, frameSize);

        // A cue that appears early: walk forward past the silence in front of it.
        var inwardCap = (int)(InwardCapSeconds / FrameSeconds);
        i = frame;
        moved = 0;
        while (i < rms.Length - 1 && moved < inwardCap && rms[i] <= threshold)
        {
            i++;
            moved++;
        }
        return FromFrame(wave, i, frameSize);
    }

    private static double WalkEnd(
        float[] rms, double threshold, int frame, int frameSize, WaveformData wave)
    {
        var outwardCap = (int)(OutwardCapSeconds / FrameSeconds);
        var i = frame;
        var moved = 0;
        while (i < rms.Length - 1 && moved < outwardCap && rms[i] > threshold)
        {
            i++;
            moved++;
        }

        if (moved > 0) return FromFrame(wave, i, frameSize);

        var inwardCap = (int)(InwardCapSeconds / FrameSeconds);
        i = frame;
        moved = 0;
        while (i > 0 && moved < inwardCap && rms[Math.Min(i, rms.Length - 1)] <= threshold)
        {
            i--;
            moved++;
        }
        return FromFrame(wave, i, frameSize);
    }

    private static double Threshold(float[] rms)
    {
        var sorted = (float[])rms.Clone();
        Array.Sort(sorted);
        var floor = sorted[(int)(sorted.Length * FloorPercentile)];
        var peak = sorted[^1];
        return Math.Max(floor * FloorMultiplier, peak * PeakFraction);
    }

    private static float[] ComputeRms(short[] pcm, int frameSize)
    {
        var frames = pcm.Length / frameSize;
        if (frames == 0) return [];

        var rms = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            var from = f * frameSize;
            for (var i = from; i < from + frameSize; i++)
            {
                var sample = pcm[i] / (double)short.MaxValue;
                sum += sample * sample;
            }
            rms[f] = (float)Math.Sqrt(sum / frameSize);
        }
        return rms;
    }

    private static int ToFrame(WaveformData wave, double time, int frameSize, int frameCount)
    {
        var sample = (time - wave.WindowStart) * wave.SampleRate;
        return Math.Clamp((int)(sample / frameSize), 0, Math.Max(0, frameCount - 1));
    }

    private static double FromFrame(WaveformData wave, int frame, int frameSize)
        => wave.WindowStart + frame * frameSize / (double)wave.SampleRate;

    private static (double, double) Clamp(WaveformData wave, double start, double end)
    {
        if (wave.IsEmpty) return (Math.Max(0, start), Math.Max(start, end));

        var minWidth = Math.Min(MinSelectionSeconds, wave.WindowDuration);
        var lo = Math.Clamp(start, wave.WindowStart, wave.WindowEnd - minWidth);
        var hi = Math.Clamp(end, lo + minWidth, wave.WindowEnd);
        return (lo, hi);
    }
}
