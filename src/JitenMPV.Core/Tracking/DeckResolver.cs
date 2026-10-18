using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.MediaInfo;

namespace JitenMPV.Core.Tracking;

public enum DeckMatchKind { Matched, Ambiguous, NotFound, Ignored }

public sealed record DeckCandidate(MediaDeck Deck, double Score);

/// <param name="Episode">Episode within <paramref name="Deck"/>; null for a movie or when the file
/// carries no number.</param>
public sealed record DeckMatch(
    DeckMatchKind Kind,
    MediaDeck? Deck,
    int? Episode,
    IReadOnlyList<DeckCandidate> Candidates)
{
    public bool IsMovie => Deck is { ChildrenDeckCount: 0 } or { MediaType: MediaDeck.Movie };
}

public sealed partial class DeckResolver(IMediaDeckApi api, MediaMap map)
{
    private const double MatchScore = 0.9;
    private const double MatchMargin = 0.08;
    private const double AliasWeight = 0.95;
    private const int MaxSearches = 4;
    private const int MaxCandidates = 10;
    private const int MaxSeasonWalk = 30;

    public async Task<DeckMatch> ResolveAsync(MediaIdentity identity, CancellationToken ct)
    {
        var key = MediaMap.KeyFor(identity);
        if (key is not null && map.Get(key) is { } entry)
        {
            if (entry.Ignore)
                return new DeckMatch(DeckMatchKind.Ignored, null, null, []);
            if (entry.DeckId is { } id && await api.GetDeckAsync(id, ct) is { } mapped)
            {
                int? episode = mapped.ChildrenDeckCount == 0 ? null : (identity.Episode ?? 0) + entry.EpisodeOffset;
                return new DeckMatch(DeckMatchKind.Matched, mapped, episode is > 0 ? episode : null, []);
            }
        }

        var queries = Queries(identity);
        var pool = new Dictionary<int, MediaDeck>();
        foreach (var query in SearchQueries(queries).Take(MaxSearches))
        {
            foreach (var deck in await api.SearchVideoAsync(query, ct))
                pool.TryAdd(deck.DeckId, deck);
            // Only the file's own title stops the search: a folder match alone would hide its movie or OVA.
            if (pool.Values.Any(d => SeriesScore(d, queries[..1]) >= MatchScore)) break;
        }

        var candidates = pool.Values
            .Select(d => new DeckCandidate(d, Math.Max(SeriesScore(d, queries), TitleScore(d, queries))))
            .OrderByDescending(c => c.Score)
            .Take(MaxCandidates)
            .ToList();

        var named = await NamedDeckAsync(identity, pool.Values, ct);
        if (named is { ChildrenDeckCount: 0 })
            return new DeckMatch(DeckMatchKind.Matched, named, null, candidates);

        // A numbered episode cannot be a movie.
        var eligible = pool.Values
            .Where(d => identity.Episode is null || d is { ChildrenDeckCount: > 0, MediaType: not MediaDeck.Movie })
            .ToList();
        var season = identity.Season ?? 1;
        var seasonDeck = named ?? Pick(eligible.Where(d => SeasonOf(d) == season), queries, identity);
        // A drama keeps all its seasons in one deck.
        if (seasonDeck is null && season > 1
            && Pick(eligible.Where(d => SeasonOf(d) == 1), queries, identity) is { } first)
        {
            var dramas = eligible.Where(d => d.MediaType == MediaDeck.Drama && SeasonOf(d) == 1);
            seasonDeck = await WalkSequelsAsync(first, season - 1, ct) ?? Pick(dramas, queries, identity);
        }

        if (seasonDeck is null)
        {
            var kind = candidates.Count == 0 ? DeckMatchKind.NotFound : DeckMatchKind.Ambiguous;
            return new DeckMatch(kind, null, null, candidates);
        }

        return await PlaceEpisodeAsync(seasonDeck, identity, candidates, ct);
    }

    public async Task<IReadOnlyList<DeckCandidate>> SearchAsync(string query, CancellationToken ct)
    {
        var decks = await api.SearchVideoAsync(query, ct);
        return [.. decks.Select(d => new DeckCandidate(d, TitleScore(d, [query])))
            .OrderByDescending(c => c.Score).Take(MaxCandidates)];
    }

