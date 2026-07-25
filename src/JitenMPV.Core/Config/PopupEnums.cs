using System.Text.Json;
using System.Text.Json.Serialization;

namespace JitenMPV.Core.Config;

[JsonConverter(typeof(PopupTriggerModeConverter))]
public enum PopupTriggerMode
{
    Hover,
    Click
}

[JsonConverter(typeof(PopupPositionModeConverter))]
public enum PopupPositionMode
{
    AboveSubtitle,
    BelowSubtitle
}

[JsonConverter(typeof(PitchIndicatorModeConverter))]
public enum PitchIndicatorMode
{
    /// Recolours the word itself.
    Text,

    /// Leaves the word's colour alone and draws a separate coloured bar beneath it.
    Underline
}

/// Falls back to the default instead of failing the whole config load, so a value written by a
/// version that offered more modes cannot reset every other setting.
internal sealed class PitchIndicatorModeConverter : JsonConverter<PitchIndicatorMode>
{
    public override PitchIndicatorMode Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return value == "underline" ? PitchIndicatorMode.Underline : PitchIndicatorMode.Text;
    }

    public override void Write(Utf8JsonWriter writer, PitchIndicatorMode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value == PitchIndicatorMode.Underline ? "underline" : "text");
}

[JsonConverter(typeof(DoubleClickActionConverter))]
public enum DoubleClickAction
{
    None,
    Master,
    Mine
}


internal sealed class DoubleClickActionConverter() : JsonStringEnumConverter<DoubleClickAction>(JsonNamingPolicy.KebabCaseLower);
internal sealed class PopupTriggerModeConverter() : JsonStringEnumConverter<PopupTriggerMode>(JsonNamingPolicy.KebabCaseLower);
internal sealed class PopupPositionModeConverter() : JsonStringEnumConverter<PopupPositionMode>(JsonNamingPolicy.KebabCaseLower);
