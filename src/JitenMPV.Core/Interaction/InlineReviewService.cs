using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Mpv;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Interaction;

public sealed class InlineReviewService(
    JitenApiClient api,
    ParseCache cache,
    StatusOverlay status,
    ILogger logger)
{
    private static readonly string[] RatingLabels = ["", "Again", "Hard", "Good", "Easy"];

    public async Task<bool> ReviewAsync(
        int wordId, byte readingIndex, int rating,
        MpvIpcClient ipc, CancellationToken ct)
    {
        try
        {
            var response = await api.ReviewAsync(wordId, readingIndex, rating, ct);
            if (!response.Success) return false;

            if (response.NewState is { } newState)
            {
                cache.UpdateWordState(wordId, readingIndex,
                    FsrsStateMapper.ToKnownState(newState, response.NextDue, DateTimeOffset.UtcNow));
            }

            var label = rating >= 1 && rating < RatingLabels.Length ? RatingLabels[rating] : rating.ToString();
            await status.ShowAsync(ipc, $"Reviewed: {label}", 1500, ct);

            logger.LogInformation("Reviewed word {WordId}:{ReadingIndex} rating={Rating}", wordId, readingIndex, rating);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to review word {WordId}:{ReadingIndex}", wordId, readingIndex);
            return false;
        }
    }
}
