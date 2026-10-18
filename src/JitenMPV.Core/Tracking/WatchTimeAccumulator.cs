namespace JitenMPV.Core.Tracking;

/// Counts the seconds that actually played: a jump larger than the elapsed wall time allows is a
/// seek and adds nothing, so seeking to the end never counts as watching.
public sealed class WatchTimeAccumulator
{
    private double? _lastPosition;
    private double _lastWall;

    public double Watched { get; private set; }

    public void Reset()
    {
        _lastPosition = null;
        Watched = 0;
    }

    public void Sample(double position, double wallSeconds, double speed)
    {
        if (_lastPosition is { } last)
        {
            var delta = position - last;
            var allowed = (wallSeconds - _lastWall) * Math.Max(speed, 0.01) * 1.5 + 0.5;
            if (delta > 0 && delta <= allowed)
                Watched += delta;
        }
        _lastPosition = position;
        _lastWall = wallSeconds;
    }
}
