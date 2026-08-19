using System.Globalization;
using System.Text;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

public sealed class OverlayRenderer
{
    private readonly StyleResolver _styleResolver;
    private readonly OsdState _osd;
    private volatile RenderSnapshot _snap;

    /// <param name="Preamble">Drawn with \q2, for text whose breaks SubtitleWrapResolver owns.</param>
    /// <param name="WrappingPreamble">Drawn without it, for text the resolver could not answer for:
    /// suppressing libass there would run a long line off both edges of the screen.</param>
    private sealed record RenderSnapshot(PluginSettings Settings, string Preamble, string WrappingPreamble);

    public OverlayRenderer(PluginSettings settings, StyleResolver styleResolver, OsdState osd)
    {
        _styleResolver = styleResolver;
        _osd = osd;
        _snap = BuildSnapshot(settings, 1280f);
    }

    public const int OverlayResY = 720;
    public const float ResY = OverlayResY;

    public static int ClampAlign(int alignment) => Math.Clamp(alignment, 1, 9);

    public static float ComputeResX(int osdWidth, int osdHeight)
        => osdHeight > 0 ? ResY * osdWidth / osdHeight : 1280f;

    public static string BuildStyleTags(PluginSettings settings)
        => $@"\fs{settings.FontSize}\fn{settings.FontFamily}\bord{settings.BorderSize.ToString(CultureInfo.InvariantCulture)}";

    public static (float X, float Y) ComputePosition(int alignment, int marginX, int marginY, float resX)
    {
        float x = (alignment % 3) switch
        {
            1 => marginX,
            2 => resX / 2,
            0 => resX - marginX,
            _ => resX / 2
        };

        float y = alignment switch
        {
            >= 7 => marginY,
            >= 4 => ResY / 2,
            _ => ResY - marginY
        };

        return (x, y);
    }

    public static string BuildPositionTags(float resX, PluginSettings settings)
        => BuildPositionTags(resX, settings, ClampAlign(settings.SubtitleAlignment));

    public static string BuildPositionTags(float resX, PluginSettings settings, int align)
    {
        var (posX, posY) = ComputePosition(align, settings.SubtitleMarginX, settings.SubtitleMarginY, resX);
        return $@"\pos({posX:F0},{posY:F0})";
    }

    public void RebuildPreamble()
        => _snap = BuildSnapshot(_snap.Settings, ComputeResX(_osd.Width, _osd.Height));

    public void UpdateSettings(PluginSettings newSettings)
        => _snap = BuildSnapshot(newSettings, ComputeResX(_osd.Width, _osd.Height));

    private static RenderSnapshot BuildSnapshot(PluginSettings settings, float resX)
    {
        int align = ClampAlign(settings.SubtitleAlignment);
        var head = $@"\an{align}{BuildPositionTags(resX, settings, align)}";
        var tail = BuildStyleTags(settings);
        // \q2 stops libass from wrapping on its own: SubtitleWrapResolver turns every wrap into
        // an explicit \N beforehand, so the drawn text and the measured hitboxes always agree.
        return new RenderSnapshot(settings, $"{{{head}\\q2{tail}}}", $"{{{head}{tail}}}");
    }

    /// Underlines carries the words whose resolved style asks for a coloured bar, since style
    /// resolution happens here and the bar overlay is written by a later stage.
    public (string Ass, IReadOnlyDictionary<(int WordId, byte ReadingIndex), UnderlineBar>? Underlines) RenderSubtitle(
        string originalText,
        ParseCacheEntry entry,
        HashSet<(int WordId, byte ReadingIndex)>? iPlusOneWords = null,
        HashSet<(int WordId, byte ReadingIndex)>? frequencyWords = null,
        HashSet<(int WordId, byte ReadingIndex)>? revealedWords = null,
        bool suppressWrap = true)
    {
        var snap = _snap;
        var sb = new StringBuilder();
        sb.Append(suppressWrap ? snap.Preamble : snap.WrappingPreamble);

        double border = snap.Settings.BorderSize;
        Dictionary<(int WordId, byte ReadingIndex), UnderlineBar>? underlines = null;

        int lastEnd = 0;
        foreach (var token in entry.Tokens)
        {
            if (token.Start > lastEnd)
            {
                AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed, border);
                AssTagBuilder.AppendEscapedText(sb, originalText, lastEnd, token.Start - lastEnd);
            }

            var style = _styleResolver.Resolve(
                token, entry.VocabStates, iPlusOneWords, frequencyWords, entry.PitchClasses, revealedWords);

            if (style.Underline == true && style.UnderlineColor is { } barColor)
            {
                (underlines ??= [])[(token.WordId, token.ReadingIndex)] = new UnderlineBar(
                    barColor, style.UnderlineThickness ?? UnderlineBarRenderer.DefaultThickness);
            }

            AssTagBuilder.AppendStyle(sb, style, border);
            AssTagBuilder.AppendEscapedText(sb, originalText, token.Start, token.Length);

            lastEnd = token.Start + token.Length;
        }

        if (lastEnd < originalText.Length)
        {
            AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed, border);
            AssTagBuilder.AppendEscapedText(sb, originalText, lastEnd, originalText.Length - lastEnd);
        }

        return (sb.ToString(), underlines);
    }

    public string RenderPlain(string text, bool suppressWrap = true)
    {
        var snap = _snap;
        var sb = new StringBuilder();
        sb.Append(suppressWrap ? snap.Preamble : snap.WrappingPreamble);
        AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed, snap.Settings.BorderSize);
        AssTagBuilder.AppendEscapedText(sb, text, 0, text.Length);
        return sb.ToString();
    }
}
