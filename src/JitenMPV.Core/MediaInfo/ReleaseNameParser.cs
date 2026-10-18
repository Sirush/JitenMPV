using System.Globalization;
using System.Text.RegularExpressions;

namespace JitenMPV.Core.MediaInfo;

/// <param name="Title">Keeps a season suffix such as "S3": subtitle sites name sequels that way.</param>
/// <param name="Episode">Null for half episodes such as 12.5.</param>
/// <param name="IsStrongEpisode">From SxxEyy or 01x05, which outranks every other source.</param>
public sealed record ReleaseName(
    string Title,
    string SeriesTitle,
    int? Season,
    int? Episode,
    bool IsStrongEpisode,
    int? Year,
    bool IsJunkTitle);

public static partial class ReleaseNameParser
{
    private static readonly Regex[] ReleaseTags =
    [
        new(@"\[.*?\]"), new(@"\(.*?\)"),
        new(@"(?<![A-Za-z0-9])(?:2160|1080|720|576|480|360)[pPiI](?![A-Za-z0-9])"),
        new(@"(?<!\d)\d{3,4}x\d{3,4}(?!\d)"), new(@"[xX]26[45]"), new(@"(?<![A-Za-z0-9])[hH]\.?26[45]"),
        new(@"[bB]lu-?[rR]ay"), new("Hi10P"), new("FLAC"), new(@"(?:AAC|DDP|DD|AC3)\d\.\d"), new("AAC"),
        new("OPUS"), new("AV1"),
        new("Dual Audio"), new("HEVC"), new("WEB-DL"), new("WEBRip"),
        new(@"(?<![A-Za-z0-9])(?:10|8)[- ]?[bB]it(?![A-Za-z])"),
        new(@"(?<![A-Za-z0-9])(?:NF|AMZN|CR|DSNP|HIDIVE|ADN|B-Global"
            + @"|(?i:Netflix|Amazon|Hulu|U-NEXT|FOD|DMM-TV|Viki|WOWOW|TVer|iQIYI|HDTV|WEBDL|DVDRip|REMUX))(?![A-Za-z0-9])"),
        new(@"[-\s]*(?<!\d)(?:19|20)\d\d([-.])[01]\d\1[0-3]\d(?!\d)(?:-[A-Za-z][A-Za-z0-9]*(?=\.\w{2,4}$|$))?"),
        new("-?JPTVclub"),
        new("「[^」]*」"),
        new(@"\.(?:mkv|mp4|avi|webm|m4v|mov|ts|m2ts|wmv|flv|ogm|3gp)$", RegexOptions.IgnoreCase),
    ];

    private static readonly Regex[] StrongEpisodes = [new(@"[Ss]\d+[Ee][Pp]?(\d+)"), new(@"(?<![A-Za-z0-9])\d+[xX](\d+)")];

    private static readonly Regex[] WeakEpisodes =
    [
        new(@"[Ss]\d+[Ee][Pp]?(\d+)"), new(@"[Ee]pisode\s*(\d+)"),
        new(@"(?<![A-Za-z])[Ee][Pp]\.?\s*(\d+)"), new(@"(?<![A-Za-z0-9])\d+[xX](\d+)"),
        new(@"\s(\d{1,3}|(?!19|20)\d{4})(?:[vV]\d+)?\s*-\s*[A-Za-z]"), new(@" - (\d+)[vV]?\d*(?![\d\p{L}])"),
        new(@"\[(\d\d?)\]"), new(@"\[(\d\d?)[vV]\d+\]"),
        new(@"(?<![A-Za-z0-9])[Ee][Pp]?(\d+)(?!\d)"),
        new(@"第([0-9０-９]+)話"), new(@"(?<!\S)[#＃]([0-9０-９]+)(?![0-9０-９])"), new("（([0-9０-９]{1,3})）"),
        new(@"(?<=\p{L}_)(\d{2,3})(?:[vV]\d+)?(?:\.[A-Za-z0-9]{2,4})?$"),
    ];

    private static readonly Regex[] Seasons =
    [
        new(@"[Ss](\d+)[Ee][Pp]?\d+"), new(@"(?<![A-Za-z0-9])(\d+)[xX]\d\d(?!\d)"), new(@"[Ss]eason\s*(\d+)"),
        new(@"(?<![A-Za-z0-9])[Ss](\d+)(?!\d)"), new(@"(\d+)(?:st|nd|rd|th)\s+[Ss]eason"),
        new(@"(\d+)(?:st|nd|rd|th)\s+[Aa]ttack"), new(@"第([0-9０-９]+)期"),
        new(@"(?<=[^\x00-\x7F])(\d{1,2})(?:st|nd|rd|th)(?![A-Za-z])"),
    ];

