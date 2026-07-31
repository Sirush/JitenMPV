using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Interaction;
using JitenMPV.Core.Text;

namespace JitenMPV.App.ViewModels;

/// Carries the picker command per row so the item template needs no ancestor binding.
public sealed record DeckOptionItem(DeckOption Option, ICommand Pick)
{
    public string Name => Option.Name;
}

/// <param name="Ruby">
/// Never empty, and blanked with an ideographic rather than an ASCII space: the placeholder has to
/// resolve through the same Japanese fallback as real ruby, or its line box measures shorter and the
/// okurigana rides above the annotated kanji.
/// </param>
public sealed record FuriganaItem(string Text, string Ruby)
{
    public static FuriganaItem From(FuriganaSegment segment)
        => new(segment.Text, segment.Ruby.Length > 0 ? segment.Ruby : "　");
}

public sealed record StateBadgeItem(KnownState State)
{
    // Matches the Reader extension's $states palette; Young/Mature/Mastered are not in that map,
    // so they take its learning/known greens.
    private static readonly Dictionary<KnownState, IBrush> Palette = new()
    {
        [KnownState.New] = new SolidColorBrush(Color.Parse("#D8B9FA")),
        [KnownState.Young] = new SolidColorBrush(Color.Parse("#5EA780")),
        [KnownState.Mature] = new SolidColorBrush(Color.Parse("#70C000")),
        [KnownState.Mastered] = new SolidColorBrush(Color.Parse("#70C000")),
        [KnownState.Blacklisted] = new SolidColorBrush(Color.Parse("#777777")),
        [KnownState.Due] = new SolidColorBrush(Color.Parse("#FF4500")),
        [KnownState.Redundant] = new SolidColorBrush(Color.Parse("#4B9FFF")),
        [KnownState.Suspended] = new SolidColorBrush(Color.Parse("#AAAAAA"))
    };

    private static readonly IBrush Fallback = new SolidColorBrush(Color.Parse("#BBBBBB"));

    public string Text => State.ToString().ToLowerInvariant();
    public IBrush Foreground => Palette.GetValueOrDefault(State, Fallback);
}

public sealed record DeckMembershipItem(DeckMembershipRow Row)
{
    // Matches the Reader extension's $deck-types palette.
    private static readonly IBrush WordListBrush = new SolidColorBrush(Color.FromArgb(230, 80, 200, 120));
    private static readonly IBrush MediaDeckBrush = new SolidColorBrush(Color.FromArgb(230, 90, 160, 255));
    private static readonly IBrush FrequencyBrush = new SolidColorBrush(Color.FromArgb(230, 190, 130, 255));

    public string Label => Row.Label;
    public string Names => Row.Names;
    public bool HasNames => !string.IsNullOrWhiteSpace(Row.Names);

    public IBrush Dot => Row.Type switch
    {
        StudyDeckType.StaticWordList => WordListBrush,
        StudyDeckType.MediaDeck => MediaDeckBrush,
        StudyDeckType.GlobalDynamic => FrequencyBrush,
        _ => MediaDeckBrush
    };
}

public partial class PopupViewModel : ViewModelBase
{
    [ObservableProperty] private IBrush _popupBackground = new SolidColorBrush(Color.FromArgb(200, 0x1A, 0x1A, 0x1A));
    [ObservableProperty] private string _spelling = "";
    [ObservableProperty] private string _reading = "";
    [ObservableProperty] private int _frequencyRank;
    [ObservableProperty] private string _partsOfSpeech = "";
    [ObservableProperty] private string _pitchAccents = "";
    [ObservableProperty] private List<string> _meanings = [];
    [ObservableProperty] private string _conjugation = "";
    [ObservableProperty] private List<StateBadgeItem> _stateBadges = [];
    [ObservableProperty] private bool _headwordLinkEnabled;
    [ObservableProperty] private bool _moveActionsBottom;
    [ObservableProperty] private bool _showReading;
    [ObservableProperty] private bool _showFurigana;
    [ObservableProperty] private List<FuriganaItem> _furiganaSegments = [];
    [ObservableProperty] private bool _showFrequency;
    [ObservableProperty] private bool _showPartsOfSpeech;
    [ObservableProperty] private bool _showPitch;
    [ObservableProperty] private bool _showPitchDiagrams;
    [ObservableProperty] private List<PitchDiagramRow> _pitchDiagrams = [];
    [ObservableProperty] private bool _showConjugation;

