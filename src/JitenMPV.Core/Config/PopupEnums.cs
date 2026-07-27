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
    BelowSubtitle,

    /// Pins the popup to PopupFixedAnchor instead of following the pointer.
    Fixed
}

[JsonConverter(typeof(PopupAnchorConverter))]
public enum PopupAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight
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


[JsonConverter(typeof(MediaOverwritePromptConverter))]
public enum MediaOverwritePrompt { Always, OncePerSession, Never }

[JsonConverter(typeof(MediaImageSourceConverter))]
public enum MediaImageSource
{
    /// The frame currently on screen, taken straight from mpv without a seek.
    MpvFrame,

    /// Seeks to the middle of the subtitle's span, for when the user paused on a black frame.
    SubtitleMidpoint
}

/// None = clean frame. Original = the subtitle track's own styling, burned in by libass.
/// Colored = JitenMPV's knowledge-state colouring, which only the mpv window carries.
[JsonConverter(typeof(MediaSubtitleBurnConverter))]
public enum MediaSubtitleBurn { None, Original, Colored }

internal sealed class DoubleClickActionConverter() : JsonStringEnumConverter<DoubleClickAction>(JsonNamingPolicy.KebabCaseLower);
internal sealed class MediaOverwritePromptConverter() : JsonStringEnumConverter<MediaOverwritePrompt>(JsonNamingPolicy.KebabCaseLower);
internal sealed class MediaImageSourceConverter() : JsonStringEnumConverter<MediaImageSource>(JsonNamingPolicy.KebabCaseLower);
internal sealed class MediaSubtitleBurnConverter() : JsonStringEnumConverter<MediaSubtitleBurn>(JsonNamingPolicy.KebabCaseLower);
internal sealed class PopupTriggerModeConverter() : JsonStringEnumConverter<PopupTriggerMode>(JsonNamingPolicy.KebabCaseLower);
internal sealed class PopupPositionModeConverter() : JsonStringEnumConverter<PopupPositionMode>(JsonNamingPolicy.KebabCaseLower);
internal sealed class PopupAnchorConverter() : JsonStringEnumConverter<PopupAnchor>(JsonNamingPolicy.KebabCaseLower);
