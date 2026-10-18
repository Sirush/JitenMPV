using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Tracking;

public sealed record EpisodePickerData(
    string FileTitle,
    int? Episode,
    int? CurrentDeckId,
    IReadOnlyList<DeckCandidate> Candidates,
    Func<string, CancellationToken, Task<IReadOnlyList<DeckCandidate>>> Search);

/// <param name="Episode">The deck's episode this file is; null keeps the detected number.</param>
public sealed record EpisodePickerResult(MediaDeck? Deck, int? Episode, bool IgnoreFolder);

public interface IEpisodePickerPresenter
{
    /// Returns null when the user closes the picker without choosing.
    Task<EpisodePickerResult?> ShowAsync(EpisodePickerData data, CancellationToken ct);
}
