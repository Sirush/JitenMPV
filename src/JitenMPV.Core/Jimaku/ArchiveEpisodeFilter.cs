using System.Globalization;
using System.Text.RegularExpressions;
using JitenMPV.Core.MediaInfo;

namespace JitenMPV.Core.Jimaku;

/// Picks an archive's members for one episode, failing closed so a season pack never yields
/// another episode's subtitle.
public static partial class ArchiveEpisodeFilter
{
    public static List<string> Filter(IReadOnlyList<string> files, int? episode)
    {
        if (episode is not { } wanted) return [.. files];

        var names = files.Select(Path.GetFileName).ToList();
        var numbers = names.Select(n => ReleaseNameParser.ExtractEpisode(n!)).ToList();
        if (numbers.All(n => n is null))
        {
            if (files.Count == 1) return [.. files];
            // Some packs number episodes in a way the release-name parser misses; their first short
            // number is the episode when it differs for every member.
            numbers = names.Select(n => FirstShortNumber(n!)).ToList();
            if (numbers.Any(n => n is null) || numbers.Distinct().Count() < numbers.Count) return [];
        }
        return [.. files.Where((_, i) => numbers[i] == wanted)];
    }

    internal static int? FirstShortNumber(string name)
    {
        var match = ShortNumber().Match(Path.GetFileNameWithoutExtension(name));
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])[0-9]{1,3}(?![A-Za-z0-9])")]
    private static partial Regex ShortNumber();
}