    /// Numbering that runs on from earlier seasons loses their episode counts; numbering past the
    /// end of the deck walks on into the following seasons.
    private async Task<DeckMatch> PlaceEpisodeAsync(
        MediaDeck deck, MediaIdentity identity, List<DeckCandidate> candidates, CancellationToken ct)
    {
        DeckMatch Matched(MediaDeck d, int? episode) => new(DeckMatchKind.Matched, d, episode, candidates);
        DeckMatch Lost() => new(DeckMatchKind.Ambiguous, null, null, candidates);

        if (deck.ChildrenDeckCount == 0)
            return identity.Episode is null or 1 ? Matched(deck, null) : Lost();
        if (identity.Episode is not { } episode)
            return Matched(deck, null);
        // Only S01E01-style episode titles say where a later season starts.
        if (deck.MediaType == MediaDeck.Drama && identity.Season is > 1 and var season && SeasonOf(deck) < season)
        {
            var episodes = await api.GetEpisodesAsync(deck.DeckId, ct);
            return Matched(deck, episodes.Find(season, episode)?.Number);
        }
        if (episode <= deck.ChildrenDeckCount)
            return Matched(deck, episode);

        if ((identity.Season ?? 1) > 1)
        {
            var earlier = (await PrequelsAsync(deck, ct)).Sum(d => d.ChildrenDeckCount);
            var within = episode - earlier;
            return earlier > 0 && within >= 1 && within <= deck.ChildrenDeckCount ? Matched(deck, within) : Lost();
        }

        var current = deck;
        var remaining = episode;
        while (remaining > current.ChildrenDeckCount)
        {
            remaining -= current.ChildrenDeckCount;
            if (await WalkSequelsAsync(current, 1, ct) is not { } next) return Lost();
            // A stated season numbers within itself and only runs on into a split cour of it.
            if (identity.Season is not null && !IsSplitCour(next)) return Lost();
            current = next;
        }
        return Matched(current, remaining);
    }

    /// Follows the sequel chain through TV seasons only; a step with no single TV sequel stops it,
    /// since guessing between two would write to the wrong show.
    private async Task<MediaDeck?> WalkSequelsAsync(MediaDeck deck, int steps, CancellationToken ct)
    {
        var current = deck;
        for (var i = 0; i < steps && i < MaxSeasonWalk; i++)
        {
            var sequels = new List<MediaDeck>();
            foreach (var rel in current.Relationships ?? [])
            {
                if (rel.RelationshipType != MediaDeckRelationship.PrequelOf
                    || rel.TargetDeck?.MediaType != current.MediaType)
                    continue;
                if (await api.GetDeckAsync(rel.TargetDeckId, ct) is { ChildrenDeckCount: > 0 } sequel)
                    sequels.Add(sequel);
            }
            if (sequels.Count != 1) return null;
            current = sequels[0];
        }
        return current;
    }

    /// A deck the file names outright wins, since some sequels carry no season marker; a stated
    /// season must still match its place in the sequel chain.
    private async Task<MediaDeck?> NamedDeckAsync(MediaIdentity identity, IEnumerable<MediaDeck> pool, CancellationToken ct)
    {
        if (identity.Title is not { } title || identity.Season == 0) return null;
        var name = Normalize(title);
        var anyDeck = identity.Episode is null || SingleVideoTitle().IsMatch(title);
        var decks = pool.Where(d => anyDeck || d.ChildrenDeckCount > 0).ToList();
        var named = decks.Where(d => d.Titles.Any(t => Normalize(YearSuffix().Replace(t, "")) == name)).ToList();
        if (identity.Season is not { } season)
        {
            var series = decks.Count(d => d.Titles.Any(t => Normalize(StripSeason(t)) == name));
            return named.Count == 1 && series == 1 ? named[0] : null;
        }

        var placed = new List<MediaDeck>();
        foreach (var deck in named)
        {
            if (SeasonOf(deck) == season
                || (await PrequelsAsync(deck, ct)).Count(d => !IsSplitCour(d)) + 1 == season)
                placed.Add(deck);
        }
        return placed.Count == 1 ? placed[0] : null;
    }

    private async Task<List<MediaDeck>> PrequelsAsync(MediaDeck deck, CancellationToken ct)
    {
        var chain = new List<MediaDeck>();
        var current = deck;
        for (var i = 0; i < MaxSeasonWalk; i++)
        {
            var prequels = new List<MediaDeck>();
            foreach (var rel in current.Relationships ?? [])
            {
                if (rel.RelationshipType != MediaDeckRelationship.SequelOf || rel.IsInverse
                    || rel.TargetDeck?.MediaType != current.MediaType)
                    continue;
                if (await api.GetDeckAsync(rel.TargetDeckId, ct) is { ChildrenDeckCount: > 0 } prequel)
                    prequels.Add(prequel);
            }
            if (prequels.Count != 1) break;
            current = prequels[0];
            chain.Add(current);
        }
        return chain;
    }

    /// The best deck when it clearly beats the rest. A release year breaks a tie between remakes,
    /// an anime wins one with its live-action adaptation, and a file without an episode number
    /// picks the film over the drama.
    private static MediaDeck? Pick(IEnumerable<MediaDeck> decks, IReadOnlyList<string> queries, MediaIdentity identity)
    {
        var ranked = decks
            .Select(d => (Deck: d, Score: SeriesScore(d, queries)))
            .Where(x => x.Score >= MatchScore)
            .OrderByDescending(x => x.Score)
            .ToList();
        if (ranked.Count == 0) return null;
        if (ranked.Count(x => x.Score >= 1) == 1 && ranked[0].Score >= 1) return ranked[0].Deck;

        var close = ranked.Where(x => ranked[0].Score - x.Score < MatchMargin).ToList();
        if (close.Count == 1) return close[0].Deck;

        var sameYear = identity.Year is not { } year ? [] : close.Where(x => x.Deck.ReleaseYear == year).ToList();
        if (sameYear.Count == 1) return sameYear[0].Deck;

        var anime = close.Where(x => x.Deck.MediaType == MediaDeck.Anime).ToList();
        if (anime.Count == 1) return anime[0].Deck;

        var films = identity.Episode is null ? close.Where(x => x.Deck.MediaType == MediaDeck.Movie).ToList() : [];
        return films.Count == 1 ? films[0].Deck : null;
    }

