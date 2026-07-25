using System.Text.Json.Serialization;

namespace JitenMPV.Core.Api.Models;

public enum StudyDeckType
{
    MediaDeck = 0,
    GlobalDynamic = 1,
    StaticWordList = 2
}

public sealed class StudyDeckListItem
{
    [JsonPropertyName("userStudyDeckId")]
    public int UserStudyDeckId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("deckType")]
    public StudyDeckType DeckType { get; set; }
}

public sealed class AddToStudyDeckRequest
{
    [JsonPropertyName("wordId")]
    public int WordId { get; init; }

    [JsonPropertyName("readingIndex")]
    public byte ReadingIndex { get; init; }

    [JsonPropertyName("occurrences")]
    public int Occurrences { get; init; } = 1;

    [JsonPropertyName("sentence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sentence { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
}

public sealed class AddExampleSentenceRequest
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
}

public sealed class LookupVocabularyRequest
{
    /// Each entry is [wordId, readingIndex]; the response is positional against this list.
    [JsonPropertyName("words")]
    public required int[][] Words { get; init; }
}

public sealed class LookupVocabularyResponse
{
    /// result[i] holds the states of words[i]; a card can hold several at once (e.g. Young + Due).
    [JsonPropertyName("result")]
    public List<List<int>>? Result { get; set; }

    /// decks[i] holds the study-deck ids words[i] belongs to.
    [JsonPropertyName("decks")]
    public List<List<int>>? Decks { get; set; }
}

public sealed record VocabularyLookup(
    int WordId,
    byte ReadingIndex,
    IReadOnlyList<KnownState> States,
    IReadOnlyList<int> DeckIds)
{
    public KnownState PrimaryState => States.Count > 0 ? States[0] : KnownState.New;
}
