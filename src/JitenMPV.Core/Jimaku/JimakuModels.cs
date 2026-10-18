using System.Text.Json.Serialization;

namespace JitenMPV.Core.Jimaku;

public sealed class JimakuEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("english_name")]
    public string? EnglishName { get; set; }

    [JsonPropertyName("japanese_name")]
    public string? JapaneseName { get; set; }

    [JsonPropertyName("flags")]
    public JimakuEntryFlags Flags { get; set; } = new();
}

public sealed class JimakuEntryFlags
{
    [JsonPropertyName("anime")]
    public bool Anime { get; set; } = true;

    [JsonPropertyName("movie")]
    public bool Movie { get; set; }
}

public sealed class JimakuFile
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
