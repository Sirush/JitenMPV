using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JitenMPV.Core.Jimaku;

/// Jimaku's search is fuzzy and lists every season, film and special of a franchise, so a
/// plausibility check and the season decide which entry a search label means.
public static partial class JimakuEntryMatcher
{
    public static JimakuEntry? PickBest(IReadOnlyList<JimakuEntry>? results, string query, int? season,
        int? episode = null)
        => Rank(results, query, season, episode).FirstOrDefault();

    /// Plausible entries, best first; ties keep Jimaku's order.
    public static IReadOnlyList<JimakuEntry> Rank(IReadOnlyList<JimakuEntry>? results, string query, int? season,
        int? episode = null)
    {
        if (results is null || results.Count == 0) return [];

        var scored = new List<(JimakuEntry Entry, int Score, int Kind)>();
        foreach (var entry in results)
        {
            string[] texts = [entry.Name, entry.EnglishName ?? "", entry.JapaneseName ?? ""];
            if (!texts.Any(t => IsPlausibleMatch(query, t))) continue;

            var score = 0;
            if (season is >= 1)
            {
                var named = texts.Select(t => SeasonHint(Normalize(t))).FirstOrDefault(s => s is not null);
                if (named is not null) score = named == season ? 2 : 0;
                // Drama entries follow TMDB, where one entry holds every season.
                else if (season >= 2 && entry.Flags.Anime)
                    score = texts.Any(t => HasSequelMarker(Normalize(t))) ? 1 : -1;
            }
            var kind = episode is not null && entry.Flags.Movie ? -1 : 0;
            scored.Add((entry, score, kind));
        }

        return [.. scored.OrderByDescending(s => s.Score).ThenByDescending(s => s.Kind).Select(s => s.Entry)];
    }

    /// Titles compare on their letters and digits alone, with full-width forms folded.
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return NonWord().Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), " ").Trim();
    }

    public static int? SeasonHint(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var pattern in SeasonHints)
        {
            var match = pattern.Match(text);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var season))
                return season;
        }
        return null;
    }

    /// Jimaku's search can return loosely related entries of the same franchise, so one title has
    /// to contain the other.
    public static bool IsPlausibleMatch(string query, string candidate)
    {
        var q = Normalize(query);
        var t = Normalize(candidate);
        if (q.Length == 0 || t.Length == 0) return false;
        return Contains(q, t) || Contains(WithoutSeason(q), WithoutSeason(t)) || TitleScore(candidate, query) > 0;
    }

    /// One kanji or kana is contained in too many titles to mean anything.
    private static bool Contains(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        return Math.Min(a.Length, b.Length) >= 2
               && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

    /// 3 for an exact match, 2 for one equal after punctuation, 1 for a long name one romanisation
    /// letter off, spelling long vowels differently, without its bracketed part or matching the part
    /// after a colon, 0 otherwise.
    internal static int TitleScore(string title, string query)
    {
        if (string.Equals(title, query, StringComparison.OrdinalIgnoreCase)) return 3;
        var a = TitleKey(title);
        var b = TitleKey(query);
        if (a == b) return 2;
        if (b.Length < 12) return 0;

        var colon = SubtitleAfterColon().Match(title);
        if (colon.Success && TitleKey(colon.Groups[1].Value) == b) return 1;
        if (ShortVowels(a) == ShortVowels(b) || TitleKey(Bracketed().Replace(title, "")) == b) return 1;

        if (a.Length < 12 || Math.Abs(a.Length - b.Length) != 1) return 0;
        if (a.Length < b.Length) (a, b) = (b, a);
        for (var i = 0; i < a.Length; i++)
            if (string.CompareOrdinal(a, 0, b, 0, i) == 0
                && string.CompareOrdinal(a, i + 1, b, i, a.Length - i - 1) == 0)
                return 1;
        return 0;
    }

    private static string ShortVowels(string key) => LongVowel().Replace(key, "$1$2");

    private static string TitleKey(string text)
        => AsciiPunctuationOrSpace().Replace(
            EpisodeWord().Replace(text.ToLowerInvariant(), "$1").Replace("☆", ""), "");

    private static string WithoutSeason(string text) => Whitespace().Replace(SeasonWords().Replace(text, " "), " ").Trim();

    private static bool HasSequelMarker(string text)
        => StandaloneS().IsMatch(text) || StandaloneIi().IsMatch(text) || StandaloneTwo().IsMatch(text)
           || text.Contains("season 2", StringComparison.Ordinal);

    private static readonly Regex[] SeasonHints =
    [
        new(@"[Ss](\d+)[Ee]\d+"), new(@"[Ss]eason\s*(\d+)"),
        new(@"(?<![A-Za-z0-9])[Ss](\d+)(?!\d)"),
        new(@"(\d+)(?:st|nd|rd|th)\s+[Ss]eason"), new(@"(\d+)(?:st|nd|rd|th)\s+[Aa]ttack"),
        new(@"第(\d+)(?:期|シリーズ|シーズン)"), new(@"(\d+)(?:st|nd|rd|th)?\s*シーズン"), new(@"シーズン\s*(\d+)"),
    ];

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:s\d+|season\s*\d+|\d+(?:st|nd|rd|th)\s*season)(?![\p{L}\p{N}])|第\d+期")]
    private static partial Regex SeasonWords();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"([aiueo])\1|(o)(?:u|h(?![aiueo]))")]
    private static partial Regex LongVowel();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@":\s*(.+)$")]
    private static partial Regex SubtitleAfterColon();

    [GeneratedRegex(@"episode\s*(\d+)")]
    private static partial Regex EpisodeWord();

    [GeneratedRegex(@"[\p{IsBasicLatin}-[A-Za-z0-9]]")]
    private static partial Regex AsciiPunctuationOrSpace();

    [GeneratedRegex(@"(?<![A-Za-z])s(?![A-Za-z])")]
    private static partial Regex StandaloneS();

    [GeneratedRegex(@"(?<![A-Za-z])ii(?![A-Za-z])")]
    private static partial Regex StandaloneIi();

    [GeneratedRegex(@"(?<!\d)2(?!\d)")]
    private static partial Regex StandaloneTwo();
}
