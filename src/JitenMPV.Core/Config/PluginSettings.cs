using System.Text.Json.Serialization;

namespace JitenMPV.Core.Config;

public sealed class PluginSettings
{
    [JsonPropertyName("api_base_url")]
    public string ApiBaseUrl { get; set; } = "https://api.jiten.moe";

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("font_family")]
    public string FontFamily { get; set; } = "Yu Gothic";

    [JsonPropertyName("font_size")]
    public int FontSize { get; set; } = 48;

    [JsonPropertyName("border_size")]
    public double BorderSize { get; set; } = 3.0;

    [JsonPropertyName("bottom_margin")]
    public int BottomMargin { get; set; } = 50;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "Default";

    [JsonPropertyName("i_plus_one_enabled")]
    public bool IPlusOneEnabled { get; set; } = true;

    [JsonPropertyName("i_plus_one_min_tokens")]
    public int IPlusOneMinTokens { get; set; } = 3;

    [JsonPropertyName("i_plus_one_max_frequency_rank")]
    public int IPlusOneMaxFrequencyRank { get; set; } = 15000;

    [JsonPropertyName("frequency_marking_enabled")]
    public bool FrequencyMarkingEnabled { get; set; }

    [JsonPropertyName("frequency_top_n")]
    public int FrequencyTopN { get; set; } = 10000;

    [JsonPropertyName("frequency_mark_all_states")]
    public bool FrequencyMarkAllStates { get; set; }
}
