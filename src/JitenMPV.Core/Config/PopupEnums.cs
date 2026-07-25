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
