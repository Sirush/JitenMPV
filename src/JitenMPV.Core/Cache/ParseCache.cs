using System.Collections.Concurrent;
using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Cache;

public sealed class ParseCacheEntry
{
    public required List<ReaderToken> Tokens { get; init; }
    public required ConcurrentDictionary<(int WordId, byte ReadingIndex), KnownState> VocabStates { get; init; }
    public required Dictionary<(int WordId, byte ReadingIndex), int> FrequencyRanks { get; init; }
    public required Dictionary<(int WordId, byte ReadingIndex), ReaderWord> VocabDetails { get; init; }

    public static ParseCacheEntry From(ReaderParseResponse response)
        => From(response, 0);

    public static ParseCacheEntry From(ReaderParseResponse response, int tokenListIndex)
    {
        var (vocabStates, freqRanks, vocabDetails) = BuildVocabData(response);
        return FromTokens(
            tokenListIndex < response.Tokens.Count ? response.Tokens[tokenListIndex] : [],
            vocabStates, freqRanks, vocabDetails);
    }

    internal static ParseCacheEntry FromTokens(
        List<ReaderToken> tokens,
        ConcurrentDictionary<(int, byte), KnownState> vocabStates,
        Dictionary<(int, byte), int> freqRanks,
        Dictionary<(int, byte), ReaderWord>? vocabDetails = null)
    {
        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new ParseCacheEntry
        {
            Tokens = tokens,
            VocabStates = vocabStates,
            FrequencyRanks = freqRanks,
            VocabDetails = vocabDetails ?? []
        };
    }

    internal static (
        ConcurrentDictionary<(int, byte), KnownState> VocabStates,
        Dictionary<(int, byte), int> FrequencyRanks,
        Dictionary<(int, byte), ReaderWord> VocabDetails)
        BuildVocabData(ReaderParseResponse response)
    {
        var vocabStates = new ConcurrentDictionary<(int, byte), KnownState>();
        var freqRanks = new Dictionary<(int, byte), int>();
        var vocabDetails = new Dictionary<(int, byte), ReaderWord>();

        foreach (var word in response.Vocabulary)
        {
            var key = (word.WordId, word.ReadingIndex);
            vocabDetails.TryAdd(key, word);
            if (word.KnownState.Count > 0)
                vocabStates.TryAdd(key, KnownStates.Collapse(word.KnownState));
            if (word.FrequencyRank > 0)
                freqRanks.TryAdd(key, word.FrequencyRank);
        }

        return (vocabStates, freqRanks, vocabDetails);
    }
}

public sealed class ParseCache(int maxEntries = 2000)
{
    private readonly BoundedCache<string, ParseCacheEntry> _cache = new(maxEntries);

    public ParseCacheEntry? GetOrDefault(string text)
        => _cache.GetOrDefault(text);

    public void Set(string text, ParseCacheEntry entry)
        => _cache.TryAdd(text, entry);

    public void UpdateWordState(int wordId, byte readingIndex, KnownState newState)
    {
        var key = (wordId, readingIndex);
        _cache.ForEachValue(entry =>
        {
            // VocabStates only holds words that already had a server-side state, so an untracked
            // word needs an insert rather than an update; VocabDetails is what proves the word
            // actually occurs in this entry.
            if (entry.VocabDetails.ContainsKey(key))
                entry.VocabStates[key] = newState;
        });
    }
}