using System.Collections.Concurrent;
using JitenMPV.Core.Api.Models;

namespace JitenMPV.Core.Cache;

public sealed class ParseCacheEntry
{
    public required List<ReaderToken> Tokens { get; init; }
    public required Dictionary<(int WordId, byte ReadingIndex), KnownState> VocabStates { get; init; }

    public static ParseCacheEntry From(ReaderParseResponse response)
    {
        var vocabStates = new Dictionary<(int, byte), KnownState>();
        foreach (var word in response.Vocabulary)
        {
            if (word.KnownState.Count > 0)
                vocabStates.TryAdd((word.WordId, word.ReadingIndex), word.KnownState[0]);
        }

        var tokens = response.Tokens.Count > 0 ? response.Tokens[0] : [];
        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new ParseCacheEntry { Tokens = tokens, VocabStates = vocabStates };
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
        _cache[text] = entry;
        _order.Enqueue(text);
        while (_cache.Count > maxEntries && _order.TryDequeue(out var oldest))
            _cache.TryRemove(oldest, out _);
    }
}