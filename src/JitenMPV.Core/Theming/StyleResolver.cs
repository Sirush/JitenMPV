using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;
using JitenMPV.Core.Pitch;

namespace JitenMPV.Core.Theming;

public sealed class StyleResolver
{
    private sealed record ThemeSnapshot(
        IReadOnlyDictionary<KnownState, WordStyleState> Theme,
        WordStyleState? IPlusOneOverride,
        WordStyleState? FrequencyOverride,
        IReadOnlyDictionary<PitchClass, WordStyleState>? PitchStyles,
        HashSet<KnownState>? BlurStates,
        WordStyleState? BlurOnStyle);

    private readonly WordStyleState _unparsed;
    private volatile ThemeSnapshot _snapshot;

    public StyleResolver(
        IReadOnlyDictionary<KnownState, WordStyleState> theme,
        WordStyleState unparsed,
        WordStyleState? iPlusOneOverride = null,
        WordStyleState? frequencyOverride = null,
        HashSet<KnownState>? blurStates = null,
        double blurStrength = 0,
        IReadOnlyDictionary<PitchClass, WordStyleState>? pitchStyles = null)
    {
        _unparsed = unparsed;
        _snapshot = BuildSnapshot(theme, iPlusOneOverride, frequencyOverride, blurStates, blurStrength, pitchStyles);
    }

    public void UpdateTheme(
        IReadOnlyDictionary<KnownState, WordStyleState> newTheme,
        WordStyleState? newIPlusOneOverride,
        WordStyleState? newFrequencyOverride,
        HashSet<KnownState>? blurStates = null,
        double blurStrength = 0,
        IReadOnlyDictionary<PitchClass, WordStyleState>? pitchStyles = null)
    {
        _snapshot = BuildSnapshot(newTheme, newIPlusOneOverride, newFrequencyOverride, blurStates, blurStrength, pitchStyles);
    }

    private static ThemeSnapshot BuildSnapshot(
        IReadOnlyDictionary<KnownState, WordStyleState> theme,
        WordStyleState? iPlusOneOverride,
        WordStyleState? frequencyOverride,
        HashSet<KnownState>? blurStates,
        double blurStrength,
        IReadOnlyDictionary<PitchClass, WordStyleState>? pitchStyles)
    {
        // libass blurs the border bitmap whenever one exists and leaves the fill sharp, so the
        // outline and shadow have to go for \blur to reach the glyph itself.
        var blurOn = blurStates is not null
            ? new WordStyleState { Blur = blurStrength, OutlineSize = 0, ShadowDepth = 0 }
            : null;
        return new ThemeSnapshot(theme, iPlusOneOverride, frequencyOverride, pitchStyles, blurStates, blurOn);
    }

    public static HashSet<KnownState>? BuildBlurStates(PluginSettings settings)
    {
        if (!settings.BlurEnabled || settings.BlurStates.Count == 0)
            return null;
        return [..settings.BlurStates.Select(s => (KnownState)s)];
    }

    public WordStyleState Resolve(
        ReaderToken token,
        IDictionary<(int WordId, byte ReadingIndex), KnownState> vocabStates,
        HashSet<(int WordId, byte ReadingIndex)>? iPlusOneWords = null,
        HashSet<(int WordId, byte ReadingIndex)>? frequencyWords = null,
        IReadOnlyDictionary<(int WordId, byte ReadingIndex), PitchClass>? pitchClasses = null,
        HashSet<(int WordId, byte ReadingIndex)>? revealedWords = null)
    {
        var snap = _snapshot;
        var key = (token.WordId, token.ReadingIndex);

        if (!vocabStates.TryGetValue(key, out var state) || !snap.Theme.TryGetValue(state, out var style))
            return _unparsed;

        // Sits under i+1 and frequency: those mark with shadow and underline, so they still show
        // through a pitch colour, while pitch deliberately overrides the SRS text colour.
        if (snap.PitchStyles is not null && pitchClasses is not null
            && pitchClasses.TryGetValue(key, out var pitchClass)
            && snap.PitchStyles.TryGetValue(pitchClass, out var pitchStyle))
        {
            style = pitchStyle.MergeOver(style);
        }

        if (snap.IPlusOneOverride is not null && iPlusOneWords?.Contains(key) == true)
            style = snap.IPlusOneOverride.MergeOver(style);

        if (snap.FrequencyOverride is not null && frequencyWords?.Contains(key) == true)
            style = snap.FrequencyOverride.MergeOver(style);

        // Applied last and skipped for revealed words, so a reveal restores the untouched style
        // instead of leaving the blur's stripped outline behind.
        if (snap.BlurStates is not null && snap.BlurStates.Contains(state)
            && revealedWords?.Contains(key) != true)
        {
            style = snap.BlurOnStyle!.MergeOver(style);
        }

        return style;
    }
}
