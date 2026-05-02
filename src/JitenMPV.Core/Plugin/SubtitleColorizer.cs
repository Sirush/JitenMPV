using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Rendering;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plugin;

public sealed class SubtitleColorizer(JitenApiClient api, ParseCache cache, OverlayRenderer renderer, ILogger logger)
{
    public async Task<string> ColorizeAsync(string subtitleText, CancellationToken ct)
    {
        try
        {
            var entry = cache.GetOrDefault(subtitleText);
            if (entry is null)
            {
                var parseResponse = await api.ParseAsync(subtitleText, ct);
                entry = ParseCacheEntry.From(parseResponse);
                cache.Set(subtitleText, entry);
            }

            return renderer.RenderSubtitle(subtitleText, entry.Tokens, entry.VocabStates);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to colorize subtitle, falling back to plain rendering");
            return renderer.RenderPlain(subtitleText);
        }
    }
}