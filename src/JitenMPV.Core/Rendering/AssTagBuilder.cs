using System.Globalization;
using System.Text;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

public static class AssTagBuilder
{
    /// <param name="defaultOutline">Border width for styles that don't set one; must match the
    /// preamble's <c>\bord</c>, since overrides persist along the line and a word that omits the
    /// tag inherits the previous word's border.</param>
    public static void AppendStyle(StringBuilder sb, WordStyleState style, double defaultOutline)
    {
        sb.Append('{');

        if (style.TextColorBgr is not null)
            sb.Append("\\c&H").Append(style.TextColorBgr).Append('&');

        if (style.OutlineColorBgr is not null)
            sb.Append("\\3c&H").Append(style.OutlineColorBgr).Append('&');

        if (style.ShadowColorBgr is not null)
            sb.Append("\\4c&H").Append(style.ShadowColorBgr).Append('&');

        AppendNum(sb, "\\bord", style.OutlineSize ?? defaultOutline);

        AppendNum(sb, "\\shad", style.ShadowDepth is { } shad and > 0 ? shad : 0);

        AppendAlpha(sb, "\\1a", style.TextOpacity);
        AppendAlpha(sb, "\\3a", style.OutlineOpacity);
        AppendAlpha(sb, "\\4a", style.ShadowOpacity);

        AppendNum(sb, "\\blur", style.Blur ?? 0);

        sb.Append(style.Bold == true ? "\\b1" : "\\b0");
        sb.Append(style.Italic == true ? "\\i1" : "\\i0");
        sb.Append(style.Underline == true ? "\\u1" : "\\u0");
        sb.Append(style.Strikethrough == true ? "\\s1" : "\\s0");

        if (style.ScaleX is { } sx and not 100)
            sb.Append("\\fscx").Append(sx);
        if (style.ScaleY is { } sy and not 100)
            sb.Append("\\fscy").Append(sy);

        sb.Append('}');
    }

    private static void AppendNum(StringBuilder sb, string tag, double value)
        => sb.Append(tag).Append(value.ToString(CultureInfo.InvariantCulture));

    private static void AppendAlpha(StringBuilder sb, string tag, int? opacity)
    {
        var assAlpha = opacity is { } a ? 255 - Math.Clamp(a, 0, 255) : 0;
        sb.Append(tag).Append("&H").Append(assAlpha.ToString("X2")).Append('&');
    }

    public static void AppendEscapedText(StringBuilder sb, string text, int start, int length)
    {
        int end = start + length;
        for (int i = start; i < end; i++)
        {
            switch (text[i])
            {
                case '\n': sb.Append("\\N"); break;
                case '\r': break;
                case '{': sb.Append("\\{"); break;
                case '}': sb.Append("\\}"); break;
                case '\\':
                    sb.Append('\\');
                    // libass reads \N, \n and \h as formatting anywhere in the text field; an empty
                    // override block breaks the sequence so the backslash stays literal.
                    if (i + 1 < end && text[i + 1] is 'N' or 'n' or 'h')
                        sb.Append("{}");
                    break;
                default: sb.Append(text[i]); break;
            }
        }
    }

    public static string EscapeText(string text)
    {
        var sb = new StringBuilder(text.Length);
        AppendEscapedText(sb, text, 0, text.Length);
        return sb.ToString();
    }
}
