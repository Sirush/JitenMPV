using System.Text.Json.Serialization;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Config;

public sealed class CustomStateStyle
{
    [JsonPropertyName("text_color")]
    public string TextColor { get; set; } = "#ffffff";

    [JsonPropertyName("outline_color")]
    public string OutlineColor { get; set; } = "#000000";

    [JsonPropertyName("shadow_color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShadowColor { get; set; }

    [JsonPropertyName("outline_size")]
    public double OutlineSize { get; set; } = 3;

    [JsonPropertyName("shadow_depth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ShadowDepth { get; set; }

    [JsonPropertyName("text_opacity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TextOpacity { get; set; }

    [JsonPropertyName("bold")]
    public bool Bold { get; set; }

    [JsonPropertyName("italic")]
    public bool Italic { get; set; }

    [JsonPropertyName("underline")]
    public bool Underline { get; set; }

    [JsonPropertyName("strikethrough")]
    public bool Strikethrough { get; set; }

    public WordStyleState ToWordStyleState() => new()
    {
        TextColor = TextColor,
        OutlineColor = OutlineColor,
        ShadowColor = ShadowColor,
        OutlineSize = OutlineSize,
        ShadowDepth = ShadowDepth,
        TextOpacity = TextOpacity,
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        Strikethrough = Strikethrough,
    };

    public static CustomStateStyle FromWordStyleState(WordStyleState ws) => new()
    {
        TextColor = ws.TextColor ?? "#ffffff",
        OutlineColor = ws.OutlineColor ?? "#000000",
        ShadowColor = ws.ShadowColor,
        OutlineSize = ws.OutlineSize ?? 3,
        ShadowDepth = ws.ShadowDepth,
        TextOpacity = ws.TextOpacity,
        Bold = ws.Bold ?? false,
        Italic = ws.Italic ?? false,
        Underline = ws.Underline ?? false,
        Strikethrough = ws.Strikethrough ?? false,
    };
}
