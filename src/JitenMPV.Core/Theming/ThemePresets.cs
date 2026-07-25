using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Theming;

public static class ThemePresets
{
    public static WordStyleState Unparsed { get; } = new()
    {
        TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3
    };

    public static WordStyleState IPlusOne { get; } = new()
    {
        ShadowColor = "#359eff", ShadowDepth = 3
    };

    public static WordStyleState Frequency { get; } = new()
    {
        Underline = true
    };

    private static readonly WordStyleState DimmedStyle = new()
    {
        TextColor = "#969696", OutlineColor = "#505050", OutlineSize = 3, TextOpacity = 150
    };

    /// Suspended carries no preset styling of its own; it reuses each preset's Redundant look so a
    /// suspended word cannot fall through to the unparsed style.
    private static IReadOnlyDictionary<KnownState, WordStyleState> WithSuspended(
        Dictionary<KnownState, WordStyleState> theme)
    {
        theme[KnownState.Suspended] = theme[KnownState.Redundant];
        return theme;
    }

    public static IReadOnlyDictionary<KnownState, WordStyleState> Default { get; } =
        WithSuspended(new Dictionary<KnownState, WordStyleState>
        {
            [KnownState.New] = new() { TextColor = "#a566ef", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Young] = new() { TextColor = "#eeeeee", OutlineColor = "#d08700", OutlineSize = 3 },
            [KnownState.Mature] = new() { TextColor = "#eeeeee", OutlineColor = "#70c000", OutlineSize = 3 },
            [KnownState.Blacklisted] = DimmedStyle,
            [KnownState.Due] = new() { TextColor = "#eeeeee", OutlineColor = "#ff4500", OutlineSize = 3 },
            [KnownState.Mastered] = new() { TextColor = "#c8c8c8", OutlineColor = "#006400", OutlineSize = 3, TextOpacity = 200 },
            [KnownState.Redundant] = DimmedStyle
        });

    public static IReadOnlyDictionary<KnownState, WordStyleState> HighContrast { get; } =
        WithSuspended(new Dictionary<KnownState, WordStyleState>
        {
            [KnownState.New] = new() { TextColor = "#00ffff", OutlineColor = "#000000", OutlineSize = 4, Bold = true },
            [KnownState.Young] = new() { TextColor = "#ffff00", OutlineColor = "#000000", OutlineSize = 4, Underline = true },
            [KnownState.Mature] = new() { TextColor = "#00ff00", OutlineColor = "#000000", OutlineSize = 4 },
            [KnownState.Blacklisted] = new() { TextColor = "#808080", OutlineColor = "#000000", OutlineSize = 3, TextOpacity = 120 },
            [KnownState.Due] = new() { TextColor = "#ff4444", OutlineColor = "#000000", OutlineSize = 4, Bold = true },
            [KnownState.Mastered] = new() { TextColor = "#88ff88", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Redundant] = new() { TextColor = "#808080", OutlineColor = "#000000", OutlineSize = 3, TextOpacity = 120 }
        });

    public static IReadOnlyDictionary<KnownState, WordStyleState> Monochrome { get; } =
        WithSuspended(new Dictionary<KnownState, WordStyleState>
        {
            [KnownState.New] = new() { TextColor = "#cccccc", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Young] = new() { TextColor = "#999999", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Mature] = new() { TextColor = "#666666", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Blacklisted] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3, TextOpacity = 102 },
            [KnownState.Due] = new() { TextColor = "#ffffff", OutlineColor = "#000000", OutlineSize = 3, Underline = true },
            [KnownState.Mastered] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Redundant] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3, TextOpacity = 102 }
        });

    public static IReadOnlyDictionary<KnownState, WordStyleState> Subtle { get; } =
        WithSuspended(new Dictionary<KnownState, WordStyleState>
        {
            [KnownState.New] = new() { TextColor = "#e0e0e0", OutlineColor = "#3a2060", OutlineSize = 2 },
            [KnownState.Young] = new() { TextColor = "#e0e0e0", OutlineColor = "#4a3500", OutlineSize = 2 },
            [KnownState.Mature] = new() { TextColor = "#e0e0e0", OutlineColor = "#2a4a00", OutlineSize = 2 },
            [KnownState.Blacklisted] = new() { TextColor = "#c0c0c0", OutlineColor = "#202020", OutlineSize = 2, TextOpacity = 180 },
            [KnownState.Due] = new() { TextColor = "#e0e0e0", OutlineColor = "#4a1500", OutlineSize = 2 },
            [KnownState.Mastered] = new() { TextColor = "#c8c8c8", OutlineColor = "#1a3a00", OutlineSize = 2 },
            [KnownState.Redundant] = new() { TextColor = "#c0c0c0", OutlineColor = "#202020", OutlineSize = 2, TextOpacity = 180 }
        });

    public static IReadOnlyDictionary<KnownState, WordStyleState> UnderlineTheme { get; } =
        WithSuspended(new Dictionary<KnownState, WordStyleState>
        {
            [KnownState.New] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3, Underline = true, ShadowColor = "#a566ef", ShadowDepth = 2 },
            [KnownState.Young] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3, Underline = true, ShadowColor = "#e8a020", ShadowDepth = 2 },
            [KnownState.Mature] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Blacklisted] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Due] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3, Underline = true, ShadowColor = "#e03030", ShadowDepth = 2 },
            [KnownState.Mastered] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Redundant] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3 }
        });

    public static IReadOnlyDictionary<KnownState, WordStyleState> ToyBox { get; } =
        WithSuspended(new Dictionary<KnownState, WordStyleState>
        {
            [KnownState.New] = new() { TextColor = "#4b8dff", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Young] = new() { TextColor = "#55b87a", OutlineColor = "#000000", OutlineSize = 3, Underline = true },
            [KnownState.Mature] = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Blacklisted] = new() { TextColor = "#888888", OutlineColor = "#000000", OutlineSize = 3, TextOpacity = 130 },
            [KnownState.Due] = new() { TextColor = "#d08700", OutlineColor = "#000000", OutlineSize = 3, Underline = true },
            [KnownState.Mastered] = new() { TextColor = "#aaaaaa", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Redundant] = new() { TextColor = "#888888", OutlineColor = "#000000", OutlineSize = 3, TextOpacity = 130 }
        });

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<KnownState, WordStyleState>> All { get; } =
        new Dictionary<string, IReadOnlyDictionary<KnownState, WordStyleState>>
        {
            ["Default"] = Default,
            ["High Contrast"] = HighContrast,
            ["Monochrome"] = Monochrome,
            ["Subtle"] = Subtle,
            ["Underline"] = UnderlineTheme,
            ["Toy Box"] = ToyBox
        };
}