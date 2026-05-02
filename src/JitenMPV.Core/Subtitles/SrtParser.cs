using System.Text.RegularExpressions;

namespace JitenMPV.Core.Subtitles;

public static partial class SrtParser
{
    public static List<SubtitleCue> Parse(string content)
    {
        var cues = new List<SubtitleCue>();
        var blocks = content.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3) continue;

            var timingMatch = TimingPattern().Match(lines[1]);
            if (!timingMatch.Success) continue;

            var start = ParseTimestamp(timingMatch.Groups[1].Value);
            var end = ParseTimestamp(timingMatch.Groups[2].Value);
            var text = StripHtmlTags(string.Join('\n', lines[2..]));

            if (!string.IsNullOrWhiteSpace(text))
                cues.Add(new SubtitleCue(start, end, text));
        }

        return cues;
    }

    private static TimeSpan ParseTimestamp(string ts)
    {
        var parts = ts.Split(':', ',');
        if (parts.Length < 4) return TimeSpan.Zero;
        return new TimeSpan(0, int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
    }

    private static string StripHtmlTags(string text) => HtmlTagPattern().Replace(text, "");

    [GeneratedRegex(@"(\d{2}:\d{2}:\d{2},\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2},\d{3})")]
    private static partial Regex TimingPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagPattern();
}
