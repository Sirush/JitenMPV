using System.Text;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

public static class AssTagBuilder
{
    public static void AppendStyle(StringBuilder sb, WordStyleState style)
    {
        sb.Append('{');

        if (style.TextColorBgr is not null)
            sb.Append("\\c&H").Append(style.TextColorBgr).Append('&');

        if (style.OutlineColorBgr is not null)
            sb.Append("\\3c&H").Append(style.OutlineColorBgr).Append('&');

        if (style.ShadowColorBgr is not null)
            sb.Append("\\4c&H").Append(style.ShadowColorBgr).Append('&');

        if (style.OutlineSize is { } bord)
            sb.Append("\\bord").Append(bord);

        if (style.ShadowDepth is { } shad and > 0)
            sb.Append("\\shad").Append(shad);

        if (style.TextOpacity is { } textAlpha and < 255)
            sb.Append("\\1a&H").Append((255 - textAlpha).ToString("X2")).Append('&');

        if (style.OutlineOpacity is { } outlineAlpha and < 255)
            sb.Append("\\3a&H").Append((255 - outlineAlpha).ToString("X2")).Append('&');

        if (style.ShadowOpacity is { } shadowAlpha and < 255)
            sb.Append("\\4a&H").Append((255 - shadowAlpha).ToString("X2")).Append('&');

        if (style.Blur is { } blur and > 0)
            sb.Append("\\blur").Append(blur);

        if (style.Bold == true) sb.Append("\\b1");
        if (style.Italic == true) sb.Append("\\i1");
        if (style.Underline == true) sb.Append("\\u1");
        if (style.Strikethrough == true) sb.Append("\\s1");

        if (style.ScaleX is { } sx and not 100)
            sb.Append("\\fscx").Append(sx);
        if (style.ScaleY is { } sy and not 100)
            sb.Append("\\fscy").Append(sy);

        sb.Append('}');
    }
}
