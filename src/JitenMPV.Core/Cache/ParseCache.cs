using System.Collections.Concurrent;
using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Cache;

public sealed class ParseCacheEntry
{
    public required List<ReaderToken> Tokens { get; init; }
    public required Dictionary<(int WordId, byte ReadingIndex), KnownState> VocabStates { get; init; }
    public required Dictionary<(int WordId, byte ReadingIndex), int> FrequencyRanks { get; init; }

    public static ParseCacheEntry From(ReaderParseResponse response)
        => From(response, 0);

    public static ParseCacheEntry From(ReaderParseResponse response, int tokenListIndex)
    {
        var (vocabStates, freqRanks) = BuildVocabData(response);
        return FromTokens(
            tokenListIndex < response.Tokens.Count ? response.Tokens[tokenListIndex] : [],
            vocabStates, freqRanks);
    }

    internal static ParseCacheEntry FromTokens(
        List<ReaderToken> tokens,
        Dictionary<(int, byte), KnownState> vocabStates,
        Dictionary<(int, byte), int> freqRanks)
    {
        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new ParseCacheEntry { Tokens = tokens, VocabStates = vocabStates, FrequencyRanks = freqRanks };
    }

    internal static (Dictionary<(int, byte), KnownState> VocabStates, Dictionary<(int, byte), int> FrequencyRanks)
        BuildVocabData(ReaderParseResponse response)
    {
        var vocabStates = new Dictionary<(int, byte), KnownState>();
        var freqRanks = new Dictionary<(int, byte), int>();

        foreach (var word in response.Vocabulary)
        {
            var key = (word.WordId, word.ReadingIndex);
            if (word.KnownState.Count > 0)
                vocabStates.TryAdd(key, word.KnownState[0]);
            if (word.FrequencyRank > 0)
                freqRanks.TryAdd(key, word.FrequencyRank);
        }

        return (vocabStates, freqRanks);
    }
}

public sealed class ParseCache(int maxEntries = 2000)
{
    private readonly ConcurrentDictionary<string, ParseCacheEntry> _cache = new();
    private readonly ConcurrentQueue<string> _order = new();

    public ParseCacheEntry? GetOrDefault(string text)
        => _cache.GetValueOrDefault(text);

    public void Set(string text, ParseCacheEntry entry)
    {
        if (_cache.TryAdd(text, entry))
        {
            _order.Enqueue(text);
            while (_cache.Count > maxEntries && _order.TryDequeue(out var oldest))
                _cache.TryRemove(oldest, out _);
        }
    }
}