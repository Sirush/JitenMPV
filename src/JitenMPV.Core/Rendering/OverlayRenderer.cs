using System.Text;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

public sealed class OverlayRenderer
{
    private static readonly WordStyleState BlurRevealStyle = new() { Blur = 0 };

    private readonly PluginSettings _settings;
    private readonly StyleResolver _styleResolver;
    private readonly OsdState _osd;
    private string _preamble;

    public OverlayRenderer(PluginSettings settings, StyleResolver styleResolver, OsdState osd)
    {
        _settings = settings;
        _styleResolver = styleResolver;
        _osd = osd;
        _preamble = BuildPreamble(settings, 1280f);
    }

    public const int OverlayResY = 720;
    public const float ResY = OverlayResY;

    public static float ComputeResX(int osdWidth, int osdHeight)
        => osdHeight > 0 ? ResY * osdWidth / osdHeight : 1280f;

    public static string BuildStyleTags(PluginSettings settings)
        => $@"\fs{settings.FontSize}\fn{settings.FontFamily}\bord{settings.BorderSize}";

    public static string BuildPositionTags(float resX, PluginSettings settings)
    {
        float centerX = resX / 2;
        float posY = ResY - settings.BottomMargin;
        return $@"\pos({centerX:F0},{posY:F0})";
    }

    public void RebuildPreamble() => _preamble = BuildPreamble(_settings, ComputeResX(_osd.Width, _osd.Height));

    private static string BuildPreamble(PluginSettings settings, float resX)
        => $@"{{\an2{BuildPositionTags(resX, settings)}{BuildStyleTags(settings)}}}";


    public string RenderSubtitle(
        string originalText,
        ParseCacheEntry entry,
        HashSet<(int WordId, byte ReadingIndex)>? iPlusOneWords = null,
        HashSet<(int WordId, byte ReadingIndex)>? frequencyWords = null,
        HashSet<(int WordId, byte ReadingIndex)>? revealedWords = null)
    {
        var sb = new StringBuilder();
        sb.Append(_preamble);

        int lastEnd = 0;
        foreach (var token in entry.Tokens)
        {
            if (token.Start > lastEnd)
            {
                AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
                AssTagBuilder.AppendEscapedText(sb, originalText, lastEnd, token.Start - lastEnd);
            }

            var style = _styleResolver.Resolve(token, entry.VocabStates, iPlusOneWords, frequencyWords);
            if (revealedWords is not null && style.Blur is > 0
                && revealedWords.Contains((token.WordId, token.ReadingIndex)))
            {
                style = BlurRevealStyle.MergeOver(style);
            }

            AssTagBuilder.AppendStyle(sb, style);
            AssTagBuilder.AppendEscapedText(sb, originalText, token.Start, token.Length);

            lastEnd = token.Start + token.Length;
        }

        if (lastEnd < originalText.Length)
        {
            AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
            AssTagBuilder.AppendEscapedText(sb, originalText, lastEnd, originalText.Length - lastEnd);
        }

        return sb.ToString();
    }

    public string RenderPlain(string text)
    {
        var sb = new StringBuilder();
        sb.Append(_preamble);
        AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
        AssTagBuilder.AppendEscapedText(sb, text, 0, text.Length);
        return sb.ToString();
    }
}
