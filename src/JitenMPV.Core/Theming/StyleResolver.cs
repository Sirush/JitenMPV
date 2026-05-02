using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Theming;

public sealed class StyleResolver(
    IReadOnlyDictionary<KnownState, WordStyleState> theme,
    WordStyleState unparsed,
    WordStyleState? iPlusOneOverride = null,
    WordStyleState? frequencyOverride = null)
{
    public WordStyleState Resolve(
        ReaderToken token,
        Dictionary<(int WordId, byte ReadingIndex), KnownState> vocabStates,
        HashSet<(int WordId, byte ReadingIndex)>? iPlusOneWords = null,
        HashSet<(int WordId, byte ReadingIndex)>? frequencyWords = null)
    {
        var key = (token.WordId, token.ReadingIndex);

        if (!vocabStates.TryGetValue(key, out var state) || !theme.TryGetValue(state, out var style))
            return unparsed;

        if (iPlusOneOverride is not null && iPlusOneWords?.Contains(key) == true)
            style = iPlusOneOverride.MergeOver(style);

        if (frequencyOverride is not null && frequencyWords?.Contains(key) == true)
            style = frequencyOverride.MergeOver(style);

        return style;
    }
}
