using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;
using JitenMPV.Core.Pitch;

namespace JitenMPV.Core.Interaction;

public sealed class PopupDataBuilder(PluginSettings settings, MiningService mining, RotationService rotation)
{
    private volatile PluginSettings _settings = settings;

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    /// The cached ReaderWord is never mutated, so its state array goes stale as soon as an action
    /// changes the card; from then on only the collapsed state describes it.
    private static IReadOnlyList<KnownState> ResolveStates(ReaderWord word, KnownState state)
        => word.KnownState.Count > 0 && KnownStates.Collapse(word.KnownState) == state
            ? word.KnownState
            : [state];

    private static IReadOnlyList<PitchDiagramRow> BuildDiagrams(ReaderWord word, PluginSettings settings)
    {
        List<PitchDiagramRow> rows = [];
        foreach (var accent in word.PitchAccents)
        {
            if (PitchAccent.BuildDiagram(word.Reading, accent) is not { } diagram) continue;

            var color = settings.PitchStyles?.GetValueOrDefault(diagram.Class.ToString())?.TextColor
                        ?? PitchAccent.DefaultColor(diagram.Class);
            rows.Add(new PitchDiagramRow(diagram, color));
        }
        return rows;
    }

    public PopupData Build(ReaderWord word, ReaderToken token, KnownState? stateOverride = null)
    {
        var settings = _settings;
        var state = stateOverride ?? KnownStates.Collapse(word.KnownState);

        bool isMastered = state == KnownState.Mastered;
        bool isBlacklisted = state == KnownState.Blacklisted;
        bool isSuspended = state == KnownState.Suspended;

        bool hasCard = state is not KnownState.New;

        // A redundant word is covered by another card and has none of its own, so the popup is
        // view-only for it: every actionable row is hidden regardless of configuration.
        bool actionable = state != KnownState.Redundant;
        bool showMine = settings.MiningEnabled && actionable;

        bool showRotate = actionable && rotation.ShowActions;
        PopupAction? rotateNext = null;
        PopupAction? rotatePrevious = null;
        if (showRotate)
        {
            rotation.TryGetNext(state, 1, out rotateNext);
            rotation.TryGetNext(state, -1, out rotatePrevious);
        }
        bool rotateIsOneWay = rotateNext == rotatePrevious;

        return new PopupData
        {
            Spelling = word.Spelling,
            Reading = word.Reading,
            ShowFurigana = settings.PopupFurigana,
            FrequencyRank = settings.PopupShowFrequency ? word.FrequencyRank : 0,
            PartsOfSpeech = word.PartsOfSpeech,
            PitchAccents = settings.PopupShowPitch ? word.PitchAccents : [],
            PitchDiagrams = settings.PopupShowPitch && settings.PopupPitchDiagram
                ? BuildDiagrams(word, settings)
                : [],
            MeaningsChunks = word.MeaningsChunks.Count > settings.PopupMaxMeanings
                ? word.MeaningsChunks.Take(settings.PopupMaxMeanings).ToList()
                : word.MeaningsChunks,
            Conjugations = settings.PopupShowConjugation ? token.Conjugations : [],
            State = state,
            States = ResolveStates(word, state),
            WordId = word.WordId,
            ReadingIndex = word.ReadingIndex,
            HeadwordLinkEnabled = !settings.PopupDisableHeadwordLink,
            MoveActionsBottom = settings.PopupMoveActionsBottom,

            ShowNeverForget = actionable && settings.PopupShowStateActions && settings.PopupShowNeverForget,
            ShowBlacklist = actionable && settings.PopupShowStateActions && settings.PopupShowBlacklist,
            ShowSuspend = actionable && settings.PopupShowStateActions && settings.PopupShowSuspend,
            ShowForget = actionable && settings.PopupShowStateActions && settings.PopupShowForget && hasCard,
            IsNeverForgotten = isMastered,
            IsBlacklisted = isBlacklisted,
            IsSuspended = isSuspended,

            ShowMine = showMine,
            IsMined = mining.IsInTargetList(word.WordId, word.ReadingIndex, word.StudyDeckIds),
            DeckOptions = showMine && mining.ResolveTargetDeck() is null
                ? [..mining.WordListDecks.Select(d => new DeckOption(d.UserStudyDeckId, d.Name))]
                : [],
            DeckMembership = settings.PopupShowDeckMembership
                ? DeckMembership.Build(
                    mining.EffectiveDeckIds(word.WordId, word.ReadingIndex, word.StudyDeckIds),
                    mining.Decks)
                : [],

            ShowRotate = showRotate,
            RotateForwardLabel = rotateIsOneWay
                ? RotationService.Label(rotateNext)
                : $"{RotationService.Label(rotateNext)} →",
            RotateBackwardLabel = $"← {RotationService.Label(rotatePrevious)}",
            ShowRotateBackward = !rotateIsOneWay,

            ShowReview = actionable && settings.ReviewsEnabled && settings.PopupShowReview,
            UseTwoGrades = settings.PopupUseTwoGrades,

            PopupBgColor = settings.PopupBgColor,
            PopupBgOpacity = settings.PopupBgOpacity,
            FontScale = settings.PopupFontScale,
            PositionMode = settings.PopupPosition,
            FixedAnchor = settings.PopupFixedAnchor,
            OffsetPx = settings.PopupOffsetPx,
            MaxWidthPx = settings.PopupMaxWidthPx
        };
    }
}
