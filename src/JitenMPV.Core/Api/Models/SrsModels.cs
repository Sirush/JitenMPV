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

    /// The scheduler state, not a KnownState — map it with FsrsStateMapper.
    [JsonPropertyName("newState")]
    public FsrsState? NewState { get; set; }

    [JsonPropertyName("nextDue")]
    public DateTimeOffset? NextDue { get; set; }
}

public sealed class BatchReviewItem
{
    [JsonPropertyName("wordId")]
    public int WordId { get; init; }

    [JsonPropertyName("readingIndex")]
    public byte ReadingIndex { get; init; }

    [JsonPropertyName("rating")]
    public int Rating { get; init; }
}

public sealed class BatchReviewRequest
{
    [JsonPropertyName("reviews")]
    public required IReadOnlyList<BatchReviewItem> Reviews { get; init; }
}

public sealed class BatchReviewResultItem
{
    [JsonPropertyName("wordId")]
    public int WordId { get; set; }

    [JsonPropertyName("readingIndex")]
    public byte ReadingIndex { get; set; }

    /// Scheduler state again, and without a per-item due date, so Young/Mature cannot be told
    /// apart here: map it with a null nextDue and refresh from lookup-vocabulary if it matters.
    [JsonPropertyName("newState")]
    public FsrsState NewState { get; set; }
}

public sealed class BatchReviewResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("processed")]
    public int Processed { get; set; }

    /// Word ids the server auto-suspended as leeches during this batch.
    [JsonPropertyName("leechSuspended")]
    public List<int> LeechSuspended { get; set; } = [];

    [JsonPropertyName("results")]
    public List<BatchReviewResultItem> Results { get; set; } = [];
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
