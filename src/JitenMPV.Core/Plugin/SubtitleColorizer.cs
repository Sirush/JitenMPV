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
    public async Task<string> ColorizeAsync(string subtitleText, CancellationToken ct)
    {
        try
        {
            if (!JapaneseDetector.ContainsJapanese(subtitleText))
                return renderer.RenderPlain(subtitleText);

            var entry = cache.GetOrDefault(subtitleText);
            if (entry is null)
            {
                var parseResponse = await api.ParseAsync(subtitleText, ct);
                entry = ParseCacheEntry.From(parseResponse);
                cache.Set(subtitleText, entry);
            }

            var iPlusOne = iPlusOneDetector?.Detect(entry.Tokens, entry.VocabStates, entry.FrequencyRanks);
            var freqWords = frequencyMarker?.Mark(entry.Tokens, entry.VocabStates, entry.FrequencyRanks);

            return renderer.RenderSubtitle(subtitleText, entry, iPlusOne, freqWords);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to colorize subtitle, falling back to plain rendering");
            return renderer.RenderPlain(subtitleText);
        }
    }
}