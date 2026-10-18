using System.Text.RegularExpressions;

namespace JitenMPV.Core.MediaInfo;

/// <param name="Title">Can be stale from the previous file when mpv is reused.</param>
public sealed record MpvMediaProperties(
    string? Path,
    string? Filename,
    string? MediaTitle,
    string? Title,
    string? MetadataTitle);

/// <param name="FolderTitle">From the parent folder, or the grandparent when the parent is a
/// "Season 2"-style folder.</param>
public sealed record MediaIdentity(
    string? Title,
    string? SeriesTitle,
    int? Season,
    int? Episode,
    bool IsStrongEpisode,
    int? Year,
    bool IsExtra,
    bool IsStream,
    string? TitleSource,
    string? FolderTitle,
    IReadOnlyList<string> TitleCandidates);

public static partial class MediaIdentityResolver
{
    public static MediaIdentity? Resolve(MpvMediaProperties props)
    {
        var path = NullIfEmpty(props.Path);
        var isStream = ReleaseNameParser.IsUrl(path);
        var filename = NullIfEmpty(props.Filename) ?? (path is null ? null : LastSegment(path));
        string? folder = null, seasonFolder = null;
        if (path is not null && !isStream)
            (folder, seasonFolder) = SeriesFolder(path);

        var candidates = new List<string>();
        void Add(string? value)
        {
            if (!string.IsNullOrEmpty(value) && value != path && !candidates.Contains(value))
                candidates.Add(value);
        }

        if (isStream)
        {
            Add(props.Title);
            Add(props.MediaTitle);
            Add(props.MetadataTitle);
            Add(filename);
        }
        else
        {
            Add(filename);
            foreach (var bracket in LaterBrackets(filename))
                Add(bracket);
            Add(folder);
            Add(props.Title);
            Add(props.MediaTitle);
            Add(props.MetadataTitle);
        }

        if (candidates.Count == 0) return null;

        var parsed = candidates.Select(ReleaseNameParser.Parse).ToList();
        var chosen = parsed.FindIndex(p => !p.IsJunkTitle);
        var best = chosen < 0 ? null : parsed[chosen];
        var titles = parsed.Where(p => !p.IsJunkTitle).Select(p => p.Title).Distinct().ToList();

        // A local file's episode comes from its own name only: folder names carry ranges such as
        // "01 ~ 14" and window titles go stale when mpv is reused for the next file.
        var episodeSources = isStream
            ? parsed
            : filename is null ? [] : [ReleaseNameParser.Parse(filename)];
        var strong = episodeSources.FirstOrDefault(p => p.IsStrongEpisode);
        var episodeSource = strong ?? episodeSources.FirstOrDefault(p => p.Episode is not null);

        var seasonSources = isStream ? parsed : LocalSeasonSources(filename, seasonFolder, folder, best);
        var season = seasonSources.Select(p => p.Season).FirstOrDefault(s => s is not null);
        // A Specials folder is season 0 unless the file numbers itself, so it never lands on the TV run.
        if (seasonFolder is not null && SpecialsFolder().IsMatch(seasonFolder)
            && (filename is null || ReleaseNameParser.Parse(filename).Season is null))
            season = 0;

        var folderTitle = folder is null ? null : ReleaseNameParser.FolderTitle(folder);
        var lowerName = (filename ?? "").ToLowerInvariant();

        return new MediaIdentity(
            best?.Title,
            best?.SeriesTitle,
            season,
            episodeSource?.Episode,
            strong is not null,
            episodeSource is null ? best?.Year : null,
            ExtraMarker().IsMatch(lowerName) || (seasonFolder is not null && ExtrasFolder().IsMatch(seasonFolder)),
            isStream,
            chosen < 0 ? null : candidates[chosen],
            folderTitle,
            titles);
    }

    private static List<ReleaseName> LocalSeasonSources(
        string? filename, string? seasonFolder, string? folder, ReleaseName? best)
    {
        var sources = new List<ReleaseName>();
        foreach (var text in new[] { filename, seasonFolder, folder })
            if (text is not null)
                sources.Add(ReleaseNameParser.Parse(text));
        if (best is not null)
            sources.Add(best);
        return sources;
    }

    /// Skips the first bracket (the release group); later ones can hold the title.
    private static IEnumerable<string> LaterBrackets(string? filename)
        => filename is null
            ? []
            : Bracket().Matches(filename).Skip(1).Select(m => m.Groups[1].Value)
                .Where(v => ThreeLetters().IsMatch(v));

    private static (string? Folder, string? SeasonFolder) SeriesFolder(string path)
    {
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return (null, null);
        var parent = parts[^2].Trim();
        if (!SeasonOnlyFolder().IsMatch(parent) && !ExtrasFolder().IsMatch(parent)) return (parent, null);
        return (parts.Length >= 3 ? parts[^3].Trim() : null, parent);
    }

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var index = trimmed.LastIndexOfAny(['/', '\\']);
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    [GeneratedRegex(@"(?<![a-z0-9])(?:ncop|nced|creditless|commentary|menu|pv|cm|trailer|teaser|tokuten)(?![a-z])|\[(?:sp|live|iv)\]")]
    private static partial Regex ExtraMarker();

    [GeneratedRegex(@"\[([^\]]+)\]")]
    private static partial Regex Bracket();

    [GeneratedRegex(@"\p{L}{3}")]
    private static partial Regex ThreeLetters();

    [GeneratedRegex(@"^(?:season\s*\d+|s\d+|\d+(?:st|nd|rd|th)\s+season|specials?|disc\s*\d+|第[0-9０-９]+期|ova|oads?|movies?)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SeasonOnlyFolder();

    /// Bonus-disc folders: creditless songs, menus, PVs, CMs and interviews, never an episode.
    [GeneratedRegex(@"^(?:extras?|bonus|sps?|nc|ncop|nced|menus?|特典映像|映像特典|特典)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtrasFolder();

    [GeneratedRegex(@"^specials?$", RegexOptions.IgnoreCase)]
    private static partial Regex SpecialsFolder();
}
