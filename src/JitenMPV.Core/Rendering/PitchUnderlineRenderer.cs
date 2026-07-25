using System.Globalization;
using System.Text;
using JitenMPV.Core.Pitch;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

/// Draws pitch bars as filled ASS shapes on their own overlay, because ASS renders `\u` underlines
/// in the primary colour and so cannot colour one independently of the text.
public static class PitchUnderlineRenderer
{
    /// Keeps the bar clear of the glyph descenders that the measured line height includes.
    private const double BaselineInset = 0.12;

    public static string Render(
        IReadOnlyList<WordRect> layout,
        IReadOnlyDictionary<(int WordId, byte ReadingIndex), PitchClass> pitchClasses,
        IReadOnlyDictionary<PitchClass, string> colors,
        double thickness)
    {
        if (layout.Count == 0 || pitchClasses.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var rect in layout)
        {
            if (!pitchClasses.TryGetValue((rect.WordId, rect.ReadingIndex), out var pitchClass)) continue;
            if (!colors.TryGetValue(pitchClass, out var color)) continue;
            if (rect.Width <= 0) continue;

            var y = rect.Y + rect.Height - rect.Height * BaselineInset - thickness;

            // One event per bar: each drawing then starts at its own origin, so \pos places it
            // exactly rather than depending on where the shape's bounding box lands.
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(CultureInfo.InvariantCulture,
                $$"""{\an7\pos({{rect.X:F1}},{{y:F1}})\bord0\shad0\1a&H00&\1c&H{{WordStyleState.ToAssBgr(color)}}&\p1}m 0 0 l {{rect.Width:F1}} 0 l {{rect.Width:F1}} {{thickness:F1}} l 0 {{thickness:F1}}{\p0}""");
        }

        return sb.ToString();
    }
}
