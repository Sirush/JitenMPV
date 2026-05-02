namespace JitenMPV.Core.Subtitles;

public static class SubtitleParser
{
    public static List<SubtitleCue> ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".ass" or ".ssa" => AssParser.Parse(content),
            ".srt" => SrtParser.Parse(content),
            _ => []
        };
    }
}
