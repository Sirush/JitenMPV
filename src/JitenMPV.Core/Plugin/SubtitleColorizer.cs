using JitenMPV.Core.Api;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Rendering;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plugin;

public sealed record ColorizedSubtitle(
    string Ass,
    ParseCacheEntry? Entry,
    IReadOnlyDictionary<(int WordId, byte ReadingIndex), UnderlineBar>? Underlines);

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

    public async Task<ColorizedSubtitle> ColorizeAsync(string subtitleText, CancellationToken ct)
        => await ColorizeWithRevealAsync(subtitleText, null, ct);

    public async Task<ColorizedSubtitle> ColorizeWithRevealAsync(
        string subtitleText,
        HashSet<(int WordId, byte ReadingIndex)>? revealedWords,
        CancellationToken ct)
    {
        try
        {
            if (!JapaneseDetector.ContainsJapanese(subtitleText))
                return new ColorizedSubtitle(renderer.RenderPlain(subtitleText), null, null);

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

            var (ass, underlines) = renderer.RenderSubtitle(
                subtitleText, entry, iPlusOne, freqWords, revealedWords);
            return new ColorizedSubtitle(ass, entry, underlines);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to colorize subtitle, falling back to plain rendering");
            return new ColorizedSubtitle(renderer.RenderPlain(subtitleText), null, null);
        }
    }
}
