namespace JitenMPV.Core.Subtitles;

public static class SubtitleParser
{
    public static List<SubtitleCue> ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var format = DetectFormat(Path.GetExtension(filePath), content);

        return format switch
        {
            SubtitleFormat.Ass => AssParser.Parse(content),
            SubtitleFormat.Srt => SrtParser.Parse(content),
            SubtitleFormat.Vtt => VttParser.Parse(content),
            _ => []
        };
    }

    public static SubtitleFormat DetectFormat(string extension, string content)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".ass" or ".ssa": return SubtitleFormat.Ass;
            case ".srt": return SubtitleFormat.Srt;
            case ".vtt": return SubtitleFormat.Vtt;
        }

        var head = content.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (head.StartsWith("WEBVTT", StringComparison.Ordinal)) return SubtitleFormat.Vtt;
        if (head.Contains("[Script Info]", StringComparison.OrdinalIgnoreCase)
            || head.Contains("[Events]", StringComparison.OrdinalIgnoreCase)) return SubtitleFormat.Ass;
        if (head.Contains("-->", StringComparison.Ordinal)) return SubtitleFormat.Srt;
        return SubtitleFormat.Unknown;
    }
}

public enum SubtitleFormat { Unknown, Ass, Srt, Vtt }
