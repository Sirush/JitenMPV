using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;
using JitenMPV.Core.Theming;

namespace JitenMPV.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _apiBaseUrl = "";
    [ObservableProperty] private int _apiTimeoutSeconds;

    [ObservableProperty] private string _selectedTheme = "";
    [ObservableProperty] private string _fontFamily = "";
    [ObservableProperty] private int _fontSize;
    [ObservableProperty] private double _borderSize;

    [ObservableProperty] private int _subtitleAlignment;
    [ObservableProperty] private int _subtitleMarginX;
    [ObservableProperty] private int _subtitleMarginY;

    [ObservableProperty] private bool _iPlusOneEnabled;
    [ObservableProperty] private int _iPlusOneMinTokens;
    [ObservableProperty] private int _iPlusOneMaxFrequencyRank;

    [ObservableProperty] private bool _frequencyMarkingEnabled;
    [ObservableProperty] private int _frequencyTopN;
    [ObservableProperty] private bool _frequencyMarkAllStates;

    [ObservableProperty] private bool _blurEnabled;
    [ObservableProperty] private double _blurStrength;
    [ObservableProperty] private bool _blurRevealOnHover;
    [ObservableProperty] private int _blurRevealDelayMs;
    [ObservableProperty] private bool _blurNew;
    [ObservableProperty] private bool _blurYoung;
    [ObservableProperty] private bool _blurMature;
    [ObservableProperty] private bool _blurBlacklisted;
    [ObservableProperty] private bool _blurDue;
    [ObservableProperty] private bool _blurMastered;
    [ObservableProperty] private bool _blurRedundant;

    [ObservableProperty] private PopupTriggerMode _popupTrigger;
    [ObservableProperty] private int _popupHoverDelayMs;
    [ObservableProperty] private bool _popupAutoHide;
    [ObservableProperty] private int _popupAutoHideDelayMs;
    [ObservableProperty] private bool _popupHideAfterAction;
    [ObservableProperty] private PopupPositionMode _popupPosition;
    [ObservableProperty] private double _popupFontScale;
    [ObservableProperty] private int _popupBgOpacity;
    [ObservableProperty] private string _popupBgColor = "";
    [ObservableProperty] private int _popupMaxMeanings;
    [ObservableProperty] private bool _popupShowPitch;
    [ObservableProperty] private bool _popupShowFrequency;
    [ObservableProperty] private bool _popupShowConjugation;
    [ObservableProperty] private bool _popupShowStateActions;
    [ObservableProperty] private bool _popupShowNeverForget;
    [ObservableProperty] private bool _popupShowBlacklist;
    [ObservableProperty] private bool _popupShowSuspend;
    [ObservableProperty] private bool _popupShowForget;
    [ObservableProperty] private bool _popupShowReview;
    [ObservableProperty] private bool _popupUseTwoGrades;

    [ObservableProperty] private bool _autopauseEnabled;
    [ObservableProperty] private int _autopauseDelayMs;

    [ObservableProperty] private bool _miningEnabled;
    [ObservableProperty] private bool _miningCaptureSentence;

    [ObservableProperty] private bool _reviewsEnabled;

    [ObservableProperty] private int _cacheSize;
    [ObservableProperty] private bool _preparseEnabled;
    [ObservableProperty] private int _preparseBatchSize;
    [ObservableProperty] private bool _statusOverlayEnabled;
    [ObservableProperty] private bool _debugLogging;
    [ObservableProperty] private int _mouseZonePercent;

    [ObservableProperty] private string _keybindReviewAgain = "";
    [ObservableProperty] private string _keybindReviewHard = "";
    [ObservableProperty] private string _keybindReviewGood = "";
    [ObservableProperty] private string _keybindReviewEasy = "";
    [ObservableProperty] private string _keybindNeverForget = "";
    [ObservableProperty] private string _keybindBlacklist = "";
    [ObservableProperty] private string _keybindSuspend = "";
    [ObservableProperty] private string _keybindForget = "";

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isApiKeyVisible;

    [ObservableProperty] private string _importCode = "";
    [ObservableProperty] private string _importStatus = "";

    public ObservableCollection<StateStyleViewModel> CustomStateStyles { get; } = [];
    private string _previousTheme = "Default";

    public ObservableCollection<string> AvailableThemes { get; } =
    [
        "Default", "High Contrast", "Monochrome", "Subtle", "Underline", "Toy Box", "Custom"
    ];

    public ObservableCollection<string> AvailableFonts { get; } =
    [
        "Yu Gothic", "Yu Mincho", "Meiryo", "MS Gothic", "MS Mincho",
        "Noto Sans JP", "Noto Serif JP", "BIZ UDGothic", "BIZ UDMincho"
    ];

    public bool IsCustomTheme => SelectedTheme == "Custom";

    private static int OpacityToPercent(int opacity255) => (int)Math.Round(opacity255 * 100.0 / 255);
    private static int PercentToOpacity(int pct) => (int)Math.Round(pct * 255.0 / 100);

    public SettingsViewModel() : this(new PluginSettings()) { }

    public SettingsViewModel(PluginSettings s)
    {
        ApiKey = s.ApiKey ?? "";
        ApiBaseUrl = s.ApiBaseUrl;
        ApiTimeoutSeconds = s.ApiTimeoutSeconds;
        SelectedTheme = s.Theme;
        FontFamily = s.FontFamily;
        FontSize = s.FontSize;
        BorderSize = s.BorderSize;
        SubtitleAlignment = s.SubtitleAlignment;
        SubtitleMarginX = s.SubtitleMarginX;
        SubtitleMarginY = s.SubtitleMarginY;
        IPlusOneEnabled = s.IPlusOneEnabled;
        IPlusOneMinTokens = s.IPlusOneMinTokens;
        IPlusOneMaxFrequencyRank = s.IPlusOneMaxFrequencyRank;
        FrequencyMarkingEnabled = s.FrequencyMarkingEnabled;
        FrequencyTopN = s.FrequencyTopN;
        FrequencyMarkAllStates = s.FrequencyMarkAllStates;
        BlurEnabled = s.BlurEnabled;
        BlurStrength = s.BlurStrength;
        BlurRevealOnHover = s.BlurRevealOnHover;
        BlurRevealDelayMs = s.BlurRevealDelayMs;
        ApplyBlurStates(s.BlurStates);
        PopupTrigger = s.PopupTrigger;
        PopupHoverDelayMs = s.PopupHoverDelayMs;
        PopupAutoHide = s.PopupAutoHide;
        PopupAutoHideDelayMs = s.PopupAutoHideDelayMs;
        PopupHideAfterAction = s.PopupHideAfterAction;
        PopupPosition = s.PopupPosition;
        PopupFontScale = s.PopupFontScale;
        PopupBgOpacity = OpacityToPercent(s.PopupBgOpacity);
        PopupBgColor = s.PopupBgColor;
        PopupMaxMeanings = s.PopupMaxMeanings;
        PopupShowPitch = s.PopupShowPitch;
        PopupShowFrequency = s.PopupShowFrequency;
        PopupShowConjugation = s.PopupShowConjugation;
        PopupShowStateActions = s.PopupShowStateActions;
        PopupShowNeverForget = s.PopupShowNeverForget;
        PopupShowBlacklist = s.PopupShowBlacklist;
        PopupShowSuspend = s.PopupShowSuspend;
        PopupShowForget = s.PopupShowForget;
        PopupShowReview = s.PopupShowReview;
        PopupUseTwoGrades = s.PopupUseTwoGrades;
        AutopauseEnabled = s.AutopauseEnabled;
        AutopauseDelayMs = s.AutopauseDelayMs;
        MiningEnabled = s.MiningEnabled;
        MiningCaptureSentence = s.MiningCaptureSentence;
        ReviewsEnabled = s.ReviewsEnabled;
        CacheSize = s.CacheSize;
        PreparseEnabled = s.PreparseEnabled;
        PreparseBatchSize = s.PreparseBatchSize;
        StatusOverlayEnabled = s.StatusOverlayEnabled;
        DebugLogging = s.DebugLogging;
        MouseZonePercent = s.MouseZonePercent;

        if (s.PopupKeybinds is { } kb)
        {
            KeybindReviewAgain = kb.GetValueOrDefault("ReviewAgain", "");
            KeybindReviewHard = kb.GetValueOrDefault("ReviewHard", "");
            KeybindReviewGood = kb.GetValueOrDefault("ReviewGood", "");
            KeybindReviewEasy = kb.GetValueOrDefault("ReviewEasy", "");
            KeybindNeverForget = kb.GetValueOrDefault("NeverForget", "");
            KeybindBlacklist = kb.GetValueOrDefault("Blacklist", "");
            KeybindSuspend = kb.GetValueOrDefault("Suspend", "");
            KeybindForget = kb.GetValueOrDefault("Forget", "");
        }

        _previousTheme = s.Theme == "Custom" ? "Default" : s.Theme;
        if (s.Theme == "Custom" && s.CustomThemeColors is { Count: > 0 } custom)
            InitCustomStylesFromSettings(custom);
    }

    partial void OnSelectedThemeChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomTheme));
        if (value == "Custom" && CustomStateStyles.Count == 0)
            InitCustomStylesFromPreset(_previousTheme);
        if (value != "Custom")
            _previousTheme = value;
    }

    public PluginSettings ToPluginSettings()
    {
        var blurStates = new List<int>();
        if (BlurNew) blurStates.Add((int)KnownState.New);
        if (BlurYoung) blurStates.Add((int)KnownState.Young);
        if (BlurMature) blurStates.Add((int)KnownState.Mature);
        if (BlurBlacklisted) blurStates.Add((int)KnownState.Blacklisted);
        if (BlurDue) blurStates.Add((int)KnownState.Due);
        if (BlurMastered) blurStates.Add((int)KnownState.Mastered);
        if (BlurRedundant) blurStates.Add((int)KnownState.Redundant);

        return new PluginSettings
        {
            ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
            ApiBaseUrl = ApiBaseUrl,
            ApiTimeoutSeconds = ApiTimeoutSeconds,
            Theme = SelectedTheme,
            FontFamily = FontFamily,
            FontSize = FontSize,
            BorderSize = BorderSize,
            SubtitleAlignment = SubtitleAlignment,
            SubtitleMarginX = SubtitleMarginX,
            SubtitleMarginY = SubtitleMarginY,
            IPlusOneEnabled = IPlusOneEnabled,
            IPlusOneMinTokens = IPlusOneMinTokens,
            IPlusOneMaxFrequencyRank = IPlusOneMaxFrequencyRank,
            FrequencyMarkingEnabled = FrequencyMarkingEnabled,
            FrequencyTopN = FrequencyTopN,
            FrequencyMarkAllStates = FrequencyMarkAllStates,
            BlurEnabled = BlurEnabled,
            BlurStrength = BlurStrength,
            BlurRevealOnHover = BlurRevealOnHover,
            BlurRevealDelayMs = BlurRevealDelayMs,
            BlurStates = blurStates,
            PopupTrigger = PopupTrigger,
            PopupHoverDelayMs = PopupHoverDelayMs,
            PopupAutoHide = PopupAutoHide,
            PopupAutoHideDelayMs = PopupAutoHideDelayMs,
            PopupHideAfterAction = PopupHideAfterAction,
            PopupPosition = PopupPosition,
            PopupFontScale = PopupFontScale,
            PopupBgOpacity = PercentToOpacity(PopupBgOpacity),
            PopupBgColor = PopupBgColor,
            PopupMaxMeanings = PopupMaxMeanings,
            PopupShowPitch = PopupShowPitch,
            PopupShowFrequency = PopupShowFrequency,
            PopupShowConjugation = PopupShowConjugation,
            PopupShowStateActions = PopupShowStateActions,
            PopupShowNeverForget = PopupShowNeverForget,
            PopupShowBlacklist = PopupShowBlacklist,
            PopupShowSuspend = PopupShowSuspend,
            PopupShowForget = PopupShowForget,
            PopupShowReview = PopupShowReview,
            PopupUseTwoGrades = PopupUseTwoGrades,
            AutopauseEnabled = AutopauseEnabled,
            AutopauseDelayMs = AutopauseDelayMs,
            MiningEnabled = MiningEnabled,
            MiningCaptureSentence = MiningCaptureSentence,
            ReviewsEnabled = ReviewsEnabled,
            CacheSize = CacheSize,
            PreparseEnabled = PreparseEnabled,
            PreparseBatchSize = PreparseBatchSize,
            StatusOverlayEnabled = StatusOverlayEnabled,
            DebugLogging = DebugLogging,
            MouseZonePercent = MouseZonePercent,
            CustomThemeColors = SelectedTheme == "Custom" && CustomStateStyles.Count > 0
                ? CustomStateStyles.ToDictionary(s => s.State.ToString(), s => s.ToCustomStateStyle())
                : null,
            PopupKeybinds = BuildKeybindsDictionary(),
        };
    }

    private Dictionary<string, string>? BuildKeybindsDictionary()
    {
        var dict = new Dictionary<string, string>();
        void TryAdd(string action, string key)
        {
            if (!string.IsNullOrWhiteSpace(key)) dict[action] = key.Trim();
        }
        TryAdd("ReviewAgain", KeybindReviewAgain);
        TryAdd("ReviewHard", KeybindReviewHard);
        TryAdd("ReviewGood", KeybindReviewGood);
        TryAdd("ReviewEasy", KeybindReviewEasy);
        TryAdd("NeverForget", KeybindNeverForget);
        TryAdd("Blacklist", KeybindBlacklist);
        TryAdd("Suspend", KeybindSuspend);
        TryAdd("Forget", KeybindForget);
        return dict.Count > 0 ? dict : null;
    }

    [RelayCommand]
    private void ToggleApiKeyVisibility() => IsApiKeyVisible = !IsApiKeyVisible;

    [RelayCommand]
    private void SetSubtitleAlignment(string value)
    {
        if (!int.TryParse(value, out var alignment)) return;

        if (alignment != SubtitleAlignment)
        {
            SubtitleAlignment = alignment;
            return;
        }

        // Clicking the active button unchecks it locally; re-notifying re-pushes the binding
        // so the picker cannot end up with nothing selected.
        OnPropertyChanged(nameof(SubtitleAlignment));
    }

    private void ApplyBlurStates(IEnumerable<int> stateInts)
    {
        var s = new HashSet<int>(stateInts);
        BlurNew = s.Contains((int)KnownState.New);
        BlurYoung = s.Contains((int)KnownState.Young);
        BlurMature = s.Contains((int)KnownState.Mature);
        BlurBlacklisted = s.Contains((int)KnownState.Blacklisted);
        BlurDue = s.Contains((int)KnownState.Due);
        BlurMastered = s.Contains((int)KnownState.Mastered);
        BlurRedundant = s.Contains((int)KnownState.Redundant);
    }

    private void InitCustomStylesFromPreset(string presetName)
    {
        if (!ThemePresets.All.TryGetValue(presetName, out var preset))
            preset = ThemePresets.Default;

        CustomStateStyles.Clear();
        foreach (var state in Enum.GetValues<KnownState>())
        {
            var ws = preset.TryGetValue(state, out var style) ? style : ThemePresets.Unparsed;
            CustomStateStyles.Add(StateStyleViewModel.FromWordStyleState(state, ws));
        }
    }

    private void InitCustomStylesFromSettings(Dictionary<string, CustomStateStyle> custom)
    {
        CustomStateStyles.Clear();
        foreach (var state in Enum.GetValues<KnownState>())
        {
            if (custom.TryGetValue(state.ToString(), out var css))
                CustomStateStyles.Add(StateStyleViewModel.FromCustomStateStyle(state, css));
            else
            {
                var fallback = ThemePresets.Default.TryGetValue(state, out var ws) ? ws : ThemePresets.Unparsed;
                CustomStateStyles.Add(StateStyleViewModel.FromWordStyleState(state, fallback));
            }
        }
    }

    [RelayCommand]
    private void ImportThemeCode()
    {
        var imported = ThemeCodeImporter.TryImport(ImportCode.Trim(), out var themeName);
        if (imported is null)
        {
            ImportStatus = "Invalid theme code";
            return;
        }

        SelectedTheme = "Custom";
        InitCustomStylesFromSettings(imported);
        ImportCode = "";
        ImportStatus = themeName is not null
            ? $"Imported \"{themeName}\""
            : "Theme imported";
    }

    [RelayCommand]
    private void ResetSection()
    {
        var defaults = new PluginSettings();
        switch (SelectedTabIndex)
        {
            case 0:
                ApiBaseUrl = defaults.ApiBaseUrl;
                ApiTimeoutSeconds = defaults.ApiTimeoutSeconds;
                break;
            case 1:
                SelectedTheme = defaults.Theme;
                FontFamily = defaults.FontFamily;
                FontSize = defaults.FontSize;
                BorderSize = defaults.BorderSize;
                SubtitleAlignment = defaults.SubtitleAlignment;
                SubtitleMarginX = defaults.SubtitleMarginX;
                SubtitleMarginY = defaults.SubtitleMarginY;
                CustomStateStyles.Clear();
                break;
            case 2:
                IPlusOneEnabled = defaults.IPlusOneEnabled;
                IPlusOneMinTokens = defaults.IPlusOneMinTokens;
                IPlusOneMaxFrequencyRank = defaults.IPlusOneMaxFrequencyRank;
                FrequencyMarkingEnabled = defaults.FrequencyMarkingEnabled;
                FrequencyTopN = defaults.FrequencyTopN;
                FrequencyMarkAllStates = defaults.FrequencyMarkAllStates;
                BlurEnabled = defaults.BlurEnabled;
                BlurStrength = defaults.BlurStrength;
                BlurRevealOnHover = defaults.BlurRevealOnHover;
                BlurRevealDelayMs = defaults.BlurRevealDelayMs;
                ApplyBlurStates(defaults.BlurStates);
                AutopauseEnabled = defaults.AutopauseEnabled;
                AutopauseDelayMs = defaults.AutopauseDelayMs;
                MiningEnabled = defaults.MiningEnabled;
                MiningCaptureSentence = defaults.MiningCaptureSentence;
                ReviewsEnabled = defaults.ReviewsEnabled;
                break;
            case 3:
                PopupTrigger = defaults.PopupTrigger;
                PopupHoverDelayMs = defaults.PopupHoverDelayMs;
                PopupAutoHide = defaults.PopupAutoHide;
                PopupAutoHideDelayMs = defaults.PopupAutoHideDelayMs;
                PopupHideAfterAction = defaults.PopupHideAfterAction;
                PopupPosition = defaults.PopupPosition;
                PopupFontScale = defaults.PopupFontScale;
                PopupBgOpacity = OpacityToPercent(defaults.PopupBgOpacity);
                PopupBgColor = defaults.PopupBgColor;
                PopupMaxMeanings = defaults.PopupMaxMeanings;
                PopupShowPitch = defaults.PopupShowPitch;
                PopupShowFrequency = defaults.PopupShowFrequency;
                PopupShowConjugation = defaults.PopupShowConjugation;
                PopupShowStateActions = defaults.PopupShowStateActions;
                PopupShowNeverForget = defaults.PopupShowNeverForget;
                PopupShowBlacklist = defaults.PopupShowBlacklist;
                PopupShowSuspend = defaults.PopupShowSuspend;
                PopupShowForget = defaults.PopupShowForget;
                PopupShowReview = defaults.PopupShowReview;
                PopupUseTwoGrades = defaults.PopupUseTwoGrades;
                break;
            case 4:
                var kb = defaults.PopupKeybinds ?? new();
                KeybindReviewAgain = kb.GetValueOrDefault("ReviewAgain", "");
                KeybindReviewHard = kb.GetValueOrDefault("ReviewHard", "");
                KeybindReviewGood = kb.GetValueOrDefault("ReviewGood", "");
                KeybindReviewEasy = kb.GetValueOrDefault("ReviewEasy", "");
                KeybindNeverForget = kb.GetValueOrDefault("NeverForget", "");
                KeybindBlacklist = kb.GetValueOrDefault("Blacklist", "");
                KeybindSuspend = kb.GetValueOrDefault("Suspend", "");
                KeybindForget = kb.GetValueOrDefault("Forget", "");
                break;
            case 5:
                CacheSize = defaults.CacheSize;
                PreparseEnabled = defaults.PreparseEnabled;
                PreparseBatchSize = defaults.PreparseBatchSize;
                StatusOverlayEnabled = defaults.StatusOverlayEnabled;
                DebugLogging = defaults.DebugLogging;
                MouseZonePercent = defaults.MouseZonePercent;
                break;
        }
    }
}
