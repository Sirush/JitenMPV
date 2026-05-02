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
}
