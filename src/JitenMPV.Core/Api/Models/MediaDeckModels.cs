using System.Text.Json.Serialization;

namespace JitenMPV.Core.Api.Models;

public enum DeckStatus
{
    None = 0,
    Planning = 1,
    Ongoing = 2,
    Completed = 3,
    Dropped = 4
}

public sealed class MediaDeck
{
    public const int Anime = 1;
    public const int Drama = 2;
    public const int Movie = 3;

    [JsonPropertyName("deckId")]
    public int DeckId { get; set; }

    [JsonPropertyName("originalTitle")]
    public string? OriginalTitle { get; set; }

    [JsonPropertyName("romajiTitle")]
    public string? RomajiTitle { get; set; }

    [JsonPropertyName("englishTitle")]
    public string? EnglishTitle { get; set; }

    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    [JsonPropertyName("childrenDeckCount")]
    public int ChildrenDeckCount { get; set; }

    [JsonPropertyName("relationships")]
    public List<MediaDeckRelationship>? Relationships { get; set; }

    [JsonPropertyName("links")]
    public List<MediaDeckLink>? Links { get; set; }

    [JsonPropertyName("coverName")]
    public string? CoverName { get; set; }

    [JsonPropertyName("mediaType")]
    public int MediaType { get; set; }

    [JsonPropertyName("parentDeckId")]
    public int? ParentDeckId { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    /// Only sent on authenticated requests; null until the user sets one.
    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonIgnore]
    public string DisplayTitle => RomajiTitle ?? EnglishTitle ?? OriginalTitle ?? $"Deck {DeckId}";

    [JsonIgnore]
    public int? ReleaseYear
        => ReleaseDate is { Length: >= 4 } date && int.TryParse(date[..4], out var year) ? year : null;

    [JsonIgnore]
    public IEnumerable<string> Titles
        => new[] { RomajiTitle, EnglishTitle, OriginalTitle }.OfType<string>().Where(t => t.Trim().Length > 0);

    /// The id at the end of the deck's first link of this type, such as an AniList anime id.
    public int? LinkId(int linkType)
        => Links?.Where(l => l.LinkType == linkType)
            .Select(l => l.Url?.TrimEnd('/').Split('/')[^1])
            .Select(id => int.TryParse(id, out var n) ? n : (int?)null)
            .FirstOrDefault(n => n is not null);
}

public sealed class MediaDeckLink
{
    /// A TMDB id is only unique together with the /tv/ or /movie/ in its link.
    public const int Tmdb = 3;
    public const int AniList = 4;
    public const int Mal = 5;

    [JsonPropertyName("linkType")]
    public int LinkType { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class MediaDeckRelationship
{
    /// Seen from the deck that carries the relationship: Sequel (1, not inverse) points at the
    /// deck this one follows, its inverse (101) at the deck that follows this one.
    public const int SequelOf = 1;
    public const int PrequelOf = 101;

    [JsonPropertyName("targetDeckId")]
    public int TargetDeckId { get; set; }

    [JsonPropertyName("targetDeck")]
    public MediaDeckSummary? TargetDeck { get; set; }

    [JsonPropertyName("relationshipType")]
    public int RelationshipType { get; set; }

    [JsonPropertyName("isInverse")]
    public bool IsInverse { get; set; }
}

public sealed class MediaDeckSummary
{
    [JsonPropertyName("deckId")]
    public int DeckId { get; set; }

    [JsonPropertyName("mediaType")]
    public int MediaType { get; set; }
}

public sealed class PagedResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("totalItems")]
    public int TotalItems { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("currentOffset")]
    public int CurrentOffset { get; set; }
}

public sealed class MediaDeckDetail
{
    [JsonPropertyName("mainDeck")]
    public MediaDeck? MainDeck { get; set; }

    [JsonPropertyName("subDecks")]
    public List<MediaDeck>? SubDecks { get; set; }
}

public sealed class DeckPreference
{
    [JsonPropertyName("status")]
    public int Status { get; set; }
}

public sealed class SetDeckStatusRequest
{
    [JsonPropertyName("status")]
    public int Status { get; init; }
}

/// <param name="Season">Only for episodes titled S01E01, with <paramref name="NumberInSeason"/>.</param>
public sealed record EpisodeDeck(
    int DeckId, int? Number, DeckStatus Status, int? Season = null, int? NumberInSeason = null);

public sealed record DeckEpisodes(MediaDeck Deck, IReadOnlyList<EpisodeDeck> Episodes)
{
    public EpisodeDeck? Find(int number) => Episodes.SingleOrDefault(e => e.Number == number);

    public EpisodeDeck? Find(int season, int number)
        => Episodes.SingleOrDefault(e => e.Season == season && e.NumberInSeason == number);

    public int? LastNumber => Episodes.Max(e => e.Number);
}
