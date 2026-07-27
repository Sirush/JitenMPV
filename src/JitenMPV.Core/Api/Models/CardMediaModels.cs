using System.Text.Json.Serialization;

namespace JitenMPV.Core.Api.Models;

public sealed class JitenPlusStatusResponse
{
    [JsonPropertyName("tier")] public string Tier { get; set; } = "none";
    [JsonPropertyName("quota")] public JitenPlusQuotaDto? Quota { get; set; }
}

public sealed class JitenPlusQuotaDto
{
    [JsonPropertyName("usedBytes")] public long UsedBytes { get; set; }
    [JsonPropertyName("maxBytes")] public long MaxBytes { get; set; }
}

public sealed class CardMediaBatchRequest
{
    [JsonPropertyName("items")] public required List<CardMediaKey> Items { get; set; }
}

public sealed class CardMediaKey
{
    [JsonPropertyName("wordId")] public int WordId { get; set; }
    [JsonPropertyName("readingIndex")] public int ReadingIndex { get; set; }
}

public sealed class CardMediaBatchResponse
{
    [JsonPropertyName("items")] public List<CardMediaEntry> Items { get; set; } = [];
}

public sealed class CardMediaEntry
{
    [JsonPropertyName("wordId")] public int WordId { get; set; }
    [JsonPropertyName("readingIndex")] public int ReadingIndex { get; set; }
    [JsonPropertyName("image")] public CardMediaFile? Image { get; set; }
    [JsonPropertyName("audio")] public CardMediaFile? Audio { get; set; }
}

public sealed class CardMediaFile
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("contentType")] public string ContentType { get; set; } = "";
    [JsonPropertyName("fileSizeBytes")] public long FileSizeBytes { get; set; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }

    /// True when the file belongs to a sibling reading and would not actually be overwritten.
    [JsonPropertyName("inherited")] public bool Inherited { get; set; }

    [JsonPropertyName("sourceReadingIndex")] public int SourceReadingIndex { get; set; }
}

public sealed class CardMediaUploadResponse
{
    [JsonPropertyName("media")] public CardMediaFile? Media { get; set; }
    [JsonPropertyName("quota")] public JitenPlusQuotaDto? Quota { get; set; }
}

public enum CardMediaUploadStatus { Success, QuotaExceeded, Rejected, Failed }

public sealed record CardMediaUploadResult(
    CardMediaUploadStatus Status,
    long StoredBytes,
    long UsedBytes,
    long MaxBytes,
    string? Error)
{
    public bool IsSuccess => Status == CardMediaUploadStatus.Success;

    public static CardMediaUploadResult Success(long storedBytes, long usedBytes, long maxBytes)
        => new(CardMediaUploadStatus.Success, storedBytes, usedBytes, maxBytes, null);

    public static CardMediaUploadResult QuotaExceeded(long usedBytes, long maxBytes, string? error)
        => new(CardMediaUploadStatus.QuotaExceeded, 0, usedBytes, maxBytes, error);

    public static CardMediaUploadResult Rejected(string? error)
        => new(CardMediaUploadStatus.Rejected, 0, 0, 0, error);

    public static CardMediaUploadResult Failed(string? error)
        => new(CardMediaUploadStatus.Failed, 0, 0, 0, error);
}
