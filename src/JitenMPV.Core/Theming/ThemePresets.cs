using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Theming;

public static class ThemePresets
{
    public static WordStyleState Unparsed { get; } = new() { TextColor = "#eeeeee", OutlineColor = "#000000", OutlineSize = 3 };

    private static readonly WordStyleState DimmedStyle = new()
                                                         {
                                                             TextColor = "#969696", OutlineColor = "#505050", OutlineSize = 3,
                                                             TextOpacity = 150
                                                         };

    public static IReadOnlyDictionary<KnownState, WordStyleState> Default { get; } =
        new Dictionary<KnownState, WordStyleState>
        {
            [KnownState.New] = new() { TextColor = "#a566ef", OutlineColor = "#000000", OutlineSize = 3 },
            [KnownState.Young] = new() { TextColor = "#eeeeee", OutlineColor = "#d08700", OutlineSize = 3 },
            [KnownState.Mature] = new() { TextColor = "#eeeeee", OutlineColor = "#70c000", OutlineSize = 3 },
            [KnownState.Blacklisted] = DimmedStyle,
            [KnownState.Due] = new() { TextColor = "#eeeeee", OutlineColor = "#ff4500", OutlineSize = 3 },
            [KnownState.Mastered] = new() { TextColor = "#c8c8c8", OutlineColor = "#006400", OutlineSize = 3, TextOpacity = 200 },
            [KnownState.Redundant] = DimmedStyle
        };
}