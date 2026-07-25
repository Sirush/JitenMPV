using System.Text.Json.Serialization;

namespace JitenMPV.Core.Api.Models;

public sealed class ReaderParseRequest
{
    [JsonPropertyName("text")]
    public required string[] Text { get; init; }
}

public sealed class ReaderParseResponse
{
    [JsonPropertyName("tokens")]
    public List<List<ReaderToken>> Tokens { get; set; } = [];

    [JsonPropertyName("vocabulary")]
    public List<ReaderWord> Vocabulary { get; set; } = [];
}

public sealed class ReaderToken
{
    [JsonPropertyName("wordId")]
    public int WordId { get; set; }

    [JsonPropertyName("readingIndex")]
    public byte ReadingIndex { get; set; }

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }

    [JsonPropertyName("length")]
    public int Length { get; set; }

    [JsonPropertyName("conjugations")]
    public List<string> Conjugations { get; set; } = [];
}

public sealed class ReaderWord
{
    [JsonPropertyName("wordId")]
    public int WordId { get; set; }

    [JsonPropertyName("readingIndex")]
    public byte ReadingIndex { get; set; }

    [JsonPropertyName("spelling")]
    public string Spelling { get; set; } = string.Empty;

    [JsonPropertyName("reading")]
    public string Reading { get; set; } = string.Empty;

    [JsonPropertyName("frequencyRank")]
    public int FrequencyRank { get; set; }

    [JsonPropertyName("partsOfSpeech")]
    public List<string> PartsOfSpeech { get; set; } = [];

    [JsonPropertyName("meaningsChunks")]
    public List<List<string>> MeaningsChunks { get; set; } = [];

    [JsonPropertyName("meaningsPartOfSpeech")]
    public List<string> MeaningsPartOfSpeech { get; set; } = [];

    [JsonPropertyName("knownState")]
    public List<KnownState> KnownState { get; set; } = [];

    [JsonPropertyName("pitchAccents")]
    public List<int> PitchAccents { get; set; } = [];

    [JsonPropertyName("studyDeckIds")]
    public List<int> StudyDeckIds { get; set; } = [];
}