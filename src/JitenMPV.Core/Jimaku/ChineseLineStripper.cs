using System.Text.RegularExpressions;

namespace JitenMPV.Core.Jimaku;

/// Turns bilingual Japanese/Chinese ASS and SRT subtitles into Japanese-only ones. A line with han
/// but no kana is Chinese once the file has enough of them to be bilingual: Japanese subtitles
/// still have a few, such as speaker names.
public static partial class ChineseLineStripper
{
    /// One subtitle event: the file lines it spans and its text lines. An ASS event's text lines
    /// are its \N-separated parts, after Prefix.
    private sealed record Cue(int First, int Count, string Prefix, List<string> Parts);

    public static (string Text, bool Changed) Strip(string content, string extension)
    {
        var ass = extension is "ass" or "ssa";
        if (!ass && extension is not ("srt" or "vtt")) return (content, false);

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var cues = ass ? AssCues(lines) : SrtCues(lines);

        var parts = cues.SelectMany(c => c.Parts).ToList();
        var kana = parts.Count(HasKana);
        var chinese = parts.Count(IsChinese);
        if (kana == 0 || chinese == 0 || chinese * 5 < kana) return (content, false);

        var output = lines.Cast<string?>().ToArray();
        foreach (var cue in cues)
        {
            var kept = cue.Parts.Where(p => !IsChinese(p)).ToList();
            if (kept.Count == cue.Parts.Count) continue;

            if (kept.Count == 0)
                Array.Fill(output, null, cue.First, cue.Count);
            else if (ass)
                output[cue.First] = cue.Prefix + string.Join("\\N", kept);
            else
                for (var i = cue.First; i < cue.First + cue.Count; i++)
                    if (IsChinese(lines[i]) && !lines[i].Contains("-->", StringComparison.Ordinal))
                        output[i] = null;
        }

        return (string.Join(newline, output.OfType<string>()), true);
    }

    private static List<Cue> AssCues(string[] lines)
    {
        var cues = new List<Cue>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("Dialogue:", StringComparison.Ordinal)) continue;
            var start = 0;
            for (var field = 0; field < 9 && start >= 0; field++)
            {
                var comma = lines[i].IndexOf(',', start);
                start = comma < 0 ? -1 : comma + 1;
            }
            if (start < 0) continue;
            cues.Add(new Cue(i, 1, lines[i][..start], [.. AssLineBreak().Split(lines[i][start..])]));
        }
        return cues;
    }

    /// A block is an optional index, a timing line and its text, with the blank line after it.
    private static List<Cue> SrtCues(string[] lines)
    {
        var cues = new List<Cue>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("-->", StringComparison.Ordinal)) continue;
            var first = i > 0 && lines[i - 1].Trim().Length > 0 ? i - 1 : i;
            var end = i + 1;
            while (end < lines.Length && lines[end].Trim().Length > 0) end++;
            cues.Add(new Cue(first, Math.Min(end + 1, lines.Length) - first, "", [.. lines[(i + 1)..end]]));
            i = end;
        }
        return cues;
    }

    private static bool HasKana(string text)
        => text.Any(c => c is >= '぀' and <= 'ヿ' or >= 'ㇰ' and <= 'ㇿ' or >= 'ｦ' and <= 'ﾟ');

    private static bool IsChinese(string text)
        => !HasKana(text)
           && AssTag().Replace(text, "").Any(c => c is >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or >= '豈' and <= '﫿');

    [GeneratedRegex(@"\\[Nn]")]
    private static partial Regex AssLineBreak();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex AssTag();
}
