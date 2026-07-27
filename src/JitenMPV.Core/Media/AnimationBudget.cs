using JitenMPV.Core.Config;

namespace JitenMPV.Core.Media;

public sealed record AnimationPlan(int Fps, double Duration, int Frames, int MaxEdge, int Quality);

public static class AnimationBudget
{
    private const double MinDuration = 0.30;
    private const double MaxDuration = 60.0;
    private const int MaxFps = 30;

    /// Drops fps toward MinFps until the frame count fits, then truncates the tail. Solving for
    /// frames first is what keeps the server's normalization engaged: past 300 frames it stores the
    /// animation unprocessed, losing the size guarantee entirely.
    public static AnimationPlan Solve(double duration, PluginSettings s)
    {
        duration = Math.Clamp(duration, MinDuration, MaxDuration);

        var minFps = Math.Max(1, s.MediaAnimMinFps);
        var maxFrames = Math.Max(1, s.MediaAnimMaxFrames);

        var fps = Math.Max(minFps, Math.Min(s.MediaAnimTargetFps, MaxFps));
        while (fps > minFps && Math.Ceiling(duration * fps) > maxFrames)
            fps--;

        // Still over budget at the floor fps: keep the head of the clip rather than dropping to a slideshow.
        if (Math.Ceiling(duration * fps) > maxFrames)
            duration = maxFrames / (double)fps;

        return new AnimationPlan(fps, duration, (int)Math.Ceiling(duration * fps),
            s.MediaAnimMaxEdge, s.MediaAnimQuality);
    }

    /// The encode ladder: each rung trades resolution and quality for size, and the last also drops
    /// frame rate. Attempt 0 is the plan as solved.
    public static AnimationPlan Step(AnimationPlan plan, int attempt, int minFps) => attempt switch
    {
        0 => plan,
        1 => plan with { Quality = Math.Min(plan.Quality, 70) },
        2 => plan with { MaxEdge = Math.Min(plan.MaxEdge, 768), Quality = Math.Min(plan.Quality, 60) },
        _ => Reframe(plan with { MaxEdge = Math.Min(plan.MaxEdge, 640), Quality = Math.Min(plan.Quality, 50) },
            Math.Max(minFps, (int)Math.Ceiling(plan.Fps * 2 / 3.0)))
    };

    private static AnimationPlan Reframe(AnimationPlan plan, int fps)
        => plan with { Fps = fps, Frames = (int)Math.Ceiling(plan.Duration * fps) };

    /// <summary>
    /// Length of the sample encoded to size a clip. A contiguous second keeps the frame-to-frame
    /// similarity the real encode benefits from, which a spread of individual frames would not:
    /// scaling one second up to four lands within about 5% on moving footage, and only overstates
    /// near-static footage, which is far too small for the error to matter.
    /// </summary>
    public const double ProbeSeconds = 1.0;
}
