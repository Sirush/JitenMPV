using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Theming;

public sealed class StyleResolver
{
    private sealed record ThemeSnapshot(
        IReadOnlyDictionary<KnownState, WordStyleState> Theme,
        WordStyleState? IPlusOneOverride,
        WordStyleState? FrequencyOverride,
        HashSet<KnownState>? BlurStates,
        WordStyleState? BlurOnStyle,
        WordStyleState BlurOffStyle);

    private readonly WordStyleState _unparsed;
    private volatile ThemeSnapshot _snapshot;

    public StyleResolver(
        IReadOnlyDictionary<KnownState, WordStyleState> theme,
        WordStyleState unparsed,
        WordStyleState? iPlusOneOverride = null,
        WordStyleState? frequencyOverride = null,
        HashSet<KnownState>? blurStates = null,
        double blurStrength = 0)
    {
        _unparsed = unparsed;
        _snapshot = BuildSnapshot(theme, iPlusOneOverride, frequencyOverride, blurStates, blurStrength);
    }

    public void UpdateTheme(
        IReadOnlyDictionary<KnownState, WordStyleState> newTheme,
        WordStyleState? newIPlusOneOverride,
        WordStyleState? newFrequencyOverride,
        HashSet<KnownState>? blurStates = null,
        double blurStrength = 0)
    {
        _snapshot = BuildSnapshot(newTheme, newIPlusOneOverride, newFrequencyOverride, blurStates, blurStrength);
    }

    internal static readonly WordStyleState BlurOffStyle = new() { Blur = 0 };

    private static ThemeSnapshot BuildSnapshot(
        IReadOnlyDictionary<KnownState, WordStyleState> theme,
        WordStyleState? iPlusOneOverride,
        WordStyleState? frequencyOverride,
        HashSet<KnownState>? blurStates,
        double blurStrength)
    {
        var blurOn = blurStates is not null ? new WordStyleState { Blur = blurStrength } : null;
        return new ThemeSnapshot(theme, iPlusOneOverride, frequencyOverride, blurStates, blurOn, BlurOffStyle);
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
        HashSet<(int WordId, byte ReadingIndex)>? frequencyWords = null)
    {
        var snap = _snapshot;
        var key = (token.WordId, token.ReadingIndex);

        if (!vocabStates.TryGetValue(key, out var state) || !snap.Theme.TryGetValue(state, out var style))
            return _unparsed;

        if (snap.IPlusOneOverride is not null && iPlusOneWords?.Contains(key) == true)
            style = snap.IPlusOneOverride.MergeOver(style);

        if (snap.FrequencyOverride is not null && frequencyWords?.Contains(key) == true)
            style = snap.FrequencyOverride.MergeOver(style);

        if (snap.BlurStates is not null)
        {
            style = snap.BlurStates.Contains(state)
                ? snap.BlurOnStyle!.MergeOver(style)
                : style.Blur is not null ? snap.BlurOffStyle.MergeOver(style) : style;
        }

        return style;
    }
}