    [ObservableProperty] private bool _showNeverForget;
    [ObservableProperty] private bool _showBlacklist;
    [ObservableProperty] private bool _showSuspend;
    [ObservableProperty] private bool _showForget;
    [ObservableProperty] private bool _showStateActions;
    [ObservableProperty] private bool _showActionRow;
    [ObservableProperty] private string _masterLabel = "Master";
    [ObservableProperty] private string _blacklistLabel = "Blacklist";
    [ObservableProperty] private string _suspendLabel = "Suspend";

    [ObservableProperty] private bool _showMine;
    [ObservableProperty] private bool _isMined;
    [ObservableProperty] private string _mineLabel = "Deck +";
    [ObservableProperty] private bool _showDeckPicker;
    [ObservableProperty] private bool _isDeckPickerOpen;
    [ObservableProperty] private bool _showDeckMembership;
    [ObservableProperty] private List<DeckMembershipItem> _deckMembership = [];
    [ObservableProperty] private List<DeckOptionItem> _deckOptions = [];

    [ObservableProperty] private bool _showRotate;
    [ObservableProperty] private bool _showRotateBackward;
    [ObservableProperty] private string _rotateForwardLabel = "";
    [ObservableProperty] private string _rotateBackwardLabel = "";

    [ObservableProperty] private bool _showReview;
    [ObservableProperty] private bool _showHardEasy;

    private string? _lastBgColor;
    private int _lastBgOpacity = -1;
    private (int WordId, byte ReadingIndex)? _lastWord;

    public void CloseDeckPicker() => IsDeckPickerOpen = false;

    public event Action<PopupAction>? ActionClicked;
    public event Action<int>? DeckSelected;

    public ICommand MineCommand { get; }
    public ICommand PickDeckCommand { get; }
    public ICommand NeverForgetCommand { get; }
    public ICommand BlacklistCommand { get; }
    public ICommand SuspendCommand { get; }
    public ICommand ForgetCommand { get; }

    public ICommand RotateForwardCommand { get; }
    public ICommand RotateBackwardCommand { get; }
    public ICommand OpenHeadwordCommand { get; }

    public ICommand ReviewAgainCommand { get; }
    public ICommand ReviewHardCommand { get; }
    public ICommand ReviewGoodCommand { get; }
    public ICommand ReviewEasyCommand { get; }

