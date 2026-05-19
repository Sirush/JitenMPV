using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Interaction;

namespace JitenMPV.App.ViewModels;

public partial class PopupViewModel : ViewModelBase
{
    [ObservableProperty] private string _spelling = "";
    [ObservableProperty] private string _reading = "";
    [ObservableProperty] private int _frequencyRank;
    [ObservableProperty] private string _partsOfSpeech = "";
    [ObservableProperty] private string _pitchAccents = "";
    [ObservableProperty] private List<string> _meanings = [];
    [ObservableProperty] private string _conjugation = "";
    [ObservableProperty] private string _stateLabel = "";
    [ObservableProperty] private bool _showReading;
    [ObservableProperty] private bool _showFrequency;
    [ObservableProperty] private bool _showPartsOfSpeech;
    [ObservableProperty] private bool _showPitch;
    [ObservableProperty] private bool _showConjugation;

    [ObservableProperty] private bool _showNeverForget;
    [ObservableProperty] private bool _showBlacklist;
    [ObservableProperty] private bool _showSuspend;
    [ObservableProperty] private bool _showForget;
    [ObservableProperty] private bool _showStateActions;
    [ObservableProperty] private string _masterLabel = "Master";
    [ObservableProperty] private string _blacklistLabel = "Blacklist";
    [ObservableProperty] private string _suspendLabel = "Suspend";

    [ObservableProperty] private bool _showReview;
    [ObservableProperty] private bool _showHardEasy;

    public event Action<PopupAction>? ActionClicked;

    public ICommand NeverForgetCommand { get; }
    public ICommand BlacklistCommand { get; }
    public ICommand SuspendCommand { get; }
    public ICommand ForgetCommand { get; }

    public ICommand ReviewAgainCommand { get; }
    public ICommand ReviewHardCommand { get; }
    public ICommand ReviewGoodCommand { get; }
    public ICommand ReviewEasyCommand { get; }

    public PopupViewModel()
    {
        NeverForgetCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.NeverForget));
        BlacklistCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.Blacklist));
        SuspendCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.Suspend));
        ForgetCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.Forget));
        ReviewAgainCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.ReviewAgain));
        ReviewHardCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.ReviewHard));
        ReviewGoodCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.ReviewGood));
        ReviewEasyCommand = new RelayCommand(() => ActionClicked?.Invoke(PopupAction.ReviewEasy));
    }

    public void Update(PopupData data)
    {
        Spelling = data.Spelling;
        Reading = data.Reading;
        FrequencyRank = data.FrequencyRank;
        ShowReading = !string.IsNullOrEmpty(data.Reading) && data.Reading != data.Spelling;
        ShowFrequency = data.FrequencyRank > 0;

        ShowPartsOfSpeech = data.PartsOfSpeech.Count > 0;
        if (ShowPartsOfSpeech)
            PartsOfSpeech = string.Join(", ", data.PartsOfSpeech);

        ShowPitch = data.PitchAccents.Count > 0;
        if (ShowPitch)
            PitchAccents = string.Join(", ", data.PitchAccents);

        Meanings = data.MeaningsChunks
            .Select((chunk, i) => $"{i + 1}. {string.Join("; ", chunk)}")
            .ToList();

        ShowConjugation = data.Conjugations.Count > 0;
        if (ShowConjugation)
            Conjugation = string.Join(" → ", data.Conjugations);

        StateLabel = data.State.ToString();

        ShowNeverForget = data.ShowNeverForget;
        ShowBlacklist = data.ShowBlacklist;
        ShowSuspend = data.ShowSuspend;
        ShowForget = data.ShowForget;
        ShowStateActions = data.ShowStateActions;

        MasterLabel = data.IsNeverForgotten ? "Un-master" : "Master";
        BlacklistLabel = data.IsBlacklisted ? "Un-blacklist" : "Blacklist";
        SuspendLabel = data.IsSuspended ? "Resume" : "Suspend";

        ShowReview = data.ShowReview;
        ShowHardEasy = !data.UseTwoGrades;
    }
}