    private static readonly Regex[] SeasonSuffixes =
    [
        new(@"^(.*?)\s+[Ss]\d+$"), new(@"^(.*?)\s+\d+(?:st|nd|rd|th)\s+[Ss]eason$"),
        new(@"^(.*?)\s+[Ss]eason\s+\d+$"), new(@"^(.*?)\s*第[0-9０-９]+期$"),
        new(@"^(.*?)\s+(?:II|III|IV|V)$"), new(@"^(.*?[^\x00-\x7F])\d{1,2}(?:st|nd|rd|th)$"),
    ];

    private static readonly string[] RomanSeasons = ["II", "III", "IV", "V"];

    public static bool IsUrl(string? path)
        => path is not null && UrlScheme().IsMatch(path);

    public static ReleaseName Parse(string text)
    {
        var strong = ExtractStrongEpisode(text);
        var episode = strong ?? ExtractEpisode(text);
        var title = ExtractTitle(text);

        // With a bracketed episode, a number left at the end belongs to the title.
        var bracket = BracketEpisode().Match(text);
        if (episode is not null && bracket.Success && bracket.Groups[1].Value.Length <= 3
            && int.Parse(bracket.Groups[1].Value, CultureInfo.InvariantCulture) == episode)
            title = TrimTitle(Sanitize(BracketEpisodeTag().Replace(text, "")));

        var seriesTitle = SeriesTitleOf(title);
        var season = SeasonOf(text) ?? RomanSeason(title);
        var year = episode is null ? YearOf(text) : null;

        return new ReleaseName(title, seriesTitle, season, episode, strong is not null, year, IsJunkTitle(title));
    }