    private static List<string> Queries(MediaIdentity identity)
    {
        var queries = new List<string>();
        foreach (var title in new[] { identity.SeriesTitle, identity.Title, identity.FolderTitle }
                     .Concat(identity.TitleCandidates))
        {
            if (!string.IsNullOrWhiteSpace(title)
                && !queries.Contains(title, StringComparer.OrdinalIgnoreCase))
                queries.Add(title);
        }
        return queries;
    }

    /// Jiten's search matches substrings, so "Title - Subtitle" also searches by the main title.
    private static IEnumerable<string> SearchQueries(List<string> queries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in queries)
        {
            if (seen.Add(query)) yield return query;
            var cut = query.IndexOf(" - ", StringComparison.Ordinal);
            if (cut >= 3 && seen.Add(query[..cut])) yield return query[..cut];
        }
    }

    private static bool IsSplitCour(MediaDeck deck) => deck.Titles.Any(t => CourMarker().IsMatch(t));

    private static double SeriesScore(MediaDeck deck, IReadOnlyList<string> queries)
        => Best(deck, queries, StripSeason);

    private static double TitleScore(MediaDeck deck, IReadOnlyList<string> queries)
        => Best(deck, queries, t => t);

    private static double Best(MediaDeck deck, IReadOnlyList<string> queries, Func<string, string> shape)
    {
        var best = 0.0;
        foreach (var query in queries)
        {
            var q = Normalize(StripSeason(query));
            if (q.Length == 0) continue;
            foreach (var title in deck.Titles)
                best = Math.Max(best, Similarity(q, Normalize(shape(title))));
            // Aliases rank under the deck's own titles: a commercial can carry a film's name as one.
            foreach (var alias in deck.Aliases ?? [])
                best = Math.Max(best, AliasWeight * Similarity(q, Normalize(shape(alias))));
        }
        return best;
    }

    private static int SeasonOf(MediaDeck deck)
    {
        foreach (var title in new[] { deck.RomajiTitle, deck.EnglishTitle, deck.OriginalTitle })
        {
            var match = SeasonMarker().Match(title ?? "");
            if (!match.Success) continue;
            var digits = match.Groups.Values.Skip(1).First(g => g.Success).Value;
            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var season))
                return season;
            return (int)char.GetNumericValue(digits[0]);
        }
        return 1;
    }

    /// A remake's year goes too, so it ties with the original instead of losing to it.
    private static string StripSeason(string title)
    {
        title = YearSuffix().Replace(title, "");
        var match = SeasonMarker().Match(title);
        return match.Success ? title[..match.Index].TrimEnd(' ', ':', '-') : title;
    }

    private static string Normalize(string text)
    {
        var decomposed = text.Replace("×", " x ").Replace("'", "").Replace("’", "").Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        // Romanisations disagree on long vowels: Sōsō, Sousou and Soso are one title.
        var folded = Whitespace().Replace(sb.ToString(), " ").Trim()
            .Replace("ou", "o").Replace("uu", "u").Replace("oo", "o");
        return folded;
    }

    /// Dice coefficient over character bigrams.
    private static double Similarity(string a, string b)
    {
        if (a == b) return 1;
        if (a.Length < 2 || b.Length < 2) return 0;

        var counts = new Dictionary<(char, char), int>();
        for (var i = 0; i < a.Length - 1; i++)
            counts[(a[i], a[i + 1])] = counts.GetValueOrDefault((a[i], a[i + 1])) + 1;

        var shared = 0;
        for (var i = 0; i < b.Length - 1; i++)
        {
            var pair = (b[i], b[i + 1]);
            if (counts.GetValueOrDefault(pair) is > 0 and var n)
            {
                counts[pair] = n - 1;
                shared++;
            }
        }

        // Kept below an exact match so a near miss never ties with the real title.
        return Math.Min(0.99, 2.0 * shared / (a.Length - 1 + b.Length - 1));
    }

    [GeneratedRegex(@"\s*(?:\b(\d+)(?:st|nd|rd|th)\s+season\b|\bseason\s*(\d+)\b|\bs(\d+)$|第([0-9０-９]+)期)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SeasonMarker();

    [GeneratedRegex(@"\bpart\s*\d+\b|\bcour\s*\d+\b|\b\d+(?:st|nd|rd|th)\s+cour\b|第[0-9０-９]+クール", RegexOptions.IgnoreCase)]
    private static partial Regex CourMarker();

    [GeneratedRegex(@"\b(?:ova|oad|movie|film)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SingleVideoTitle();

    [GeneratedRegex(@"\s*\((?:19|20)\d\d\)$")]
    private static partial Regex YearSuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
