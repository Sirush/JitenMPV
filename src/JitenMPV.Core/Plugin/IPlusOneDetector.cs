using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Plugin;

public sealed class IPlusOneDetector(int minTokens = 3, int maxFrequencyRank = 15000)
{
    public HashSet<(int WordId, byte ReadingIndex)>? Detect(
        List<ReaderToken> tokens,
        IDictionary<(int WordId, byte ReadingIndex), KnownState> vocabStates,
        IDictionary<(int WordId, byte ReadingIndex), int>? frequencyRanks = null)
    {
        if (tokens.Count < minTokens) return null;

        (int WordId, byte ReadingIndex) firstUnknown = default;
        bool foundUnknown = false;

        foreach (var token in tokens)
        {
            var key = (token.WordId, token.ReadingIndex);

            if (!vocabStates.TryGetValue(key, out var state) || state != KnownState.New)
                continue;

            if (frequencyRanks is not null
                && frequencyRanks.TryGetValue(key, out var rank)
                && rank > maxFrequencyRank)
                continue;

            if (!foundUnknown)
            {
                firstUnknown = key;
                foundUnknown = true;
            }
            else if (key != firstUnknown)
            {
                return null;
            }
        }

        return foundUnknown ? [firstUnknown] : null;
    }
}
