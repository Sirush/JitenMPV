using System.Globalization;
using System.Text;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

public sealed class OverlayRenderer
{
    private static readonly WordStyleState UnparsedBlurReset = StyleResolver.BlurOffStyle.MergeOver(ThemePresets.Unparsed);

    private readonly StyleResolver _styleResolver;
    private readonly OsdState _osd;
    private volatile RenderSnapshot _snap;

    private sealed record RenderSnapshot(PluginSettings Settings, string Preamble);

    public OverlayRenderer(PluginSettings settings, StyleResolver styleResolver, OsdState osd)
    {
        _styleResolver = styleResolver;
        _osd = osd;
        _snap = new RenderSnapshot(settings, BuildPreamble(settings, 1280f));
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
    {
        var s = _snap.Settings;
        _snap = new RenderSnapshot(s, BuildPreamble(s, ComputeResX(_osd.Width, _osd.Height)));
    }

    public void UpdateSettings(PluginSettings newSettings)
    {
        var preamble = BuildPreamble(newSettings, ComputeResX(_osd.Width, _osd.Height));
        _snap = new RenderSnapshot(newSettings, preamble);
    }

    private static string BuildPreamble(PluginSettings settings, float resX)
    {
        int align = ClampAlign(settings.SubtitleAlignment);
        return $@"{{\an{align}{BuildPositionTags(resX, settings, align)}{BuildStyleTags(settings)}}}";
    }

    public string RenderSubtitle(
        string originalText,
        ParseCacheEntry entry,
        HashSet<(int WordId, byte ReadingIndex)>? iPlusOneWords = null,
        HashSet<(int WordId, byte ReadingIndex)>? frequencyWords = null,
        HashSet<(int WordId, byte ReadingIndex)>? revealedWords = null)
    {
        var snap = _snap;
        var sb = new StringBuilder();
        sb.Append(snap.Preamble);

        var unparsed = snap.Settings.BlurEnabled ? UnparsedBlurReset : ThemePresets.Unparsed;

        int lastEnd = 0;
        foreach (var token in entry.Tokens)
        {
            if (token.Start > lastEnd)
            {
                AssTagBuilder.AppendStyle(sb, unparsed);
                AssTagBuilder.AppendEscapedText(sb, originalText, lastEnd, token.Start - lastEnd);
            }

            var style = _styleResolver.Resolve(
                token, entry.VocabStates, iPlusOneWords, frequencyWords, entry.PitchClasses);
            if (revealedWords is not null && style.Blur is > 0
                && revealedWords.Contains((token.WordId, token.ReadingIndex)))
            {
                style = StyleResolver.BlurOffStyle.MergeOver(style);
            }

            AssTagBuilder.AppendStyle(sb, style);
            AssTagBuilder.AppendEscapedText(sb, originalText, token.Start, token.Length);

            lastEnd = token.Start + token.Length;
        }

        if (lastEnd < originalText.Length)
        {
            AssTagBuilder.AppendStyle(sb, unparsed);
            AssTagBuilder.AppendEscapedText(sb, originalText, lastEnd, originalText.Length - lastEnd);
        }

        return sb.ToString();
    }

    public string RenderPlain(string text)
    {
        var sb = new StringBuilder();
        sb.Append(_snap.Preamble);
        AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
        AssTagBuilder.AppendEscapedText(sb, text, 0, text.Length);
        return sb.ToString();
    }
}
