using System.Globalization;
using System.Text;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

/// Paints the region HitTestService accepts for each word, in a colour per word; regions are
/// clamped to their neighbours, so any translucent fills blending indicates a layout bug.
public static class HitboxDebugRenderer
{
    /// ASS alpha, where 00 is opaque: light enough that three stacked fills stay readable.
    private const int FillAlpha = 0xB4;
    private const int EdgeAlpha = 0x30;
    private const double EdgeWidth = 1.5;

    /// Successive hues a golden angle apart, so words next to each other never land on near colours.
    private const double HueStep = 137.508;

    public static string Render(IReadOnlyList<WordRect> layout)
    {
        if (layout.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < layout.Count; i++)
        {
            var rect = layout[i];
            var color = WordStyleState.ToAssBgr(HueToRgbHex(i * HueStep % 360.0));
            var (x0, y0, x1, y1) = rect.HitRegion;

            AppendBox(sb, x0, y0, x1 - x0, y1 - y0, color, FillAlpha, EdgeAlpha, EdgeWidth);
            AppendBox(sb, rect.X, rect.Y, rect.Width, rect.Height, color, 0xFF, EdgeAlpha, EdgeWidth);
        }

        return sb.ToString();
    }

    private static void AppendBox(
        StringBuilder sb, double x, double y, double width, double height,
        string colorBgr, int fillAlpha, int edgeAlpha, double edgeWidth)
    {
        if (width <= 0 || height <= 0) return;

        if (sb.Length > 0) sb.Append('\n');
        sb.Append(CultureInfo.InvariantCulture,
            $$"""{\an7\pos({{x:F1}},{{y:F1}})\bord{{edgeWidth:F1}}\shad0\1a&H{{fillAlpha:X2}}&\1c&H{{colorBgr}}&\3a&H{{edgeAlpha:X2}}&\3c&H{{colorBgr}}&\p1}m 0 0 l {{width:F1}} 0 l {{width:F1}} {{height:F1}} l 0 {{height:F1}}{\p0}""");
    }

    private static string HueToRgbHex(double hue)
    {
        const double saturation = 0.9;
        double sector = hue / 60.0;
        double secondary = 1 - saturation * Math.Abs(sector % 2 - 1);
        double lowest = 1 - saturation;

        var (r, g, b) = (int)sector switch
        {
            0 => (1.0, secondary, lowest),
            1 => (secondary, 1.0, lowest),
            2 => (lowest, 1.0, secondary),
            3 => (lowest, secondary, 1.0),
            4 => (secondary, lowest, 1.0),
            _ => (1.0, lowest, secondary)
        };

        return $"{(int)(r * 255):x2}{(int)(g * 255):x2}{(int)(b * 255):x2}";
    }
}
