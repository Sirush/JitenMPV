namespace JitenMPV.Core.Subtitles;

/// The cue list pre-parsing already builds, kept instead of discarded so a mined line can offer its
/// neighbours as sentence context.
/// A stepped-to cue together with the position to seek to for it.
public readonly record struct SubtitleStep(SubtitleCue Cue, TimeSpan SeekTime);

public sealed class SubtitleTimeline
{
    private static readonly TimeSpan SeekInset = TimeSpan.FromMilliseconds(50);

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

    /// The cue <paramref name="delta"/> steps away from the one playing at <paramref name="from"/>,
    /// both in subtitle-file time. Unlike mpv's sub-seek this reaches cues the demuxer has not read
    /// yet; null means the step ran off either end of the file.
    ///
    /// Steps move between distinct start times rather than array positions. Subtitle files routinely
    /// carry several cues on one timestamp — two speakers, or dialogue over a sign — and treating
    /// those as separate stops strands a backward step on a cue that starts where it already is.
    public SubtitleStep? Step(TimeSpan from, int delta)
    {
        if (delta == 0) return null;

        lock (_lock)
        {
            if (_cues.Length == 0) return null;

            int index;
            if (delta > 0)
            {
                index = FirstStartingAfter(_cues, from);
                for (var i = 1; i < delta && index >= 0; i++)
                    index = FirstStartingAfter(_cues, _cues[index].Start);
            }
            else
            {
                index = IndexAtOrBefore(_cues, from);
                if (index < 0) return null;

                // In the gap after a cue the first step back is the line just left; from inside one
                // it is the line before it.
                var remaining = from > _cues[index].End ? delta + 1 : delta;
                for (var i = 0; i > remaining && index >= 0; i--)
                    index = LastStartingBefore(_cues, _cues[index].Start);
            }

            return index >= 0 ? new SubtitleStep(_cues[index], SeekTimeFor(index)) : null;
        }
    }

    /// Aims just inside the cue rather than at its boundary, where rounding can still resolve to the
    /// line before, but never as far as the next line begins: overshooting into it would make the
    /// following backward step land right back where it started.
    private TimeSpan SeekTimeFor(int index)
    {
        var cue = _cues[index];
        var limit = cue.End;

        var next = FirstStartingAfter(_cues, cue.Start);
        if (next >= 0 && _cues[next].Start < limit)
            limit = _cues[next].Start;

        var room = Math.Max(0, (limit - cue.Start).Ticks / 2);
        return cue.Start + TimeSpan.FromTicks(Math.Min(SeekInset.Ticks, room));
    }

    private static int IndexNear(SubtitleCue[] cues, TimeSpan t)
    {
        if (cues.Length == 0) return -1;

        var best = IndexAtOrBefore(cues, t);
        if (best < 0) return 0;
        if (t <= cues[best].End) return best;

        // t sits in a gap: pick whichever neighbour is closer.
        var next = best + 1;
        if (next >= cues.Length) return best;
        return (t - cues[best].End) <= (cues[next].Start - t) ? best : next;
    }

    /// First cue starting strictly after <paramref name="t"/>, or -1 when none does.
    private static int FirstStartingAfter(SubtitleCue[] cues, TimeSpan t)
    {
        var lo = 0;
        var hi = cues.Length - 1;
        var best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (cues[mid].Start > t)
            {
                best = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return best;
    }

    /// Last cue starting strictly before <paramref name="t"/>, or -1 when none does. Skipping equal
    /// starts is what makes a backward step clear a whole timestamp rather than one cue of it.
    private static int LastStartingBefore(SubtitleCue[] cues, TimeSpan t)
    {
        var lo = 0;
        var hi = cues.Length - 1;
        var best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (cues[mid].Start < t)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best;
    }

    /// Last cue starting at or before <paramref name="t"/>, or -1 when t precedes every cue. That
    /// cue either contains t or is the one just before a gap, which is still the right anchor for
    /// "the line being watched".
    private static int IndexAtOrBefore(SubtitleCue[] cues, TimeSpan t)
    {
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

        return best;
    }
}
