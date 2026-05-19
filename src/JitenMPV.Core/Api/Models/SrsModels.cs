using System.Text.Json.Serialization;

namespace JitenMPV.Core.Api.Models;

public sealed class SetVocabularyStateRequest
{
    [JsonPropertyName("wordId")]
    public int WordId { get; init; }

    [JsonPropertyName("readingIndex")]
    public byte ReadingIndex { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }
}

public sealed class SetVocabularyStateResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

public sealed class ReviewRequest
{
    [JsonPropertyName("wordId")]
    public int WordId { get; init; }

    [JsonPropertyName("readingIndex")]
    public byte ReadingIndex { get; init; }

    [JsonPropertyName("rating")]
    public int Rating { get; init; }
}

public sealed class ReviewResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("newState")]
    public KnownState? NewState { get; set; }
}

public static class VocabularyStateActions
{
    public const string NeverForgetAdd = "neverForget-add";
    public const string NeverForgetRemove = "neverForget-remove";
    public const string BlacklistAdd = "blacklist-add";
    public const string BlacklistRemove = "blacklist-remove";
    public const string SuspendAdd = "suspend-add";
    public const string SuspendRemove = "suspend-remove";
    public const string ForgetAdd = "forget-add";
}
