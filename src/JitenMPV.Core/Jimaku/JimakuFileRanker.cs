using System.Text.RegularExpressions;
using JitenMPV.Core.MediaInfo;

namespace JitenMPV.Core.Jimaku;

public static partial class JimakuFileRanker
{
    private static readonly HashSet<string> TextExtensions = ["srt", "ass", "ssa", "vtt"];
    private static readonly HashSet<string> ArchiveExtensions = ["zip", "7z", "rar"];

    public static string ExtensionOf(string name)
    {
        var ext = Path.GetExtension(name);
        return ext.Length > 1 ? ext[1..].ToLowerInvariant() : "";
    }

    public static bool IsText(string name) => TextExtensions.Contains(ExtensionOf(name));

    public static bool IsArchive(string name) => ArchiveExtensions.Contains(ExtensionOf(name));

    /// Files labelled Chinese on Jimaku nearly always carry the Japanese lines too.
    public static bool IsBilingualName(string name)
        => BilingualLabel().IsMatch(JimakuEntryMatcher.Normalize(name));

    /// Best first; archives that cannot be opened are dropped.
    public static List<T> Rank<T>(IEnumerable<T> files, Func<T, string> name, int? episode, string? mediaName)
    {
        var group = ReleaseGroupOf(mediaName);
        return [.. files
            .Where(f => IsText(name(f)) || (IsArchive(name(f)) && SubtitleArchive.CanExtract(ExtensionOf(name(f)))))
            .Select((file, index) => (File: file, Index: index, Score: Score(name(file), episode, group)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => x.File)];
    }

    internal static int Score(string name, int? episode, string? releaseGroup)
    {
        // JitenMPV draws mpv's sub-text itself, so ASS styling is lost anyway, while its sign
        // and karaoke events add noise; plain SRT reads cleanest.
        var score = ExtensionOf(name) switch
        {
            "srt" => 30,
            "ass" or "ssa" or "vtt" => 25,
            _ => 10
        };

        if (episode is { } wanted)
        {
            var found = ReleaseNameParser.ExtractEpisode(name);
            if (found == wanted) score += 100;
            else if (found is not null) score -= 400;
        }

        if (IsBilingualName(name)) score -= 150;

        // A subtitle from the video's own release group is the likeliest to share its timing.
        if (releaseGroup is not null && ReleaseGroupOf(name) is { } fileGroup
            && string.Equals(fileGroup, releaseGroup, StringComparison.OrdinalIgnoreCase))
            score += 15;

        return score;
    }

    internal static string? ReleaseGroupOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var match = LeadingGroup().Match(name);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"(?:^| )(?:chs|cht|sc|jpsc|cn)(?: |$)")]
    private static partial Regex BilingualLabel();

    [GeneratedRegex(@"^\s*\[([^\]]+)\]")]
    private static partial Regex LeadingGroup();
}
