using System.Text;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Plugin;

/// Unwraps the line breaks a subtitle file put in for readability, so a sentence is coloured,
/// parsed and hit-tested as one line whenever the joined form still fits across the screen.
public sealed class SubtitleLineJoiner(PluginSettings settings, OsdState osd)
{
    /// Below SubtitleMeasurer's block, which starts at 99 and grows with the token count.
    private const int MeasureId = 98;

    /// U+3000 and up covers kana, kanji and the full-width punctuation that surrounds them.
    private const char CjkStart = '　';

    private volatile PluginSettings _settings = settings;

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    /// Returns the text to render, which is the joined line when it fits and the original otherwise.
    public async Task<string> ResolveAsync(string text, MpvIpcClient ipc, CancellationToken ct)
    {
        var s = _settings;
        if (!s.SubtitleSingleLine || !text.Contains('\n')) return text;

        var joined = Join(text);
        if (joined.Length == 0 || joined == text) return text;

        // \q2 suppresses libass wrapping: a measurement that wraps reports the width of the play
        // res rather than of the line, which would read as fitting no matter how long it is.
        var ass = $@"{{\an7\pos(0,0)\q2{OverlayRenderer.BuildStyleTags(s)}}}{AssTagBuilder.EscapeText(joined)}";
        var bounds = await ipc.MeasureOverlayAsync(MeasureId, ass, ct);
        await ipc.RemoveOverlayAsync(MeasureId, ct);

        return bounds is not null && bounds.Width <= AvailableWidth(s) ? joined : text;
    }

    public static string Join(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            if (sb.Length > 0 && NeedsSpace(sb[^1], trimmed[0]))
                sb.Append(' ');
            sb.Append(trimmed);
        }
        return sb.ToString();
    }

    /// Japanese wraps mid-sentence without spaces, so a break between two CJK characters closes up;
    /// Latin text keeps the word boundary the break stood for.
    private static bool NeedsSpace(char left, char right)
        => left < CjkStart && right < CjkStart;

    private float AvailableWidth(PluginSettings s)
    {
        float resX = OverlayRenderer.ComputeResX(osd.Width, osd.Height);
        int align = OverlayRenderer.ClampAlign(s.SubtitleAlignment);

        // A centred line spends its margin on both sides; an edge-aligned one only on its own.
        return resX - s.SubtitleMarginX * (align % 3 == 2 ? 2 : 1);
    }
}
