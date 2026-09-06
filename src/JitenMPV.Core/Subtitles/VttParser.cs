using System.Net;
using System.Text.RegularExpressions;

namespace JitenMPV.Core.Subtitles;

public static partial class VttParser
{
    public static List<SubtitleCue> Parse(string content)
    {
        var cues = new List<SubtitleCue>();
        var blocks = content.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n')
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) continue;

            var timingIndex = Array.FindIndex(lines, l => l.Contains("-->"));
            if (timingIndex is < 0 or > 1) continue;

            var timingMatch = TimingPattern().Match(lines[timingIndex]);
            if (!timingMatch.Success) continue;

            var text = CleanText(string.Join('\n', lines[(timingIndex + 1)..]));
            if (string.IsNullOrWhiteSpace(text)) continue;

            cues.Add(new SubtitleCue(
                ParseTimestamp(timingMatch.Groups[1].Value),
                ParseTimestamp(timingMatch.Groups[2].Value),
                text));
        }

        return cues;
    }

    private static TimeSpan ParseTimestamp(string ts)
    {
        var parts = ts.Split(':', '.');
        return parts.Length switch
        {
            4 => new TimeSpan(0, int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3])),
            3 => new TimeSpan(0, 0, int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2])),
            _ => TimeSpan.Zero
        };
    }

    private static string CleanText(string text)
        => WebUtility.HtmlDecode(TagPattern().Replace(text, "")).Trim();

    [GeneratedRegex(@"((?:\d{1,2}:)?\d{2}:\d{2}\.\d{3})\s*-->\s*((?:\d{1,2}:)?\d{2}:\d{2}\.\d{3})")]
    private static partial Regex TimingPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();
}