    /// Leaked mpv templates, UUIDs, CDN tokens and letterless leftovers are not titles.
    public static bool IsJunkTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return true;
        if (title.Contains('?') || title.Contains("token=", StringComparison.Ordinal)) return true;
        if (title.Contains("${", StringComparison.Ordinal) || title.Contains('}')) return true;
        if (Uuid().IsMatch(title)) return true;
        if (!title.Any(char.IsWhiteSpace) && title.Length >= 16 && HexOnly().IsMatch(title)) return true;
        if (ThreeLetters().IsMatch(title) || JapaneseScript().IsMatch(title)) return false;
        return title.Count(char.IsLetter) < 3;
    }

    internal static string Sanitize(string text)
    {
        text = LaterLenticular().Replace(text, " ").Replace('【', ' ').Replace('】', ' ');
        foreach (var tag in ReleaseTags)
            text = tag.Replace(text, " ");
        return Whitespace().Replace(SeparatorChars().Replace(text, " "), " ").Trim();
    }

    internal static int? ExtractStrongEpisode(string text)
        => FirstNumber(Prepare(text), StrongEpisodes);

    internal static int? ExtractEpisode(string text)
    {
        text = Prepare(text);
        if (ExtractStrongEpisode(text) is { } strong) return strong;
        if (HalfEpisode().IsMatch(text)) return null;
        if (FirstNumber(text, WeakEpisodes) is { } episode) return episode;
        // Cleo-style Toradora_-_01 names need the same cleanup as title extraction.
        var dash = DashEpisode().Match(Sanitize(text));
        return dash.Success ? ParseNumber(dash.Groups[1].Value) : null;
    }

    /// A folder holds a whole season, so a trailing number is part of its name.
    internal static string? FolderTitle(string folder)
    {
        var title = ExtractTitle(folder, keepTrailingNumber: true);
        return IsJunkTitle(title) ? null : title;
    }

    internal static string ExtractTitle(string text, bool keepTrailingNumber = false)
    {
        var hasOva = Ova().IsMatch(text.ToLowerInvariant());
        var clean = TrailingVersion().Replace(Sanitize(HalfEpisodeTitle().Replace(EpisodeRange().Replace(text, ""), "$1")), "");
        if (IsSceneName(text))
            clean = SceneGroup().Replace(clean, "");
        if (SceneTail().Match(clean) is { Success: true } scene && IsSceneName(text))
            clean = scene.Groups[1].Value;

        var seasonTail = SeasonThenTail().Match(clean);
        if (seasonTail.Success && RipTail().IsMatch(seasonTail.Groups[2].Value))
            clean = seasonTail.Groups[1].Value;
        clean = TrailingSeasonWord().Replace(TrailingSeasonCode().Replace(clean, ""), "");

        // "Episode of <name>" titles are films, not numbered TV episodes.
        var film = EpisodeOfFilm().Match(clean);
        string title;
        if (film.Success)
            title = $"{film.Groups[1].Value} - {film.Groups[2].Value}";
        else
        {
            Regex[] patterns = [TitleBeforeSxxEyy(), TitleBeforeNxM(), TitleBeforeEpisodeCode(),
                TitleBeforeJapaneseEpisode(), TitleBeforeHashEpisode(), TitleBeforeBroadcastNumber()];
            title = FirstGroup(clean, keepTrailingNumber ? patterns : [.. patterns, TitleBeforeTrailingNumber()]) ?? clean;
            if (title.Length == 0 && TitleAfterEpisodeCode().Match(clean) is { Success: true } after)
                title = after.Groups[1].Value;
        }

        title = TrimTitle(title);
        if (hasOva && title.Length > 0 && !Ova().IsMatch(title.ToLowerInvariant()))
            title += " OVA";
        return title;
    }

    internal static int? SeasonOf(string text)
        => FirstNumber(Resolution().Replace(text, ""), Seasons);

    private static int? YearOf(string text)
    {
        var match = YearTag().Match(text);
        if (!match.Success) match = TrailingYear().Match(Sanitize(text));
        if (!match.Success && IsSceneName(text)) match = SceneYear().Match(Sanitize(text));
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static int? RomanSeason(string title)
    {
        var index = Array.FindIndex(RomanSeasons, r => title.EndsWith(" " + r, StringComparison.Ordinal));
        return index < 0 ? null : index + 2;
    }

    private static string SeriesTitleOf(string title)
    {
        foreach (var suffix in SeasonSuffixes)
        {
            var match = suffix.Match(title);
            if (match.Success && match.Groups[1].Value.Length > 0)
                return match.Groups[1].Value;
        }
        return title;
    }

    private static string TrimTitle(string title)
    {
        title = TrailingEpisodeWord().Replace(title, "");
        title = TrailingEpCode().Replace(title, "");
        title = TrailingDash().Replace(title, "");
        return title.Trim();
    }

    /// Dotted scene names put the year or season right after the title and only tags behind it.
    private static bool IsSceneName(string text)
        => !text.Contains(' ') && text.Count(c => c == '.') >= 3;

    private static string Prepare(string text)
        => Crc().Replace(Resolution().Replace(text, ""), "");

    private static string? FirstGroup(string text, params Regex[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = pattern.Match(text);
            if (match.Success) return match.Groups[1].Value;
        }
        return null;
    }

    private static int? FirstNumber(string text, Regex[] patterns)
        => FirstGroup(text, patterns) is { } number ? ParseNumber(number) : null;

    private static int? ParseNumber(string digits)
    {
        var value = 0;
        foreach (var c in digits)
        {
            var digit = (int)char.GetNumericValue(c);
            if (digit < 0 || value > 100_000) return null;
            value = value * 10 + digit;
        }
        return value;
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*://")]
    private static partial Regex UrlScheme();

    [GeneratedRegex(@"[._~]")]
    private static partial Regex SeparatorChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\d{3,}[xX]\d{3,}")]
    private static partial Regex Resolution();

    [GeneratedRegex(@"\[[0-9A-Fa-f]{8}\]")]
    private static partial Regex Crc();

    [GeneratedRegex(@" - (\d+)[vV]?\d*(?![\d\p{L}])")]
    private static partial Regex DashEpisode();

    [GeneratedRegex(@" - \d+\.\d(?![\d])")]
    private static partial Regex HalfEpisode();

    [GeneratedRegex(@"( - \d+)\.\d(?!\d)")]
    private static partial Regex HalfEpisodeTitle();

    [GeneratedRegex(@"\[(\d+)[vV]?\d*\]")]
    private static partial Regex BracketEpisode();

    [GeneratedRegex(@"\[\d+[vV]?\d*\]")]
    private static partial Regex BracketEpisodeTag();

    [GeneratedRegex(@"[\(\[]((?:19|20)\d\d)[\)\]]")]
    private static partial Regex YearTag();

    [GeneratedRegex(@"\s((?:19|20)\d\d)$")]
    private static partial Regex TrailingYear();

    [GeneratedRegex(@"\s((?:19|20)\d\d)(?:\s|$)")]
    private static partial Regex SceneYear();

    [GeneratedRegex(@"^(.+?)\s(?:(?:19|20)\d\d|[Ss]\d+)(?:\s|$)")]
    private static partial Regex SceneTail();

    [GeneratedRegex(@"\s+-?\s*\d{1,4}\s*[~～]\s*\d{1,4}(?!\d)")]
    private static partial Regex EpisodeRange();

    [GeneratedRegex(@"(?<![a-z])ova(?![a-z])")]
    private static partial Regex Ova();

    [GeneratedRegex(@"\s+[vV]\d+\s*$")]
    private static partial Regex TrailingVersion();

    [GeneratedRegex(@"^(.*?)\s+[Ss]\d+\s+(.+)$")]
    private static partial Regex SeasonThenTail();

    [GeneratedRegex(@"^(?:bdrip|brrip)(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex RipTail();

    [GeneratedRegex(@"\s+[Ss]\d+\s*$")]
    private static partial Regex TrailingSeasonCode();

    [GeneratedRegex(@"\s-[A-Za-z0-9]+$")]
    private static partial Regex SceneGroup();

    [GeneratedRegex(@"(?<!\d(?:st|nd|rd|th))\s+[Ss]eason\s*\d*\s*$")]
    private static partial Regex TrailingSeasonWord();

    [GeneratedRegex(@"^(.*?[Ee]pisode\s+[Oo]f.*?)\s+\d+[vV]?\d*\s*-\s*(.+)$")]
    private static partial Regex EpisodeOfFilm();

    /// A year between the title and SxxEyy tells remakes apart; it is not part of the title.
    [GeneratedRegex(@"^(.*?)(?:\s+(?:19|20)\d\d)?\s*[Ss]\d+[Ee][Pp]?\d+")]
    private static partial Regex TitleBeforeSxxEyy();

    [GeneratedRegex(@"^(.*?)\s*-?\s*(?<![A-Za-z0-9])\d+[xX]\d+")]
    private static partial Regex TitleBeforeNxM();

    [GeneratedRegex(@"^(.*?)\s*-?\s*(?<![A-Za-z0-9])[Ee][Pp]?\.?\d+(?!\d)")]
    private static partial Regex TitleBeforeEpisodeCode();

    [GeneratedRegex(@"^(.*?)\s*第[0-9０-９]+話")]
    private static partial Regex TitleBeforeJapaneseEpisode();

    [GeneratedRegex(@"^(.*?)\s*(?<!\S)[#＃][0-9０-９]+(?![0-9０-９])")]
    private static partial Regex TitleBeforeHashEpisode();

    /// Japanese broadcast recordings number episodes as （０８）.
    [GeneratedRegex(@"^(.*?)\s*（[0-9０-９]{1,3}）")]
    private static partial Regex TitleBeforeBroadcastNumber();

    [GeneratedRegex(@"^[Ee][Pp]?\.?\s*\d+\s*-?\s*(.+)$")]
    private static partial Regex TitleAfterEpisodeCode();

    /// A 【】 block right after a title is a channel or broadcast note; one at the start can be the title.
    [GeneratedRegex(@"(?<=[^\s\]\)】]\s*)【[^】]*】")]
    private static partial Regex LaterLenticular();

    [GeneratedRegex(@"^(.*?)\s*-?\s*\d+[vV]?\d*\s*$")]
    private static partial Regex TitleBeforeTrailingNumber();

    [GeneratedRegex(@"\s*[-:]?\s*[Ee]pisode\s*$")]
    private static partial Regex TrailingEpisodeWord();

    [GeneratedRegex(@"\s*[-:]?\s*[Ee][Pp]\.?\s*$")]
    private static partial Regex TrailingEpCode();

    [GeneratedRegex(@"\s*-+\s*$")]
    private static partial Regex TrailingDash();

    [GeneratedRegex(@"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
    private static partial Regex Uuid();

    [GeneratedRegex(@"^[0-9A-Fa-f]+$")]
    private static partial Regex HexOnly();

    [GeneratedRegex(@"\p{L}{3}")]
    private static partial Regex ThreeLetters();

    /// Japanese titles can be a single character.
    [GeneratedRegex(@"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]")]
    private static partial Regex JapaneseScript();
}
