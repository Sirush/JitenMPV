using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Plugin;

public sealed class FrequencyMarker(int topN = 10000, bool markAllStates = false)
{
    public HashSet<(int WordId, byte ReadingIndex)>? Mark(
        List<ReaderToken> tokens,
        Dictionary<(int WordId, byte ReadingIndex), KnownState> vocabStates,
        Dictionary<(int WordId, byte ReadingIndex), int> frequencyRanks)
    {
        HashSet<(int WordId, byte ReadingIndex)>? result = null;

        foreach (var token in tokens)
        {
            var key = (token.WordId, token.ReadingIndex);

            if (!frequencyRanks.TryGetValue(key, out var rank) || rank > topN)
                continue;

            if (!markAllStates
                && vocabStates.TryGetValue(key, out var state)
                && state != KnownState.New)
                continue;

            result ??= [];
            result.Add(key);
        }

        return result;
    }
}
