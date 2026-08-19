using JitenMPV.Core.Cache;
using JitenMPV.Core.Pitch;

namespace JitenMPV.Core.Rendering;

/// Collects the coloured bars a word carries, whichever renderer ends up drawing them.
public static class UnderlineBarMerger
{
    /// The theme bar comes first so it sits nearest the text, leaving pitch stacked beneath it.
    public static IReadOnlyDictionary<(int WordId, byte ReadingIndex), IReadOnlyList<UnderlineBar>>? Build(
        ParseCacheEntry? entry,
        IReadOnlyDictionary<(int WordId, byte ReadingIndex), UnderlineBar>? themeUnderlines,
        IReadOnlyDictionary<PitchClass, string> pitchColors,
        double pitchThickness)
    {
        Dictionary<(int WordId, byte ReadingIndex), IReadOnlyList<UnderlineBar>>? bars = null;

        if (themeUnderlines is not null)
            foreach (var (key, bar) in themeUnderlines)
                (bars ??= [])[key] = new List<UnderlineBar> { bar };

        if (pitchColors.Count > 0 && entry is not null)
        {
            foreach (var (key, pitchClass) in entry.PitchClasses)
            {
                if (!pitchColors.TryGetValue(pitchClass, out var pitchColor)) continue;

                var pitchBar = new UnderlineBar(pitchColor, pitchThickness);
                bars ??= [];
                bars[key] = bars.TryGetValue(key, out var existing)
                    ? [.. existing, pitchBar]
                    : new List<UnderlineBar> { pitchBar };
            }
        }

        return bars;
    }

    public static IEnumerable<(WordRect Rect, IReadOnlyList<UnderlineBar> Bars)> ForLayout(
        IReadOnlyList<WordRect> layout,
        IReadOnlyDictionary<(int WordId, byte ReadingIndex), IReadOnlyList<UnderlineBar>>? bars)
    {
        if (bars is null) yield break;

        foreach (var rect in layout)
            if (bars.TryGetValue((rect.WordId, rect.ReadingIndex), out var list))
                yield return (rect, list);
    }
}
