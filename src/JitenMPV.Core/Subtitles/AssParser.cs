using System.Text.RegularExpressions;

namespace JitenMPV.Core.Subtitles;

public static partial class AssParser
{
    public static List<SubtitleCue> Parse(string content)
    {
        var cues = new List<SubtitleCue>();
        var lines = content.Replace("\r\n", "\n").Split('\n');

        bool inEvents = false;
        int textFieldIndex = -1;

        foreach (var line in lines)
        {
            if (line.StartsWith("[Events]", StringComparison.OrdinalIgnoreCase))
            {
                inEvents = true;
                continue;
            }

            if (line.StartsWith('['))
            {
                inEvents = false;
                continue;
            }

            if (!inEvents) continue;

            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                var fields = line["Format:".Length..].Split(',');
                for (int i = 0; i < fields.Length; i++)
                {
                    if (fields[i].Trim().Equals("Text", StringComparison.OrdinalIgnoreCase))
                    {
                        textFieldIndex = i;
                        break;
                    }
                }
                continue;
            }

            if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;
            if (textFieldIndex < 0) continue;

            var rest = line["Dialogue:".Length..];
            var parts = rest.Split(',');
            if (parts.Length <= textFieldIndex) continue;

            var start = ParseAssTimestamp(parts[1].Trim());
            var end = ParseAssTimestamp(parts[2].Trim());

            // Text field is the last field and may contain commas
            var text = string.Join(',', parts[textFieldIndex..]);
            text = StripAssTags(text.Trim());
            text = text.Replace("\\N", "\n").Replace("\\n", "\n");

            if (!string.IsNullOrWhiteSpace(text))
                cues.Add(new SubtitleCue(start, end, text));
        }

        return cues;
    }

    private static TimeSpan ParseAssTimestamp(string ts)
    {
        var parts = ts.Split(':', '.');
        if (parts.Length < 4) return TimeSpan.Zero;
        return new TimeSpan(0, int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]),
            int.Parse(parts[3]) * 10);
    }

    private static string StripAssTags(string text) => AssTagPattern().Replace(text, "");

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex AssTagPattern();
}
