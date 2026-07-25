using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Mpv;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Interaction;

public sealed class WordActionService(
    JitenApiClient api,
    ParseCache cache,
    StatusOverlay status,
    ILogger logger)
{
    public async Task<bool> SetStateAsync(
        int wordId, byte readingIndex, PopupAction action,
        KnownState currentState, string subtitleText,
        MpvIpcClient ipc, CancellationToken ct)
    {
        var resolved = ResolveAction(action, currentState);
        if (resolved is null) return false;
        var (stateAction, newState, label) = resolved.Value;

        try
        {
            var response = await api.SetVocabularyStateAsync(wordId, readingIndex, stateAction, ct);
            if (!response.Success) return false;

            cache.UpdateWordState(wordId, readingIndex, newState);

            var entry = cache.GetOrDefault(subtitleText);
            var spelling = entry?.VocabDetails.GetValueOrDefault((wordId, readingIndex))?.Spelling ?? "word";
            await status.ShowAsync(ipc, $"{spelling}: {label}", 2000, ct);

            logger.LogInformation("{Action} word {WordId}:{ReadingIndex}",
                stateAction, wordId, readingIndex);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to {Action} word {WordId}:{ReadingIndex}",
                action, wordId, readingIndex);
            return false;
        }
    }

    private static (string ApiAction, KnownState NewState, string Label)? ResolveAction(
        PopupAction action, KnownState currentState)
    {
        return action switch
        {
            PopupAction.NeverForget when currentState == KnownState.Mastered =>
                (VocabularyStateActions.NeverForgetRemove, KnownState.New, "Unmastered"),
            PopupAction.NeverForget =>
                (VocabularyStateActions.NeverForgetAdd, KnownState.Mastered, "Mastered"),
            PopupAction.Blacklist when currentState == KnownState.Blacklisted =>
                (VocabularyStateActions.BlacklistRemove, KnownState.New, "Removed Blacklist"),
            PopupAction.Blacklist =>
                (VocabularyStateActions.BlacklistAdd, KnownState.Blacklisted, "Blacklisted"),
            PopupAction.Suspend when currentState == KnownState.Suspended =>
                (VocabularyStateActions.SuspendRemove, KnownState.New, "Resumed"),
            PopupAction.Suspend =>
                (VocabularyStateActions.SuspendAdd, KnownState.Suspended, "Suspended"),
            PopupAction.Forget =>
                (VocabularyStateActions.ForgetAdd, KnownState.New, "Forgotten"),
            _ => null
        };
    }
}
