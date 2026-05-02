using System.Text;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

public sealed class OverlayRenderer(PluginSettings settings, StyleResolver styleResolver)
{
    private readonly string _preamble = $@"{{\an2\fs{settings.FontSize}\fn{settings.FontFamily}\bord{settings.BorderSize}}}";

    public string RenderSubtitle(
        string originalText,
        ParseCacheEntry entry,
        HashSet<(int WordId, byte ReadingIndex)>? iPlusOneWords = null,
        HashSet<(int WordId, byte ReadingIndex)>? frequencyWords = null)
    {
        var sb = new StringBuilder();
        sb.Append(_preamble);

        int lastEnd = 0;
        foreach (var token in entry.Tokens)
        {
            if (token.Start > lastEnd)
            {
                AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
                sb.Append(originalText, lastEnd, token.Start - lastEnd);
            }

            var style = styleResolver.Resolve(token, entry.VocabStates, iPlusOneWords, frequencyWords);
            AssTagBuilder.AppendStyle(sb, style);
            sb.Append(originalText, token.Start, token.Length);

            lastEnd = token.Start + token.Length;
        }

        if (lastEnd < originalText.Length)
        {
            AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
            sb.Append(originalText, lastEnd, originalText.Length - lastEnd);
        }

        sb.Replace("\n", "\\N");
        return sb.ToString();
    }

    public string RenderPlain(string text)
    {
        var sb = new StringBuilder();
        sb.Append(_preamble);
        AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
        sb.Append(text);
        sb.Replace("\n", "\\N");
        return sb.ToString();
    }
}