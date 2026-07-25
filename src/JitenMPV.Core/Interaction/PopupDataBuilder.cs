using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Interaction;

public sealed class PopupDataBuilder(PluginSettings settings)
{
    private volatile PluginSettings _settings = settings;

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    public PopupData Build(ReaderWord word, ReaderToken token, KnownState? stateOverride = null)
    {
        var settings = _settings;
        var state = stateOverride ?? (word.KnownState.Count > 0 ? word.KnownState[0] : KnownState.New);

        bool isMastered = state == KnownState.Mastered;
        bool isBlacklisted = state == KnownState.Blacklisted;
        bool isSuspended = state == KnownState.Redundant;

        bool hasCard = state is not KnownState.New;

        return new PopupData
        {
            Spelling = word.Spelling,
            Reading = word.Reading,
            FrequencyRank = settings.PopupShowFrequency ? word.FrequencyRank : 0,
            PartsOfSpeech = word.PartsOfSpeech,
            PitchAccents = settings.PopupShowPitch ? word.PitchAccents : [],
            MeaningsChunks = word.MeaningsChunks.Count > settings.PopupMaxMeanings
                ? word.MeaningsChunks.Take(settings.PopupMaxMeanings).ToList()
                : word.MeaningsChunks,
            Conjugations = settings.PopupShowConjugation ? token.Conjugations : [],
            State = state,
            WordId = word.WordId,
            ReadingIndex = word.ReadingIndex,

            ShowNeverForget = settings.PopupShowStateActions && settings.PopupShowNeverForget,
            ShowBlacklist = settings.PopupShowStateActions && settings.PopupShowBlacklist,
            ShowSuspend = settings.PopupShowStateActions && settings.PopupShowSuspend,
            ShowForget = settings.PopupShowStateActions && settings.PopupShowForget && hasCard,
            IsNeverForgotten = isMastered,
            IsBlacklisted = isBlacklisted,
            IsSuspended = isSuspended,

            ShowReview = settings.PopupShowReview,
            UseTwoGrades = settings.PopupUseTwoGrades,

            PopupBgColor = settings.PopupBgColor,
            PopupBgOpacity = settings.PopupBgOpacity,
            FontScale = settings.PopupFontScale,
            PositionMode = settings.PopupPosition
        };
    }
}
