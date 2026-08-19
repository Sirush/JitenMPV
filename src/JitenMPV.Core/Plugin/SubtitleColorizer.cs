using JitenMPV.Core.Api;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Rendering;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plugin;

public sealed record ColorizedSubtitle(
    string Ass,
    ParseCacheEntry? Entry,
    IReadOnlyDictionary<(int WordId, byte ReadingIndex), UnderlineBar>? Underlines,
    IReadOnlyList<SubtitleRun> Runs);

public sealed class SubtitleColorizer(
    JitenApiClient api,
    ParseCache cache,
    OverlayRenderer renderer,
    IPlusOneDetector? iPlusOneDetector,
    FrequencyMarker? frequencyMarker,
    ILogger logger)
{
    private sealed record DetectorSnapshot(IPlusOneDetector? IPlusOne, FrequencyMarker? Frequency);

    private volatile DetectorSnapshot _detectors = new(iPlusOneDetector, frequencyMarker);

    public void UpdateDetectors(IPlusOneDetector? iPlusOne, FrequencyMarker? freqMarker)
    {
        _detectors = new DetectorSnapshot(iPlusOne, freqMarker);
    }

    public async Task<ColorizedSubtitle> ColorizeAsync(
        string subtitleText, bool suppressWrap, CancellationToken ct)
        => await ColorizeWithRevealAsync(subtitleText, null, suppressWrap, ct);

    /// Renders from a parse that already happened, without hitting the dictionary again. Used to
    /// colour a text whose line breaks were inserted after the parse (wrapped subtitles): the same
    /// tokens describe both texts, and re-parsing would split the words the breaks cut across.
    public Task<ColorizedSubtitle> ColorizeWithParsedEntryAsync(
        string subtitleText, ParseCacheEntry entry,
        HashSet<(int WordId, byte ReadingIndex)>? revealedWords,
        bool suppressWrap,
        CancellationToken ct)
    {
        try
        {
            // The parse cache resolves by text, so a text that was re-based onto an existing parse
            // (wrapped subtitles) must be registered under its own key as well. Otherwise later
            // re-renders, mining and word actions resolve the current text to a fresh parse with
            // entirely new word ids, or to nothing, and clicks land on hitboxes with no popup.
            cache.Set(subtitleText, entry);

            var det = _detectors;
            var iPlusOne = det.IPlusOne?.Detect(entry.Tokens, entry.VocabStates, entry.FrequencyRanks);
            var freqWords = det.Frequency?.Mark(entry.Tokens, entry.VocabStates, entry.FrequencyRanks);

            var (ass, underlines, runs) = renderer.RenderSubtitle(
                subtitleText, entry, iPlusOne, freqWords, revealedWords, suppressWrap);
            return Task.FromResult(new ColorizedSubtitle(ass, entry, underlines, runs));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to colour subtitle from existing parse, falling back to plain rendering");
            return Task.FromResult(Plain(subtitleText, suppressWrap));
        }
    }

    /// Plain white, for a text with no parse to colour by. Shares the caller's wrap mode so the
    /// fallback still breaks where the resolver said it would.
    public ColorizedSubtitle Plain(string subtitleText, bool suppressWrap)
        => new(renderer.RenderPlain(subtitleText, suppressWrap), null, null,
            SubtitleRunBuilder.Plain(subtitleText));

    public async Task<ColorizedSubtitle> ColorizeWithRevealAsync(
        string subtitleText,
        HashSet<(int WordId, byte ReadingIndex)>? revealedWords,
        bool suppressWrap,
        CancellationToken ct)
    {
        try
        {
            if (!JapaneseDetector.ContainsJapanese(subtitleText))
                return Plain(subtitleText, suppressWrap);

            var entry = cache.GetOrDefault(subtitleText);
            if (entry is null)
            {
                var parseResponse = await api.ParseAsync(subtitleText, ct);
                entry = ParseCacheEntry.From(parseResponse);
                cache.Set(subtitleText, entry);
            }

            var det = _detectors;
            var iPlusOne = det.IPlusOne?.Detect(entry.Tokens, entry.VocabStates, entry.FrequencyRanks);
            var freqWords = det.Frequency?.Mark(entry.Tokens, entry.VocabStates, entry.FrequencyRanks);

            var (ass, underlines, runs) = renderer.RenderSubtitle(
                subtitleText, entry, iPlusOne, freqWords, revealedWords, suppressWrap);
            return new ColorizedSubtitle(ass, entry, underlines, runs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to colorize subtitle, falling back to plain rendering");
            return Plain(subtitleText, suppressWrap);
        }
    }
}
