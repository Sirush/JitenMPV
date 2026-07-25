using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Interaction;

public sealed record DeckMembershipRow(StudyDeckType Type, string Label, string Names);

public static class DeckMembership
{
    /// Word lists first: the only type the user adds to directly.
    private static readonly StudyDeckType[] DisplayOrder =
        [StudyDeckType.StaticWordList, StudyDeckType.MediaDeck, StudyDeckType.GlobalDynamic];

    private static string LabelFor(StudyDeckType type) => type switch
    {
        StudyDeckType.StaticWordList => "Word list",
        StudyDeckType.MediaDeck => "Media deck",
        StudyDeckType.GlobalDynamic => "Freq deck",
        _ => "Deck"
    };

    /// One row per deck type the word belongs to. Ids with no matching deck are skipped, so an
    /// unloaded or stale deck list degrades to fewer rows rather than to blank ones.
    public static IReadOnlyList<DeckMembershipRow> Build(
        IReadOnlyList<int> studyDeckIds, IReadOnlyList<StudyDeckListItem> decks)
    {
        if (studyDeckIds.Count == 0 || decks.Count == 0) return [];

        var groups = new Dictionary<StudyDeckType, List<StudyDeckListItem>>();
        foreach (var id in studyDeckIds)
        {
            var deck = decks.FirstOrDefault(d => d.UserStudyDeckId == id);
            if (deck is null) continue;

            if (!groups.TryGetValue(deck.DeckType, out var list))
                groups[deck.DeckType] = list = [];
            list.Add(deck);
        }

        List<DeckMembershipRow> rows = [];
        foreach (var type in DisplayOrder)
        {
            if (!groups.TryGetValue(type, out var group)) continue;

            var label = group.Count > 1 ? $"{LabelFor(type)} x{group.Count}" : LabelFor(type);
            var names = string.Join(", ",
                group.Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)));

            rows.Add(new DeckMembershipRow(type, label, names));
        }

        return rows;
    }
}
