using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Interaction;

public sealed class PopupDataBuilder(PluginSettings settings, MiningService mining)
{
    private volatile PluginSettings _settings = settings;

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    public PopupData Build(ReaderWord word, ReaderToken token, KnownState? stateOverride = null)
    {
        var settings = _settings;
        var state = stateOverride ?? KnownStates.Collapse(word.KnownState);

        bool isMastered = state == KnownState.Mastered;
        bool isBlacklisted = state == KnownState.Blacklisted;
        bool isSuspended = state == KnownState.Suspended;

        bool hasCard = state is not KnownState.New;

        // Redundant cards are view-only: they are covered by another card and cannot be mined.
        bool showMine = settings.MiningEnabled && state != KnownState.Redundant;

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

            ShowMine = showMine,
            IsMined = mining.IsInTargetList(word.WordId, word.ReadingIndex, word.StudyDeckIds),
            DeckOptions = showMine && mining.ResolveTargetDeck() is null
                ? [..mining.Decks.Select(d => new DeckOption(d.UserStudyDeckId, d.Name))]
                : [],
            DeckMembership = settings.PopupShowDeckMembership
                ? DeckMembership.Build(
                    mining.EffectiveDeckIds(word.WordId, word.ReadingIndex, word.StudyDeckIds),
                    mining.Decks)
                : [],

            ShowReview = settings.ReviewsEnabled && settings.PopupShowReview,
            UseTwoGrades = settings.PopupUseTwoGrades,

            PopupBgColor = settings.PopupBgColor,
            PopupBgOpacity = settings.PopupBgOpacity,
            FontScale = settings.PopupFontScale,
            PositionMode = settings.PopupPosition
        };
    }
}
