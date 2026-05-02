using System.Text;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

public sealed class OverlayRenderer(PluginSettings settings)
{
    private readonly string _preamble = $@"{{\an2\fs{settings.FontSize}\fn{settings.FontFamily}\bord{settings.BorderSize}}}";

    public string RenderSubtitle(
        string originalText,
        List<ReaderToken> tokens,
        Dictionary<(int WordId, byte ReadingIndex), KnownState> vocabStates)
    {
        var sb = new StringBuilder();
        AppendPreamble(sb);

        int lastEnd = 0;
        foreach (var token in tokens)
        {
            if (token.Start > lastEnd)
            {
                AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
                sb.Append(originalText, lastEnd, token.Start - lastEnd);
            }

            var style = ResolveStyle(token, vocabStates);
            AssTagBuilder.AppendStyle(sb, style);
            sb.Append(originalText, token.Start, token.Length);

            lastEnd = token.Start + token.Length;
        }

        if (lastEnd < originalText.Length)
        {
            AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
            sb.Append(originalText, lastEnd, originalText.Length - lastEnd);
        }

        return sb.ToString();
    }

    public string RenderPlain(string text)
    {
        var sb = new StringBuilder();
        AppendPreamble(sb);
        AssTagBuilder.AppendStyle(sb, ThemePresets.Unparsed);
        sb.Append(text);
        return sb.ToString();
    }

    private void AppendPreamble(StringBuilder sb)
    {
        sb.Append(_preamble);
    }

    private static WordStyleState ResolveStyle(
        ReaderToken token,
        Dictionary<(int, byte), KnownState> vocabStates)
    {
        if (vocabStates.TryGetValue((token.WordId, token.ReadingIndex), out var state)
            && ThemePresets.Default.TryGetValue(state, out var style))
        {
            return style;
        }

        return ThemePresets.Unparsed;
    }
}