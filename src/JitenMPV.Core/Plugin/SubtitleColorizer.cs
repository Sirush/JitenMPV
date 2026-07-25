using JitenMPV.Core.Api;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Rendering;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plugin;

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

    public async Task<(string Ass, ParseCacheEntry? Entry)> ColorizeAsync(string subtitleText, CancellationToken ct)
        => await ColorizeWithRevealAsync(subtitleText, null, ct);

    public async Task<(string Ass, ParseCacheEntry? Entry)> ColorizeWithRevealAsync(
        string subtitleText,
        HashSet<(int WordId, byte ReadingIndex)>? revealedWords,
        CancellationToken ct)
    {
        try
        {
            if (!JapaneseDetector.ContainsJapanese(subtitleText))
                return (renderer.RenderPlain(subtitleText), null);

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

            return (renderer.RenderSubtitle(subtitleText, entry, iPlusOne, freqWords, revealedWords), entry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to colorize subtitle, falling back to plain rendering");
            return (renderer.RenderPlain(subtitleText), null);
        }
    }
}
