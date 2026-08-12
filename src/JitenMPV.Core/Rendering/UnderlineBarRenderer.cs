using System.Globalization;
using System.Text;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

public readonly record struct UnderlineBar(string Color, double Thickness);

/// Draws underlines as filled ASS shapes on their own overlay, because ASS renders `\u` underlines
/// in the primary colour and so cannot colour one independently of the text.
public static class UnderlineBarRenderer
{
    /// Consecutive lines leave only a few units between their glyphs, so a bar much thicker than
    /// this fills the gap end to end and reads as touching the text above it.
    public const double DefaultThickness = 2;

    /// Separates stacked bars.
    private const double BarGap = 1;

    /// Clearance below the text, as a share of the line pitch so it holds at any font size.
    private const double TextGapRatio = 0.05;

    /// <param name="lineSlot">Distance to the next line's top. WordRect.Height cannot stand in for
    /// it: mpv reports the rendered line box, which runs about half a line taller than the glyphs
    /// and would put every bar inside the following line. Null for a single line, which has no line
    /// below to measure against or collide with.</param>
    /// Bars stack downwards from the start of the next line's slot. Glyphs do not begin at a slot's
    /// top edge, so that leading strip is the only space a bar can occupy without landing on the
    /// descenders of its own line, which fill their slot entirely on lines carrying brackets.
    public static string Render(
        IEnumerable<(WordRect Rect, IReadOnlyList<UnderlineBar> Bars)> words,
        double? lineSlot = null)
    {
        var sb = new StringBuilder();

        foreach (var (rect, bars) in words)
        {
            if (rect.Width <= 0) continue;

            var slot = lineSlot ?? rect.Height;
            var top = rect.Y + slot + slot * TextGapRatio;

            foreach (var bar in bars)
            {
                if (bar.Thickness <= 0) continue;

                // One event per bar: each drawing then starts at its own origin, so \pos places it
                // exactly rather than depending on where the shape's bounding box lands.
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(CultureInfo.InvariantCulture,
                    $$"""{\an7\pos({{rect.X:F1}},{{top:F1}})\bord0\shad0\1a&H00&\1c&H{{WordStyleState.ToAssBgr(bar.Color)}}&\p1}m 0 0 l {{rect.Width:F1}} 0 l {{rect.Width:F1}} {{bar.Thickness:F1}} l 0 {{bar.Thickness:F1}}{\p0}""");

                top += bar.Thickness + BarGap;
            }
        }

        return sb.ToString();
    }
}
