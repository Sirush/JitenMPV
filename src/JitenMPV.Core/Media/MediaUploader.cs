using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Plus;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

public sealed record MediaUploadOutcome(
    bool ImageUploaded,
    bool AudioUploaded,
    bool ImageAttempted,
    bool AudioAttempted,
    bool QuotaExceeded,
    bool Revoked,
    long UsedBytes,
    long MaxBytes,
    string? Error)
{
    public bool AnyUploaded => ImageUploaded || AudioUploaded;
    public bool AnyFailed => (ImageAttempted && !ImageUploaded) || (AudioAttempted && !AudioUploaded);
}

public sealed class MediaUploader(JitenApiClient api, JitenPlusService plus, ILogger logger)
{
    /// Image first, then audio: two independent uploads, so a failure between them is reported
    /// per kind rather than pretending the card is consistent.
    public async Task<MediaUploadOutcome> UploadAsync(
        int wordId, byte readingIndex, CapturedImage? image, CapturedAudio? audio, CancellationToken ct)
    {
        var state = new MediaUploadOutcome(false, false, image is not null, audio is not null,
            false, false, 0, 0, null);

        if (image is not null)
            state = Merge(state, await SendAsync(wordId, readingIndex, image.Bytes, image.FileName,
                image.ContentType, ct), isImage: true);

        if (audio is not null && !state.Revoked)
            state = Merge(state, await SendAsync(wordId, readingIndex, audio.Bytes, audio.FileName,
                audio.ContentType, ct), isImage: false);

        return state;
    }

    private async Task<CardMediaUploadResult> SendAsync(
        int wordId, byte readingIndex, byte[] bytes, string fileName, string contentType,
        CancellationToken ct)
    {
        try
        {
            var result = await api.UploadCardMediaAsync(
                wordId, readingIndex, bytes, fileName, contentType, ct);

            if (result.IsSuccess && result.MaxBytes > 0)
                plus.ApplyQuota(result.UsedBytes, result.MaxBytes);
            else if (result.IsSuccess)
                plus.ApplyQuotaDelta(result.StoredBytes);

            return result;
        }
        catch (JitenPlusRequiredException ex)
        {
            plus.MarkRevoked(ex.Message);
            return CardMediaUploadResult.Rejected(ex.Message);
        }
        catch (JitenApiKeyRejectedException)
        {
            return CardMediaUploadResult.Rejected("API key rejected");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Card media upload failed");
            return CardMediaUploadResult.Failed(ex.Message);
        }
    }

    private MediaUploadOutcome Merge(
        MediaUploadOutcome state, CardMediaUploadResult result, bool isImage)
    {
        var revoked = state.Revoked || !plus.Current.IsActive;
        return state with
        {
            ImageUploaded = isImage ? result.IsSuccess : state.ImageUploaded,
            AudioUploaded = isImage ? state.AudioUploaded : result.IsSuccess,
            QuotaExceeded = state.QuotaExceeded || result.Status == CardMediaUploadStatus.QuotaExceeded,
            Revoked = revoked,
            UsedBytes = result.MaxBytes > 0 ? result.UsedBytes : state.UsedBytes,
            MaxBytes = result.MaxBytes > 0 ? result.MaxBytes : state.MaxBytes,
            Error = state.Error ?? result.Error
        };
    }
}
