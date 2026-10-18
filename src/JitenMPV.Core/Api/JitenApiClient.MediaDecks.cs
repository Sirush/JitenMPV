using System.Net.Http.Json;
using System.Text.RegularExpressions;
using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Api;

public interface IMediaDeckApi
{
    /// Anime, drama and movie decks.
    Task<IReadOnlyList<MediaDeck>> SearchVideoAsync(string title, CancellationToken ct);

    Task<MediaDeck?> GetDeckAsync(int deckId, CancellationToken ct);

    Task<DeckEpisodes> GetEpisodesAsync(int deckId, CancellationToken ct);

    Task<DeckStatus> GetDeckStatusAsync(int deckId, CancellationToken ct);

    /// Writes only when the status differs, then reads it back. False means it already matched.
    Task<bool> SetDeckStatusAsync(int deckId, DeckStatus status, CancellationToken ct);
}

public sealed partial class JitenApiClient : IMediaDeckApi
{
    private const int MaxDetailPages = 200;

    /// One search covers all three types: mediaType takes a single value, but books, games and
    /// the rest can be excluded.
    private const string NonVideoMediaTypes = "4,5,6,7,8,9,10,11";

    public async Task<IReadOnlyList<MediaDeck>> SearchVideoAsync(string title, CancellationToken ct)
    {
        var path = $"/api/media-deck/get-media-decks?titleFilter={Uri.EscapeDataString(title)}"
                   + $"&excludeMediaTypes={NonVideoMediaTypes}&offset=0";
        var page = await GetAsync<PagedResponse<List<MediaDeck>>>(path, "SearchMediaDecks", ct);
        return [.. (page?.Data ?? [])
            .Where(d => d.DeckId > 0 && d.MediaType is MediaDeck.Anime or MediaDeck.Drama or MediaDeck.Movie)];
    }

    public async Task<MediaDeck?> GetDeckAsync(int deckId, CancellationToken ct)
    {
        var page = await GetAsync<PagedResponse<MediaDeckDetail>>(
            $"/api/media-deck/{deckId}/detail?offset=0", "MediaDeckDetail", ct);
        return page?.Data?.MainDeck is { } deck && deck.DeckId == deckId ? deck : null;
    }

    /// Refuses a response whose paging or parentage does not add up: the episode list decides
    /// which decks get written, so a half-read one must never pass for whole.
    public async Task<DeckEpisodes> GetEpisodesAsync(int deckId, CancellationToken ct)
    {
        var episodes = new List<EpisodeDeck>();
        var seen = new HashSet<int>();
        var numbers = new HashSet<int>();
        var codes = new HashSet<(int, int)>();
        MediaDeck? main = null;
        var offset = 0;

        for (var pageIndex = 0; ; pageIndex++)
        {
            if (pageIndex >= MaxDetailPages)
                throw new InvalidDataException($"Deck {deckId} has more episode pages than expected");

            var page = await GetAsync<PagedResponse<MediaDeckDetail>>(
                $"/api/media-deck/{deckId}/detail?offset={offset}", "MediaDeckDetail", ct);
            var rows = page?.Data?.SubDecks;
            if (page is null || rows is null || page.PageSize <= 0 || page.TotalItems < 0
                || page.CurrentOffset != offset || rows.Count > page.PageSize
                || (offset < page.TotalItems && rows.Count == 0))
                throw new InvalidDataException($"Invalid episode page for deck {deckId} at offset {offset}");

            main ??= page.Data?.MainDeck;
            foreach (var row in rows)
            {
                if (row.DeckId <= 0 || !seen.Add(row.DeckId) || row.ParentDeckId != deckId)
                    throw new InvalidDataException($"Unexpected episode deck {row.DeckId} under deck {deckId}");

                var number = EpisodeNumber(row.OriginalTitle);
                if (number is { } n && !numbers.Add(n))
                    throw new InvalidDataException($"Deck {deckId} has two decks titled Episode {n}");

                var code = SeasonEpisode().Match(row.OriginalTitle ?? "");
                (int, int)? seasonEpisode = code.Success
                    ? (int.Parse(code.Groups[1].Value), int.Parse(code.Groups[2].Value))
                    : null;
                if (seasonEpisode is { } se && !codes.Add(se))
                    throw new InvalidDataException($"Deck {deckId} has two decks titled {row.OriginalTitle}");

                episodes.Add(new EpisodeDeck(row.DeckId, number, ToStatus(row.Status),
                    seasonEpisode?.Item1, seasonEpisode?.Item2));
            }

            offset += rows.Count;
            if (offset >= page.TotalItems) break;
        }

        if (main is null || main.DeckId != deckId)
            throw new InvalidDataException($"Deck {deckId} detail did not describe deck {deckId}");

        // Dramas that title their episodes S01E01 instead are numbered on through the seasons.
        if (numbers.Count == 0)
        {
            var order = episodes.Where(e => e.Season is not null)
                .OrderBy(e => e.Season).ThenBy(e => e.NumberInSeason).Select(e => e.DeckId).ToList();
            episodes = [.. episodes.Select(e => e.Season is null
                ? e
                : e with { Number = order.IndexOf(e.DeckId) + 1 })];
        }
        return new DeckEpisodes(main, episodes);
    }

    public async Task<DeckStatus> GetDeckStatusAsync(int deckId, CancellationToken ct)
    {
        var preference = await GetAsync<DeckPreference>(
            $"/api/user/deck-preferences/{deckId}", "DeckPreference", ct);
        if (preference is null || !Enum.IsDefined(typeof(DeckStatus), preference.Status))
            throw new InvalidDataException($"Invalid preference for deck {deckId}");
        return (DeckStatus)preference.Status;
    }

    public async Task<bool> SetDeckStatusAsync(int deckId, DeckStatus status, CancellationToken ct)
    {
        if (await GetDeckStatusAsync(deckId, ct) == status) return false;

        var request = new SetDeckStatusRequest { Status = (int)status };
        using (var response = await SendWithRetryAsync(
                   (http, token) => http.PostAsJsonAsync(
                       $"/api/user/deck-preferences/{deckId}/status", request, JsonOptions, token),
                   "SetDeckStatus", ct))
        {
            await EnsureSuccessAsync(response, "SetDeckStatus", ct);
        }

        var readBack = await GetDeckStatusAsync(deckId, ct);
        if (readBack != status)
            throw new InvalidDataException($"Deck {deckId} reads back as {readBack} after setting {status}");
        return true;
    }

    /// The API has no episode-number field, so only an exact "Episode N" title counts; list order
    /// and numbers inside arbitrary titles are never trusted.
    public static int? EpisodeNumber(string? title)
    {
        var match = EpisodeTitle().Match(title ?? "");
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : null;
    }

    private static DeckStatus ToStatus(int? status)
        => status is { } s && Enum.IsDefined(typeof(DeckStatus), s) ? (DeckStatus)s : DeckStatus.None;

    [GeneratedRegex(@"^[Ee]pisode\s+(\d+)$")]
    private static partial Regex EpisodeTitle();

    [GeneratedRegex(@"^[Ss](\d+)\s*[Ee](\d+)$")]
    private static partial Regex SeasonEpisode();
}
