using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Config;

public static class ThemeCodeImporter
{
    private const string Prefix = "jtr:1";

    private static readonly Dictionary<string, string> StateNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["new"] = "New",
        ["young"] = "Young",
        ["mature"] = "Mature",
        ["mastered"] = "Mastered",
        ["due"] = "Due",
        ["blacklisted"] = "Blacklisted",
        ["redundant"] = "Redundant",
    };

    public static Dictionary<string, CustomStateStyle>? TryImport(string code, out string? themeName)
    {
        themeName = null;

        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        try
        {
            var base64 = code[Prefix.Length..];
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                themeName = nameProp.GetString();

            if (!root.TryGetProperty("states", out var states) || states.ValueKind != JsonValueKind.Object)
                return null;

            var result = new Dictionary<string, CustomStateStyle>();

            foreach (var stateProp in states.EnumerateObject())
            {
                if (!StateNameMap.TryGetValue(stateProp.Name, out var mpvStateName))
                    continue;

                if (!stateProp.Value.TryGetProperty("effects", out var effects) ||
                    effects.ValueKind != JsonValueKind.Array)
                    continue;

                var style = new CustomStateStyle();
                MapEffects(effects, style);
                result[mpvStateName] = style;
            }

            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static void MapEffects(JsonElement effects, CustomStateStyle style)
    {
        foreach (var effect in effects.EnumerateArray())
        {
            if (!effect.TryGetProperty("type", out var typeProp))
                continue;

            var type = typeProp.GetString();
            switch (type)
            {
                case "text-colour":
                    if (effect.TryGetProperty("colour", out var tc))
                        style.TextColor = NormalizeColor(tc.GetString()) ?? style.TextColor;
                    break;

                case "shadow":
                    if (effect.TryGetProperty("colour", out var sc))
                        style.ShadowColor = NormalizeColor(sc.GetString());
                    var ox = effect.TryGetProperty("offsetX", out var oxProp) ? Math.Abs(oxProp.GetDouble()) : 0;
                    var oy = effect.TryGetProperty("offsetY", out var oyProp) ? Math.Abs(oyProp.GetDouble()) : 0;
                    style.ShadowDepth = Math.Max(ox, oy);
                    break;

                case "opacity":
                    if (effect.TryGetProperty("value", out var opVal))
                        style.TextOpacity = (int)Math.Round(opVal.GetDouble() * 255);
                    break;

                case "font-weight":
                    if (effect.TryGetProperty("value", out var fw) && fw.GetString() == "bold")
                        style.Bold = true;
                    break;

                case "font-style":
                    if (effect.TryGetProperty("value", out var fs) && fs.GetString() == "italic")
                        style.Italic = true;
                    break;

                // Dashed, dotted and wavy all import as a solid bar; ASS drawings have no dash pattern.
                case "underline":
                    style.Underline = true;
                    if (effect.TryGetProperty("colour", out var uc))
                        style.UnderlineColor = NormalizeColor(uc.GetString());
                    if (effect.TryGetProperty("thickness", out var ut))
                        style.UnderlineThickness = Math.Clamp(ut.GetDouble(), 1, 10);
                    break;

                // The reader's border boxes the word; ASS has only a glyph outline, so the radius
                // and dashed style are dropped and the width carries over as-is.
                case "border":
                    if (effect.TryGetProperty("colour", out var bc) && NormalizeColor(bc.GetString()) is { } borderColor)
                        style.OutlineColor = borderColor;
                    if (effect.TryGetProperty("width", out var bw))
                        style.OutlineSize = Math.Clamp(bw.GetDouble(), 0, 10);
                    break;
            }
        }
    }

    private static string? NormalizeColor(string? hex)
    {
        if (hex is null) return null;
        return $"#{WordStyleState.NormalizeHex(hex)}";
    }
}
