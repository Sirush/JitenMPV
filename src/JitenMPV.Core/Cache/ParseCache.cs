using System.Collections.Concurrent;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Pitch;

namespace JitenMPV.Core.Cache;

public sealed class ParseCacheEntry
{
    public required List<ReaderToken> Tokens { get; init; }
    public required ConcurrentDictionary<(int WordId, byte ReadingIndex), KnownState> VocabStates { get; init; }
    public required Dictionary<(int WordId, byte ReadingIndex), int> FrequencyRanks { get; init; }
    public required Dictionary<(int WordId, byte ReadingIndex), ReaderWord> VocabDetails { get; init; }
    public Dictionary<(int WordId, byte ReadingIndex), PitchClass> PitchClasses { get; init; } = [];

    public static ParseCacheEntry From(ReaderParseResponse response)
        => From(response, 0);

    public static ParseCacheEntry From(ReaderParseResponse response, int tokenListIndex)
    {
        var (vocabStates, freqRanks, vocabDetails, pitchClasses) = BuildVocabData(response);
        return FromTokens(
            tokenListIndex < response.Tokens.Count ? response.Tokens[tokenListIndex] : [],
            vocabStates, freqRanks, vocabDetails, pitchClasses);
    }

    internal static ParseCacheEntry FromTokens(
        List<ReaderToken> tokens,
        ConcurrentDictionary<(int, byte), KnownState> vocabStates,
        Dictionary<(int, byte), int> freqRanks,
        Dictionary<(int, byte), ReaderWord>? vocabDetails = null,
        Dictionary<(int, byte), PitchClass>? pitchClasses = null)
    {
        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new ParseCacheEntry
        {
            Tokens = tokens,
            VocabStates = vocabStates,
            FrequencyRanks = freqRanks,
            VocabDetails = vocabDetails ?? [],
            PitchClasses = pitchClasses ?? []
        };
    }

    internal static (
        ConcurrentDictionary<(int, byte), KnownState> VocabStates,
        Dictionary<(int, byte), int> FrequencyRanks,
        Dictionary<(int, byte), ReaderWord> VocabDetails,
        Dictionary<(int, byte), PitchClass> PitchClasses)
        BuildVocabData(ReaderParseResponse response)
    {
        var vocabStates = new ConcurrentDictionary<(int, byte), KnownState>();
        var freqRanks = new Dictionary<(int, byte), int>();
        var vocabDetails = new Dictionary<(int, byte), ReaderWord>();
        var pitchClasses = new Dictionary<(int, byte), PitchClass>();

        foreach (var word in response.Vocabulary)
        {
            var key = (word.WordId, word.ReadingIndex);
            vocabDetails.TryAdd(key, word);
            if (word.KnownState.Count > 0)
                vocabStates.TryAdd(key, KnownStates.Collapse(word.KnownState));
            if (word.FrequencyRank > 0)
                freqRanks.TryAdd(key, word.FrequencyRank);

            // The first accent is the primary reading, matching how the Reader styles words.
            if (word.PitchAccents.Count > 0)
            {
                var pitchClass = PitchAccent.ClassifyReading(word.Reading, word.PitchAccents[0]);
                if (pitchClass != PitchClass.Unknown)
                    pitchClasses.TryAdd(key, pitchClass);
            }
        }

        return (vocabStates, freqRanks, vocabDetails, pitchClasses);
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