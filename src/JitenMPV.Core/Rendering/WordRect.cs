namespace JitenMPV.Core.Rendering;

public sealed record WordRect(
    int TokenIndex, int WordId, byte ReadingIndex,
    float X, float Y, float Width, float Height)
{
    /// Slack around the text block that still counts as pointing at a word, in overlay units.
    /// Applied only where it cannot cross into a neighbouring word or line: between words the
    /// boundary is the midpoint of whatever gap exists, so regions never overlap and an edge
    /// click resolves to the word whose glyphs it is actually on.
    public const float HitPadX = 12f;
    public const float HitPadTop = 10f;

    /// Kept small so the bottom line's region cannot reach the OSC seek bar below the subtitle.
    public const float HitPadBottom = 4f;

    /// Assigned by AssignHitRegions; defaults to the glyph box so an unprocessed rect is still hittable.
    public float HitX0 { get; init; } = X;
    public float HitY0 { get; init; } = Y;
    public float HitX1 { get; init; } = X + Width;
    public float HitY1 { get; init; } = Y + Height;

    public (float X0, float Y0, float X1, float Y1) HitRegion => (HitX0, HitY0, HitX1, HitY1);

    public float Right => X + Width;
    public float Bottom => Y + Height;

    public static List<WordRect> AssignHitRegions(List<WordRect> rects)
    {
        if (rects.Count == 0) return rects;

        // Rects on one line share the exact same Y, assigned from a single per-line value.
        var lines = rects
            .GroupBy(r => r.Y)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(r => r.X).ToList())
            .ToList();

        var result = new List<WordRect>(rects.Count);
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            float lineTop = line.Min(r => r.Y);
            float lineBottom = line.Max(r => r.Bottom);

            float y0 = lineTop - HitPadTop;
            if (li > 0)
            {
                float prevBottom = lines[li - 1].Max(r => r.Bottom);
                y0 = Math.Max(y0, (prevBottom + lineTop) / 2f);
            }

            float y1 = lineBottom + HitPadBottom;
            if (li < lines.Count - 1)
            {
                float nextTop = lines[li + 1].Min(r => r.Y);
                y1 = Math.Min(y1, (lineBottom + nextTop) / 2f);
            }

            for (int i = 0; i < line.Count; i++)
            {
                var rect = line[i];

                float x0 = rect.X - HitPadX;
                if (i > 0)
                    x0 = Math.Max(x0, (line[i - 1].Right + rect.X) / 2f);

                float x1 = rect.Right + HitPadX;
                if (i < line.Count - 1)
                    x1 = Math.Min(x1, (rect.Right + line[i + 1].X) / 2f);

                result.Add(rect with { HitX0 = x0, HitY0 = y0, HitX1 = x1, HitY1 = y1 });
            }
        }

        return result;
    }
}