    public PopupViewModel()
    {
        // With a picker available the button toggles it; otherwise it mines to the configured deck.
        MineCommand = new RelayCommand(() =>
        {
            if (ShowDeckPicker)
                IsDeckPickerOpen = !IsDeckPickerOpen;
            else
                ActionClicked?.Invoke(PopupAction.Mine);
        });
        PickDeckCommand = new RelayCommand<DeckOption>(deck =>
        {
            if (deck is null) return;
            IsDeckPickerOpen = false;
            DeckSelected?.Invoke(deck.DeckId);
        });
        NeverForgetCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.NeverForget));
        BlacklistCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.Blacklist));
        SuspendCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.Suspend));
        ForgetCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.Forget));
        RotateForwardCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.RotateForward));
        RotateBackwardCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.RotateBackward));
        OpenHeadwordCommand = new RelayCommand(OpenHeadword);
        ReviewAgainCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.ReviewAgain));
        ReviewHardCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.ReviewHard));
        ReviewGoodCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.ReviewGood));
        ReviewEasyCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.ReviewEasy));
    }

    private void OpenHeadword()
    {
        if (!HeadwordLinkEnabled || _lastWord is not { } word) return;

        try
        {
            Process.Start(new ProcessStartInfo(
                $"https://jiten.moe/vocabulary/{word.WordId}/{word.ReadingIndex}")
            {
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // No browser association, or the shell refused: nothing useful to show over the video.
        }
    }

    public void Update(PopupData data)
    {
        Spelling = data.Spelling;
        // With ruby asked for, a reading it could not annotate still reads better as bare kana.
        Reading = data.ShowFurigana ? FuriganaParser.ToKana(data.Reading) : data.Reading;
        FrequencyRank = data.FrequencyRank;
        var segments = data.ShowFurigana
            ? FuriganaParser.ForSpelling(data.Spelling, data.Reading)
            : null;
        ShowFurigana = segments is not null;
        FuriganaSegments = segments is null
            ? []
            : [..segments.Select(FuriganaItem.From)];

        ShowReading = !ShowFurigana && !string.IsNullOrEmpty(Reading) && Reading != data.Spelling;
        ShowFrequency = data.FrequencyRank > 0;

        ShowPartsOfSpeech = data.PartsOfSpeech.Count > 0;
        PartsOfSpeech = ShowPartsOfSpeech ? string.Join(", ", data.PartsOfSpeech) : "";

        PitchDiagrams = [..data.PitchDiagrams];
        ShowPitchDiagrams = PitchDiagrams.Count > 0;
        // The numeric line is the fallback when diagrams are off or the reading yielded none.
        ShowPitch = !ShowPitchDiagrams && data.PitchAccents.Count > 0;
        PitchAccents = ShowPitch ? string.Join(", ", data.PitchAccents) : "";

        Meanings = data.MeaningsChunks
            .Select((chunk, i) => $"{i + 1}. {string.Join("; ", chunk)}")
            .ToList();

        ShowConjugation = data.Conjugations.Count > 0;
        Conjugation = ShowConjugation ? string.Join(" → ", data.Conjugations) : "";

        StateBadges = [..data.States.Select(s => new StateBadgeItem(s))];
        HeadwordLinkEnabled = data.HeadwordLinkEnabled;
        MoveActionsBottom = data.MoveActionsBottom;

        ShowNeverForget = data.ShowNeverForget;
        ShowBlacklist = data.ShowBlacklist;
        ShowSuspend = data.ShowSuspend;
        ShowForget = data.ShowForget;
        ShowStateActions = data.ShowStateActions;
        ShowActionRow = data.ShowActionRow;

        MasterLabel = data.IsNeverForgotten ? "Un-master" : "Master";
        BlacklistLabel = data.IsBlacklisted ? "Un-blacklist" : "Blacklist";
        SuspendLabel = data.IsSuspended ? "Resume" : "Suspend";

        ShowMine = data.ShowMine;
        IsMined = data.IsMined;
        MineLabel = data.IsMined ? "In list" : "Deck +";
        ShowDeckPicker = data.ShowDeckPicker;
        DeckOptions = [..data.DeckOptions.Select(o => new DeckOptionItem(o, PickDeckCommand))];

        DeckMembership = [..data.DeckMembership.Select(r => new DeckMembershipItem(r))];
        ShowDeckMembership = DeckMembership.Count > 0;

        // A picker left open would otherwise carry over to whatever word is shown next.
        var word = (data.WordId, data.ReadingIndex);
        if (!ShowDeckPicker || _lastWord != word) IsDeckPickerOpen = false;
        _lastWord = word;

        ShowRotate = data.ShowRotate;
        ShowRotateBackward = data.ShowRotateBackward;
        RotateForwardLabel = data.RotateForwardLabel;
        RotateBackwardLabel = data.RotateBackwardLabel;

        ShowReview = data.ShowReview;
        ShowHardEasy = !data.UseTwoGrades;

        if (data.PopupBgColor != _lastBgColor || data.PopupBgOpacity != _lastBgOpacity)
        {
            if (Color.TryParse(data.PopupBgColor, out var bgColor))
            {
                _lastBgColor = data.PopupBgColor;
                _lastBgOpacity = data.PopupBgOpacity;
                PopupBackground = new SolidColorBrush(Color.FromArgb((byte)data.PopupBgOpacity, bgColor.R, bgColor.G, bgColor.B));
            }
        }
    }
}
