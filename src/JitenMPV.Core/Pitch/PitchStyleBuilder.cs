using JitenMPV.Core.Config;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Pitch;

public static class PitchStyleBuilder
{
    /// Null when the word's own styling is left alone — colouring off, or underline mode, which
    /// PitchUnderlineRenderer draws on a separate overlay instead.
    public static IReadOnlyDictionary<PitchClass, WordStyleState>? Build(PluginSettings settings)
    {
        if (!settings.PitchColoringEnabled) return null;
        if (settings.PitchIndicator == PitchIndicatorMode.Underline) return null;

        return PitchAccent.Styleable.ToDictionary(
            c => c,
            c => new WordStyleState { TextColor = ColorFor(settings, c) });
    }

    /// Bar colours for underline mode; empty whenever no bars should be drawn.
    public static IReadOnlyDictionary<PitchClass, string> BuildUnderlineColors(PluginSettings settings)
    {
        if (!settings.PitchColoringEnabled || settings.PitchIndicator != PitchIndicatorMode.Underline)
            return new Dictionary<PitchClass, string>();

        return PitchAccent.Styleable.ToDictionary(c => c, c => ColorFor(settings, c));
    }

    private static string ColorFor(PluginSettings settings, PitchClass pitchClass)
        => settings.PitchStyles?.GetValueOrDefault(pitchClass.ToString())?.TextColor
           ?? PitchAccent.DefaultColor(pitchClass);
}
