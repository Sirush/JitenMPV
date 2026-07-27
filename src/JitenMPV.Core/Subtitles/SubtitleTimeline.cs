namespace JitenMPV.Core.Subtitles;

/// The cue list pre-parsing already builds, kept instead of discarded so a mined line can offer its
/// neighbours as sentence context.
public sealed class SubtitleTimeline
{
    private readonly Lock _lock = new();
    private SubtitleCue[] _cues = [];

    public bool IsLoaded
    {
        get { lock (_lock) return _cues.Length > 0; }
    }

    public void Load(IReadOnlyList<SubtitleCue> cues)
    {
        var sorted = cues.Where(c => !string.IsNullOrWhiteSpace(c.Text))
                         .OrderBy(c => c.Start)
                         .ToArray();
        lock (_lock)
            _cues = sorted;
    }

    public void Clear()
    {
        lock (_lock)
            _cues = [];
    }

    public SubtitleCue? At(TimeSpan t)
    {
        lock (_lock)
        {
            var index = IndexNear(_cues, t);
            return index >= 0 ? _cues[index] : null;
        }
    }

    /// The cue containing (or nearest to) <paramref name="t"/>, plus up to <paramref name="radius"/>
    /// cues on each side, in playback order.
    public IReadOnlyList<SubtitleCue> Around(TimeSpan t, int radius)
    {
        lock (_lock)
        {
            var index = IndexNear(_cues, t);
            if (index < 0) return [];

            var start = Math.Max(0, index - radius);
            var end = Math.Min(_cues.Length - 1, index + radius);
            return _cues[start..(end + 1)];
        }
    }

    /// The position of the current cue inside the window <see cref="Around"/> returns for the same
    /// arguments, so a caller can highlight it without re-searching.
    public int IndexInWindow(TimeSpan t, int radius)
    {
        lock (_lock)
        {
            var index = IndexNear(_cues, t);
            return index < 0 ? -1 : index - Math.Max(0, index - radius);
        }
    }

    private static int IndexNear(SubtitleCue[] cues, TimeSpan t)
    {
        if (cues.Length == 0) return -1;

        // Last cue whose start is at or before t; that cue either contains t or is the one just
        // before a gap, which is still the right anchor for "the line being watched".
        var lo = 0;
        var hi = cues.Length - 1;
        var best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (cues[mid].Start <= t)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (best < 0) return 0;
        if (t <= cues[best].End) return best;

        // t sits in a gap: pick whichever neighbour is closer.
        var next = best + 1;
        if (next >= cues.Length) return best;
        return (t - cues[best].End) <= (cues[next].Start - t) ? best : next;
    }
}
