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

        if (style.OutlineSize is { } bord)
            sb.Append("\\bord").Append(bord);

        if (style.TextOpacity is { } opacity and < 255)
            sb.Append("\\1a&H").Append((255 - opacity).ToString("X2")).Append('&');

        if (style.Bold == true) sb.Append("\\b1");
        if (style.Italic == true) sb.Append("\\i1");
        if (style.Underline == true) sb.Append("\\u1");

        sb.Append('}');
    }
}
