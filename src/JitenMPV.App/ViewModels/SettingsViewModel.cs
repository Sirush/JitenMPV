using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JitenMPV.App.Fonts;
using JitenMPV.Core.Api;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;
using JitenMPV.Core.Fonts;
using JitenMPV.Core.Install;
using JitenMPV.Core.Media;
using JitenMPV.Core.Pitch;
using JitenMPV.Core.Plus;
using JitenMPV.Core.Theming;
using JitenMPV.Core.Update;
using Microsoft.Extensions.Logging.Abstractions;
using AvaloniaFontFamily = Avalonia.Media.FontFamily;

namespace JitenMPV.App.ViewModels;

public sealed record StudyDeckOption(int DeckId, string Name);

public partial class PitchStyleViewModel(PitchClass pitchClass, string color) : ViewModelBase
{
    public string Name { get; } = pitchClass.ToString();
    [ObservableProperty] private string _color = color;
}

public partial class SettingsViewModel : ViewModelBase
{
    /// Sidebar position of the Media tab; ResetSection and the tab visibility bindings share it.
    private const int MediaTabIndex = 3;

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _apiBaseUrl = "";
    [ObservableProperty] private int _apiTimeoutSeconds;

    [ObservableProperty] private string _selectedTheme = "";
    [ObservableProperty] private string _fontFamily = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFontWarning))]
    private string? _fontWarning;

    public bool HasFontWarning => !string.IsNullOrEmpty(FontWarning);

    [ObservableProperty] private int _fontSize;
    [ObservableProperty] private double _borderSize;

    [ObservableProperty] private int _subtitleAlignment;
    [ObservableProperty] private int _subtitleMarginX;
    [ObservableProperty] private int _subtitleMarginY;
    [ObservableProperty] private bool _subtitleSingleLine;

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
    [ObservableProperty] private int _popupSwitchDelayMs;
    [ObservableProperty] private bool _popupAutoHide;
    [ObservableProperty] private int _popupAutoHideDelayMs;
    [ObservableProperty] private bool _popupHideAfterAction;
    [ObservableProperty] private PopupPositionMode _popupPosition;
    [ObservableProperty] private PopupAnchor _popupFixedAnchor;
    [ObservableProperty] private int _popupOffsetPx;
    [ObservableProperty] private int _popupMaxWidthPx;
    [ObservableProperty] private double _popupFontScale;
    [ObservableProperty] private int _popupBgOpacity;
    [ObservableProperty] private string _popupBgColor = "";
    [ObservableProperty] private int _popupMaxMeanings;
    [ObservableProperty] private bool _popupFurigana;
    [ObservableProperty] private bool _popupShowPitch;
    [ObservableProperty] private bool _popupPitchDiagram;
    [ObservableProperty] private bool _popupShowFrequency;
    [ObservableProperty] private bool _popupShowConjugation;
    [ObservableProperty] private bool _popupShowStateActions;
    [ObservableProperty] private bool _popupShowNeverForget;
    [ObservableProperty] private bool _popupShowBlacklist;
    [ObservableProperty] private bool _popupShowSuspend;
    [ObservableProperty] private bool _popupShowForget;
    [ObservableProperty] private bool _popupShowDeckMembership;
    [ObservableProperty] private bool _popupShowReview;
    [ObservableProperty] private bool _popupUseTwoGrades;
    /// Presented positively; PluginSettings stores the Reader's negative popup_disable_headword_link.
    [ObservableProperty] private bool _popupHeadwordLink;
    [ObservableProperty] private bool _popupMoveActionsBottom;
    [ObservableProperty] private bool _popupShowRotateActions;

    [ObservableProperty] private bool _rotateStatesEnabled;
    [ObservableProperty] private bool _rotateCycle;
    [ObservableProperty] private bool _rotateCycleNeverForget;
    [ObservableProperty] private bool _rotateCycleBlacklist;
    [ObservableProperty] private bool _rotateCycleSuspended;

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
    [ObservableProperty] private bool _debugShowHitboxes;
    [ObservableProperty] private int _mouseZonePercent;
    [ObservableProperty] private bool _settingsButtonEnabled;
    [ObservableProperty] private bool _subtitleNavButtonsEnabled;

    [ObservableProperty] private string _keybindReviewAgain = "";
    [ObservableProperty] private string _keybindReviewHard = "";
    [ObservableProperty] private string _keybindReviewGood = "";
    [ObservableProperty] private string _keybindReviewEasy = "";
    [ObservableProperty] private string _keybindNeverForget = "";
    [ObservableProperty] private string _keybindBlacklist = "";
    [ObservableProperty] private string _keybindSuspend = "";
    [ObservableProperty] private string _keybindForget = "";
    [ObservableProperty] private string _keybindRotateForward = "";
    [ObservableProperty] private string _keybindRotateBackward = "";

    [ObservableProperty] private string _keybindPrevSub = "";
    [ObservableProperty] private string _keybindNextSub = "";
    [ObservableProperty] private string _keybindLoopSub = "";

    [ObservableProperty] private bool _mediaCaptureEnabled;
    [ObservableProperty] private bool _mediaCaptureImage;
    [ObservableProperty] private bool _mediaCaptureImageAnimated;
    [ObservableProperty] private bool _mediaCaptureAudio;
    [ObservableProperty] private bool _mediaReviewPopup;
    [ObservableProperty] private MediaOverwritePrompt _mediaOverwritePrompt;
    [ObservableProperty] private MediaImageSource _mediaImageSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleBurnHint))]
    [NotifyPropertyChangedFor(nameof(SubtitleBurnWarning))]
    private MediaSubtitleBurn _mediaSubtitleBurn;

    [ObservableProperty] private int _mediaImageMaxEdge;
    [ObservableProperty] private int _mediaImageQuality;
    [ObservableProperty] private int _mediaAnimMaxFrames;
    [ObservableProperty] private int _mediaAnimTargetFps;
    [ObservableProperty] private int _mediaAnimMinFps;
    [ObservableProperty] private int _mediaAnimMaxEdge;
    [ObservableProperty] private int _mediaAnimQuality;
    [ObservableProperty] private double _mediaAnimMaxMb;
    [ObservableProperty] private int _mediaAudioBitrateKbps;
    [ObservableProperty] private bool _mediaAudioStereo;
    [ObservableProperty] private double _mediaAudioMaxMb;
    [ObservableProperty] private bool _mediaAudioAutoTrim;
    [ObservableProperty] private int _mediaAudioPadLeadMs;
    [ObservableProperty] private int _mediaAudioPadTailMs;
    [ObservableProperty] private double _mediaAudioWindowMarginSeconds;
    [ObservableProperty] private int _mediaSentenceContextLines;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleBurnWarning))]
    private string _ffmpegPath = "";

    [ObservableProperty] private string _jitenPlusTierLabel = "Unknown";
    [ObservableProperty] private string _jitenPlusQuotaLabel = "";
    [ObservableProperty] private bool _jitenPlusLocked = true;
    [ObservableProperty] private string _jitenPlusStatus = "";
    [ObservableProperty] private bool _isCheckingJitenPlus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleBurnWarning))]
    private string _ffmpegStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleBurnWarning))]
    [NotifyPropertyChangedFor(nameof(FfmpegStatusColor))]
    [NotifyPropertyChangedFor(nameof(ShowFfmpegSetup))]
    [NotifyPropertyChangedFor(nameof(ShowFfmpegManual))]
    [NotifyPropertyChangedFor(nameof(CanDownloadFfmpeg))]
    private bool _ffmpegAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FfmpegStatusColor))]
    [NotifyPropertyChangedFor(nameof(ShowFfmpegSetup))]
    [NotifyPropertyChangedFor(nameof(ShowFfmpegManual))]
    [NotifyPropertyChangedFor(nameof(CanDownloadFfmpeg))]
    private bool _ffmpegProbed;

    /// Attribution belongs on the copy JitenMPV placed there, not on a system ffmpeg it merely found.
    [ObservableProperty] private bool _showFfmpegAttribution;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadFfmpeg))]
    private bool _isInstallingFfmpeg;

    [ObservableProperty] private double _ffmpegInstallPercent;
    [ObservableProperty] private string _ffmpegInstallStage = "";
    [ObservableProperty] private bool _ffmpegPromptDismissed;

    [ObservableProperty] private bool _pluginAutostart = true;
    [ObservableProperty] private string _pluginStartKey = "F10";
    [ObservableProperty] private bool _updateCheckEnabled = true;

    public string ConfigFilePath => Path.Combine(AppPaths.ConfigDir, "config.json");

    public string VersionLabel => $"v{Installer.CurrentVersion}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallBannerVisible))]
    private bool _showInstallBanner;

    [ObservableProperty] private string _installBannerText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallBannerVisible))]
    private string _installStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallStatusColor))]
    private bool _installFailed;

    public string InstallStatusColor => InstallFailed ? "#fca5a5" : "#86efac";

    /// Outlives ShowInstallBanner on purpose: installing clears that flag, and a banner that simply
    /// disappears is the one moment the user most needs to be told what happened.
    public bool IsInstallBannerVisible => ShowInstallBanner || InstallStatus.Length > 0;

    /// Running from somewhere mpv cannot spawn, or with no script in mpv's scripts directory, means
    /// the plugin will never start. Offering the fix beats leaving the user to discover the silence.
    private void RefreshInstallState()
    {
        var config = MpvConfigLocator.Resolve();
        ShowInstallBanner = !Installer.IsInstalled();
        InstallBannerText = $"JitenMPV is not installed for mpv. Script goes to {config.ScriptsDir}";
    }

    [RelayCommand]
    private void InstallForMpv()
    {
        var result = Installer.Install(new InstallOptions());
        InstallStatus = result.Success
            ? "Installed. Restart mpv to load the plugin."
            : $"Install failed: {result.Error}";
        if (result.Warning is { } warning) InstallStatus += $"\n{warning}";
        InstallFailed = !result.Success;
        RefreshInstallState();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateBannerVisible))]
    [NotifyPropertyChangedFor(nameof(UpdateBannerText))]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    private UpdateInfo? _availableUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateBannerVisible))]
    private string _updateStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateStatusColor))]
    private bool _updateFailed;

    public string UpdateStatusColor => UpdateFailed ? "#fca5a5" : "#86efac";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    [NotifyPropertyChangedFor(nameof(CanCheckForUpdates))]
    private bool _isUpdating;

    [ObservableProperty] private double _updatePercent;
    [ObservableProperty] private string _updateStage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckForUpdates))]
    private bool _isCheckingUpdate;

    [ObservableProperty] private string _updateCheckStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateCheckStatusColor))]
    private bool _updateCheckFailed;

    public string UpdateCheckStatusColor => UpdateCheckFailed ? "#fca5a5" : "#a1a1aa";

    public bool CanCheckForUpdates => !IsCheckingUpdate && !IsUpdating;

    /// Survives a finished update for the same reason the install banner does: the outcome is
    /// what the user came to the banner for.
    public bool IsUpdateBannerVisible => AvailableUpdate is not null || UpdateStatus.Length > 0;

    public string UpdateBannerText => AvailableUpdate is { } update
        ? $"JitenMPV {update.Version} is available. You have {Installer.CurrentVersion}."
        : "";

    public bool CanInstallUpdate => AvailableUpdate is not null && !IsUpdating && SelfUpdater.IsSupported;

    private async Task CheckForUpdateAsync()
        => AvailableUpdate = await UpdateChecker.CheckAsync(UpdateCheckEnabled, CancellationToken.None);

    [RelayCommand]
    private async Task CheckForUpdatesNowAsync()
    {
        if (IsCheckingUpdate) return;

        IsCheckingUpdate = true;
        UpdateCheckFailed = false;
        UpdateCheckStatus = "Checking...";

        try
        {
            var result = await UpdateChecker.CheckNowAsync(CancellationToken.None);
            AvailableUpdate = result.Update;

            // An unreachable GitHub also yields no update, and calling that "up to date" would be
            // a guess presented as a fact.
            UpdateCheckFailed = !result.Reachable;
            UpdateCheckStatus = result switch
            {
                { Reachable: false } => "Could not reach GitHub. Try again later.",
                { Update: { } update } => $"Version {update.Version} is available.",
                _ => $"You are on the latest version ({Installer.CurrentVersion})."
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Network failures are already folded into Reachable; this is update-state.json itself.
            UpdateCheckFailed = true;
            UpdateCheckStatus = "Could not check for updates.";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (AvailableUpdate is not { } update || IsUpdating) return;

        IsUpdating = true;
        UpdatePercent = 0;
        UpdateStage = "Starting";

        var progress = new Progress<UpdateProgress>(p =>
        {
            UpdateStage = p.Stage;
            if (p.Fraction is { } fraction) UpdatePercent = fraction * 100;
        });

        try
        {
            var result = await SelfUpdater.UpdateAsync(update, progress, CancellationToken.None);
            UpdateStatus = result.Message;
            UpdateFailed = !result.Success;
            if (result.Success) AvailableUpdate = null;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AvailableUpdate?.ReleaseNotesUrl ?? UpdateChecker.ReleasesUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            // No browser registered; the version is already on screen to look up by hand.
        }
    }

    public string FfmpegStatusColor => !FfmpegProbed ? "#a1a1aa"
        : FfmpegAvailable ? "#86efac"
        : "#fca5a5";

    /// The setup block earns its place on the first screen only while ffmpeg is missing; once it
    /// works, the General tab is back to being about the API key.
    public bool ShowFfmpegSetup => FfmpegProbed && !FfmpegAvailable;

    public bool CanDownloadFfmpeg => ShowFfmpegSetup && !IsInstallingFfmpeg && FfmpegInstaller.IsSupported;

    public string FfmpegDownloadLabel => $"Download ffmpeg (about {FfmpegDownloadSizeMb} MB)";

    /// Measured against the BtbN win64 LGPL asset, 2026-07-27. Only ffmpeg itself is kept, so the
    /// installed footprint is far smaller than the download.
    private const int FfmpegDownloadSizeMb = 140;

    public string FfmpegManualCommand => FfmpegSetupHelp.ManualCommand;

    public string FfmpegManualHint => FfmpegSetupHelp.Hint;

    public bool ShowFfmpegManual => ShowFfmpegSetup && FfmpegManualCommand.Length > 0;

    public ObservableCollection<MediaOverwritePrompt> OverwritePrompts { get; } =
    [
        MediaOverwritePrompt.Always, MediaOverwritePrompt.OncePerSession, MediaOverwritePrompt.Never
    ];

    public string SubtitleBurnHint => MediaSubtitleBurn switch
    {
        MediaSubtitleBurn.Original =>
            "Displays with normal, uncoloured subtitles.",
        MediaSubtitleBurn.Colored =>
            "Shows subtitles in the same colours as your theme.",
        _ => "Only show the image without subtitles."
    };

    public string SubtitleBurnWarning
        => MediaSubtitleBurn == MediaSubtitleBurn.Original && !FfmpegAvailable && FfmpegStatus.Length > 0
            ? "ffmpeg was not found, so the subtitles will be left out"
            : "";

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isApiKeyVisible;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyStatusColor))]
    private string _apiKeyStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyStatusColor))]
    private bool _isApiKeyValid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyStatusColor))]
    private bool _isTestingApiKey;

    public string ApiKeyStatusColor => IsTestingApiKey ? "#a1a1aa"
        : IsApiKeyValid ? "#86efac"
        : "#fca5a5";

    [ObservableProperty] private string _importCode = "";
    [ObservableProperty] private string _importStatus = "";

    [ObservableProperty] private bool _miningToStudyDeck;
    [ObservableProperty] private bool _miningAutoOnReview;
    [ObservableProperty] private bool _miningSkipIfPresent;
    [ObservableProperty] private StudyDeckOption? _selectedStudyDeck;
    [ObservableProperty] private string _deckStatus = "";
    [ObservableProperty] private bool _isLoadingDecks;
    [ObservableProperty] private DoubleClickAction _doubleClickAction;
    [ObservableProperty] private string _keybindMine = "";

    public ObservableCollection<StudyDeckOption> AvailableDecks { get; } = [];

    public ObservableCollection<DoubleClickAction> DoubleClickActions { get; } =
    [
        DoubleClickAction.None, DoubleClickAction.Master, DoubleClickAction.Mine
    ];

    public ObservableCollection<PopupAnchor> PopupAnchors { get; } =
    [
        PopupAnchor.TopLeft, PopupAnchor.TopCenter, PopupAnchor.TopRight,
        PopupAnchor.BottomLeft, PopupAnchor.BottomCenter, PopupAnchor.BottomRight
    ];

    public bool IsFixedPopupPosition => PopupPosition == PopupPositionMode.Fixed;

    partial void OnPopupPositionChanged(PopupPositionMode value)
        => OnPropertyChanged(nameof(IsFixedPopupPosition));

    [ObservableProperty] private bool _pitchColoringEnabled;
    [ObservableProperty] private PitchIndicatorMode _pitchIndicator;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPitchUnderline))]
    private double _pitchUnderlineThickness;

    public bool IsPitchUnderline => PitchIndicator == PitchIndicatorMode.Underline;

    partial void OnPitchIndicatorChanged(PitchIndicatorMode value) => OnPropertyChanged(nameof(IsPitchUnderline));
    public ObservableCollection<PitchStyleViewModel> PitchStyles { get; } = [];

    private void InitPitchStyles(Dictionary<string, CustomStateStyle>? stored)
    {
        PitchStyles.Clear();
        foreach (var pitchClass in PitchAccent.Styleable)
        {
            var color = stored?.GetValueOrDefault(pitchClass.ToString())?.TextColor
                        ?? PitchAccent.DefaultColor(pitchClass);
            PitchStyles.Add(new PitchStyleViewModel(pitchClass, color));
        }
    }

    [RelayCommand]
    private void ResetPitchColors() => InitPitchStyles(null);

    public ObservableCollection<StateStyleViewModel> CustomStateStyles { get; } = [];
    private string _previousTheme = "Default";

    public ObservableCollection<string> AvailableThemes { get; } =
    [
        "Default", "High Contrast", "Monochrome", "Subtle", "Underline", "Toy Box", "Custom"
    ];

    /// Installed families only. Offering fonts that are absent is what made the family setting look
    /// inert: mpv silently substitutes for a name it cannot resolve, so every choice looked identical.
    public ObservableCollection<string> AvailableFonts { get; } = [];

    private bool _fontCatalogLoaded;

    private async Task LoadInstalledFontsAsync()
    {
        var families = await Task.Run(() => SystemFontCatalog.JapaneseFamilies);

        AvailableFonts.Clear();
        foreach (var family in families)
            AvailableFonts.Add(family);

        _fontCatalogLoaded = true;
        UpdateFontStatus();
    }

    partial void OnFontFamilyChanged(string value) => UpdateFontStatus();

    /// Silent until the catalog is in, so a slow enumeration cannot flash a warning it has no
    /// grounds for.
    private void UpdateFontStatus()
    {
        OnPropertyChanged(nameof(PreviewFont));
        if (!_fontCatalogLoaded) return;

        if (SystemFontCatalog.CanRenderJapanese(FontFamily))
        {
            FontWarning = null;
            return;
        }

        // An existing config keeps whatever font it was created with, so the better default cannot
        // reach anyone who already ran the plugin; naming the font here is what does.
        var suggestion = DefaultSubtitleFont.Value;
        FontWarning = "This font is not installed, or cannot render Japanese. mpv will substitute "
                      + "one, often a font that draws Chinese kanji forms."
                      + (SystemFontCatalog.CanRenderJapanese(suggestion) ? $" Try {suggestion}." : "");
    }

    public AvaloniaFontFamily PreviewFont
        => string.IsNullOrWhiteSpace(FontFamily)
            ? AvaloniaFontFamily.Default
            : new AvaloniaFontFamily(FontFamily);

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
        SubtitleSingleLine = s.SubtitleSingleLine;
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
        PopupSwitchDelayMs = s.PopupSwitchDelayMs;
        PopupAutoHide = s.PopupAutoHide;
        PopupAutoHideDelayMs = s.PopupAutoHideDelayMs;
        PopupHideAfterAction = s.PopupHideAfterAction;
        PopupPosition = s.PopupPosition;
        PopupFixedAnchor = s.PopupFixedAnchor;
        PopupOffsetPx = s.PopupOffsetPx;
        PopupMaxWidthPx = s.PopupMaxWidthPx;
        PopupFontScale = s.PopupFontScale;
        PopupBgOpacity = OpacityToPercent(s.PopupBgOpacity);
        PopupBgColor = s.PopupBgColor;
        PopupMaxMeanings = s.PopupMaxMeanings;
        PopupFurigana = s.PopupFurigana;
        PopupShowPitch = s.PopupShowPitch;
        PopupPitchDiagram = s.PopupPitchDiagram;
        PitchColoringEnabled = s.PitchColoringEnabled;
        PitchIndicator = s.PitchIndicator;
        PitchUnderlineThickness = s.PitchUnderlineThickness;
        InitPitchStyles(s.PitchStyles);
        PopupShowFrequency = s.PopupShowFrequency;
        PopupShowConjugation = s.PopupShowConjugation;
        PopupShowStateActions = s.PopupShowStateActions;
        PopupShowNeverForget = s.PopupShowNeverForget;
        PopupShowBlacklist = s.PopupShowBlacklist;
        PopupShowSuspend = s.PopupShowSuspend;
        PopupShowForget = s.PopupShowForget;
        PopupShowDeckMembership = s.PopupShowDeckMembership;
        PopupShowReview = s.PopupShowReview;
        PopupUseTwoGrades = s.PopupUseTwoGrades;
        PopupHeadwordLink = !s.PopupDisableHeadwordLink;
        PopupMoveActionsBottom = s.PopupMoveActionsBottom;
        PopupShowRotateActions = s.PopupShowRotateActions;
        RotateStatesEnabled = s.RotateStatesEnabled;
        RotateCycle = s.RotateCycle;
        RotateCycleNeverForget = s.RotateCycleNeverForget;
        RotateCycleBlacklist = s.RotateCycleBlacklist;
        RotateCycleSuspended = s.RotateCycleSuspended;
        AutopauseEnabled = s.AutopauseEnabled;
        AutopauseDelayMs = s.AutopauseDelayMs;
        MiningEnabled = s.MiningEnabled;
        MiningCaptureSentence = s.MiningCaptureSentence;
        MiningToStudyDeck = s.MiningToStudyDeck;
        MiningAutoOnReview = s.MiningAutoOnReview;
        MiningSkipIfPresent = s.MiningSkipIfPresent;
        DoubleClickAction = s.DoubleClickAction;
        if (s.MiningStudyDeckId is { } deckId)
        {
            var placeholder = new StudyDeckOption(deckId, $"Deck #{deckId}");
            AvailableDecks.Add(placeholder);
            SelectedStudyDeck = placeholder;
        }
        ReviewsEnabled = s.ReviewsEnabled;
        CacheSize = s.CacheSize;
        PluginAutostart = s.PluginAutostart;
        PluginStartKey = s.PluginStartKey;
        UpdateCheckEnabled = s.UpdateCheckEnabled;
        PreparseEnabled = s.PreparseEnabled;
        PreparseBatchSize = s.PreparseBatchSize;
        StatusOverlayEnabled = s.StatusOverlayEnabled;
        DebugLogging = s.DebugLogging;
        DebugShowHitboxes = s.DebugShowHitboxes;
        MouseZonePercent = s.MouseZonePercent;
        SettingsButtonEnabled = s.SettingsButtonEnabled;
        SubtitleNavButtonsEnabled = s.SubtitleNavButtonsEnabled;
        KeybindPrevSub = s.KeybindPrevSub;
        KeybindNextSub = s.KeybindNextSub;
        KeybindLoopSub = s.KeybindLoopSub;

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
            KeybindMine = kb.GetValueOrDefault("Mine", "");
            KeybindRotateForward = kb.GetValueOrDefault("RotateForward", "");
            KeybindRotateBackward = kb.GetValueOrDefault("RotateBackward", "");
        }

        ApplyMediaSettings(s);
        ApplyJitenPlusSnapshot(JitenPlusCache.Load());

        _previousTheme = s.Theme == "Custom" ? "Default" : s.Theme;
        if (s.Theme == "Custom" && s.CustomThemeColors is { Count: > 0 } custom)
            InitCustomStylesFromSettings(custom);

        // The General tab is the landing tab and leads with ffmpeg's state, so the probe cannot
        // wait for the user to visit a tab or press a button.
        _ = DetectFfmpegAsync();
        _ = CheckForUpdateAsync();
        _ = LoadInstalledFontsAsync();
        RefreshInstallState();
    }

    private void ApplyMediaSettings(PluginSettings s)
    {
        MediaCaptureEnabled = s.MediaCaptureEnabled;
        MediaCaptureImage = s.MediaCaptureImage;
        MediaCaptureImageAnimated = s.MediaCaptureImageAnimated;
        MediaCaptureAudio = s.MediaCaptureAudio;
        MediaReviewPopup = s.MediaReviewPopup;
        MediaOverwritePrompt = s.MediaOverwritePrompt;
        MediaImageSource = s.MediaImageSource;
        MediaSubtitleBurn = s.MediaSubtitleBurn;
        MediaImageMaxEdge = s.MediaImageMaxEdge;
        MediaImageQuality = s.MediaImageQuality;
        MediaAnimMaxFrames = s.MediaAnimMaxFrames;
        MediaAnimTargetFps = s.MediaAnimTargetFps;
        MediaAnimMinFps = s.MediaAnimMinFps;
        MediaAnimMaxEdge = s.MediaAnimMaxEdge;
        MediaAnimQuality = s.MediaAnimQuality;
        MediaAnimMaxMb = BytesToMb(s.MediaAnimMaxBytes);
        MediaAudioBitrateKbps = s.MediaAudioBitrateKbps;
        MediaAudioStereo = s.MediaAudioStereo;
        MediaAudioMaxMb = BytesToMb(s.MediaAudioMaxBytes);
        MediaAudioAutoTrim = s.MediaAudioAutoTrim;
        MediaAudioPadLeadMs = s.MediaAudioPadLeadMs;
        MediaAudioPadTailMs = s.MediaAudioPadTailMs;
        MediaAudioWindowMarginSeconds = s.MediaAudioWindowMarginSeconds;
        MediaSentenceContextLines = s.MediaSentenceContextLines;
        FfmpegPath = s.FfmpegPath;
        FfmpegPromptDismissed = s.FfmpegPromptDismissed;
    }

    private void ApplyJitenPlusSnapshot(JitenPlusSnapshot snapshot)
    {
        JitenPlusLocked = !snapshot.IsActive;
        JitenPlusTierLabel = snapshot.Tier switch
        {
            JitenPlusTier.Full => "Jiten+",
            JitenPlusTier.Trial => "Jiten+ Trial",
            _ => "No Jiten+"
        };
        JitenPlusQuotaLabel = snapshot.MaxBytes > 0
            ? $"{FormatBytes(snapshot.UsedBytes)} / {FormatBytes(snapshot.MaxBytes)} used"
            : "";
    }

    private static double BytesToMb(int bytes) => Math.Round(bytes / 1_000_000.0, 2);
    private static int MbToBytes(double mb) => (int)Math.Round(Math.Max(0.1, mb) * 1_000_000);

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"
        : $"{bytes / (1024.0 * 1024):0.#} MB";

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
            SubtitleSingleLine = SubtitleSingleLine,
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
            PopupSwitchDelayMs = PopupSwitchDelayMs,
            PopupAutoHide = PopupAutoHide,
            PopupAutoHideDelayMs = PopupAutoHideDelayMs,
            PopupHideAfterAction = PopupHideAfterAction,
            PopupPosition = PopupPosition,
            PopupFixedAnchor = PopupFixedAnchor,
            PopupOffsetPx = PopupOffsetPx,
            PopupMaxWidthPx = PopupMaxWidthPx,
            PopupFontScale = PopupFontScale,
            PopupBgOpacity = PercentToOpacity(PopupBgOpacity),
            PopupBgColor = PopupBgColor,
            PopupMaxMeanings = PopupMaxMeanings,
            PopupFurigana = PopupFurigana,
            PopupShowPitch = PopupShowPitch,
            PopupPitchDiagram = PopupPitchDiagram,
            PitchColoringEnabled = PitchColoringEnabled,
            PitchIndicator = PitchIndicator,
            PitchUnderlineThickness = PitchUnderlineThickness,
            PitchStyles = PitchStyles.ToDictionary(p => p.Name, p => new CustomStateStyle { TextColor = p.Color }),
            PopupShowFrequency = PopupShowFrequency,
            PopupShowConjugation = PopupShowConjugation,
            PopupShowStateActions = PopupShowStateActions,
            PopupShowNeverForget = PopupShowNeverForget,
            PopupShowBlacklist = PopupShowBlacklist,
            PopupShowSuspend = PopupShowSuspend,
            PopupShowForget = PopupShowForget,
            PopupShowDeckMembership = PopupShowDeckMembership,
            PopupShowReview = PopupShowReview,
            PopupUseTwoGrades = PopupUseTwoGrades,
            PopupDisableHeadwordLink = !PopupHeadwordLink,
            PopupMoveActionsBottom = PopupMoveActionsBottom,
            PopupShowRotateActions = PopupShowRotateActions,
            RotateStatesEnabled = RotateStatesEnabled,
            RotateCycle = RotateCycle,
            RotateCycleNeverForget = RotateCycleNeverForget,
            RotateCycleBlacklist = RotateCycleBlacklist,
            RotateCycleSuspended = RotateCycleSuspended,
            AutopauseEnabled = AutopauseEnabled,
            AutopauseDelayMs = AutopauseDelayMs,
            MiningEnabled = MiningEnabled,
            MiningCaptureSentence = MiningCaptureSentence,
            MiningToStudyDeck = MiningToStudyDeck,
            MiningAutoOnReview = MiningAutoOnReview,
            MiningSkipIfPresent = MiningSkipIfPresent,
            MiningStudyDeckId = SelectedStudyDeck?.DeckId,
            DoubleClickAction = DoubleClickAction,
            ReviewsEnabled = ReviewsEnabled,
            CacheSize = CacheSize,
            PluginAutostart = PluginAutostart,
            PluginStartKey = string.IsNullOrWhiteSpace(PluginStartKey) ? "F10" : PluginStartKey.Trim(),
            UpdateCheckEnabled = UpdateCheckEnabled,
            PreparseEnabled = PreparseEnabled,
            PreparseBatchSize = PreparseBatchSize,
            StatusOverlayEnabled = StatusOverlayEnabled,
            DebugLogging = DebugLogging,
            DebugShowHitboxes = DebugShowHitboxes,
            MouseZonePercent = MouseZonePercent,
            SettingsButtonEnabled = SettingsButtonEnabled,
            SubtitleNavButtonsEnabled = SubtitleNavButtonsEnabled,
            KeybindPrevSub = KeybindPrevSub.Trim(),
            KeybindNextSub = KeybindNextSub.Trim(),
            KeybindLoopSub = KeybindLoopSub.Trim(),
            MediaCaptureEnabled = MediaCaptureEnabled,
            MediaCaptureImage = MediaCaptureImage,
            MediaCaptureImageAnimated = MediaCaptureImageAnimated,
            MediaCaptureAudio = MediaCaptureAudio,
            MediaReviewPopup = MediaReviewPopup,
            MediaOverwritePrompt = MediaOverwritePrompt,
            MediaImageSource = MediaImageSource,
            MediaSubtitleBurn = MediaSubtitleBurn,
            MediaImageMaxEdge = MediaImageMaxEdge,
            MediaImageQuality = MediaImageQuality,
            MediaAnimMaxFrames = MediaAnimMaxFrames,
            MediaAnimTargetFps = MediaAnimTargetFps,
            MediaAnimMinFps = MediaAnimMinFps,
            MediaAnimMaxEdge = MediaAnimMaxEdge,
            MediaAnimQuality = MediaAnimQuality,
            MediaAnimMaxBytes = MbToBytes(MediaAnimMaxMb),
            MediaAudioBitrateKbps = MediaAudioBitrateKbps,
            MediaAudioStereo = MediaAudioStereo,
            MediaAudioMaxBytes = MbToBytes(MediaAudioMaxMb),
            MediaAudioAutoTrim = MediaAudioAutoTrim,
            MediaAudioPadLeadMs = MediaAudioPadLeadMs,
            MediaAudioPadTailMs = MediaAudioPadTailMs,
            MediaAudioWindowMarginSeconds = MediaAudioWindowMarginSeconds,
            MediaSentenceContextLines = MediaSentenceContextLines,
            FfmpegPath = FfmpegPath,
            FfmpegPromptDismissed = FfmpegPromptDismissed,
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
        TryAdd("Mine", KeybindMine);
        TryAdd("RotateForward", KeybindRotateForward);
        TryAdd("RotateBackward", KeybindRotateBackward);
        return dict.Count > 0 ? dict : null;
    }

    [RelayCommand]
    private void ToggleApiKeyVisibility() => IsApiKeyVisible = !IsApiKeyVisible;

    /// Tests the key currently in the box, which need not be the saved one, so a paste can be
    /// checked before committing it.
    [RelayCommand]
    private async Task TestApiKeyAsync()
    {
        if (IsTestingApiKey) return;

        var key = ApiKey.Trim();
        if (string.IsNullOrEmpty(key))
        {
            IsApiKeyValid = false;
            ApiKeyStatus = "Enter an API key first";
            return;
        }

        IsTestingApiKey = true;
        IsApiKeyValid = false;
        ApiKeyStatus = "Testing...";
        try
        {
            var client = new JitenApiClient(key, ApiBaseUrl, ApiTimeoutSeconds,
                NullLogger<SettingsViewModel>.Instance);
            IsApiKeyValid = await client.PingAsync(
                key, ApiBaseUrl, ApiTimeoutSeconds, CancellationToken.None);

            // PingAsync swallows transport failures into false, so an unreachable server and a bad
            // key look the same here; the message covers both.
            ApiKeyStatus = IsApiKeyValid
                ? "This key works"
                : "This key was refused, or jiten.moe could not be reached";
        }
        catch (Exception ex)
        {
            ApiKeyStatus = $"Could not check: {ex.Message}";
        }
        finally
        {
            IsTestingApiKey = false;
        }
    }

    /// A key edited after a test invalidates the verdict shown next to it.
    partial void OnApiKeyChanged(string value)
    {
        ApiKeyStatus = "";
        IsApiKeyValid = false;
    }

    [RelayCommand]
    private async Task LoadDecksAsync()
    {
        if (IsLoadingDecks) return;
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            DeckStatus = "Set an API key first";
            return;
        }

        IsLoadingDecks = true;
        DeckStatus = "Loading...";
        try
        {
            var client = new JitenApiClient(ApiKey, ApiBaseUrl, ApiTimeoutSeconds,
                NullLogger<SettingsViewModel>.Instance);
            var decks = await client.GetStudyDecksAsync(CancellationToken.None);

            var wordLists = decks.Where(d => d.DeckType == StudyDeckType.StaticWordList).ToList();

            // The selection is restored by id: the pre-load placeholder is a different instance.
            var previousId = SelectedStudyDeck?.DeckId;
            AvailableDecks.Clear();
            foreach (var deck in wordLists)
                AvailableDecks.Add(new StudyDeckOption(deck.UserStudyDeckId, deck.Name));

            SelectedStudyDeck = AvailableDecks.FirstOrDefault(d => d.DeckId == previousId);
            DeckStatus = wordLists.Count == 0 ? "No word lists found" : $"{wordLists.Count} lists";
        }
        catch (JitenApiKeyRejectedException)
        {
            DeckStatus = "API key rejected";
        }
        catch (Exception ex)
        {
            DeckStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsLoadingDecks = false;
        }
    }

    /// The settings window in standalone GUI mode has no PluginHost, so it builds its own client
    /// and service exactly as LoadDecksAsync does.
    [RelayCommand]
    private async Task RefreshJitenPlusAsync()
    {
        if (IsCheckingJitenPlus) return;
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            JitenPlusStatus = "Set an API key first";
            return;
        }

        IsCheckingJitenPlus = true;
        JitenPlusStatus = "Checking...";
        try
        {
            var client = new JitenApiClient(ApiKey, ApiBaseUrl, ApiTimeoutSeconds,
                NullLogger<SettingsViewModel>.Instance);
            using var service = new JitenPlusService(client, NullLogger<SettingsViewModel>.Instance,
                loadCache: false);

            var snapshot = await service.RefreshAsync(CancellationToken.None);
            ApplyJitenPlusSnapshot(snapshot);
            JitenPlusStatus = snapshot.Error is { } error
                ? $"Could not check: {error}"
                : snapshot.IsActive ? "Checked just now" : "You do not have Jiten+ at the moment";
        }
        catch (JitenApiKeyRejectedException)
        {
            JitenPlusStatus = "Your API key was refused";
        }
        catch (Exception ex)
        {
            JitenPlusStatus = $"Could not check: {ex.Message}";
        }
        finally
        {
            IsCheckingJitenPlus = false;
        }
    }

    /// Reveals config.json rather than just opening the folder, since the point of the button is to
    /// get at that one file. Falls back to the folder before the first save creates it.
    [RelayCommand]
    private void OpenConfigFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ConfigDir);
            var reveal = File.Exists(ConfigFilePath);

            if (OperatingSystem.IsWindows())
            {
                // explorer parses its own command line: the path must be quoted inside the
                // /select, token, not as a whole quoted argument, or a path containing a space
                // silently opens Documents instead of selecting the file.
                Process.Start(new ProcessStartInfo("explorer.exe",
                    reveal ? $"/select,\"{ConfigFilePath}\"" : $"\"{AppPaths.ConfigDir}\"")
                {
                    UseShellExecute = false
                });
                return;
            }

            var psi = new ProcessStartInfo { UseShellExecute = false };

            if (OperatingSystem.IsMacOS())
            {
                psi.FileName = "open";
                if (reveal) psi.ArgumentList.Add("-R");
                psi.ArgumentList.Add(reveal ? ConfigFilePath : AppPaths.ConfigDir);
            }
            else
            {
                psi.FileName = "xdg-open";
                psi.ArgumentList.Add(AppPaths.ConfigDir);
            }

            Process.Start(psi);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception)
        {
            // No file manager, or none registered for directories; nothing actionable to offer.
        }
    }

    [RelayCommand]
    private async Task DetectFfmpegAsync()
    {
        FfmpegLocator.Invalidate();
        var resolved = await FfmpegLocator.ResolveAsync(FfmpegPath, CancellationToken.None);
        FfmpegAvailable = resolved is not null;
        FfmpegProbed = true;
        ShowFfmpegAttribution = resolved?.Source == FfmpegSource.Managed;
        FfmpegStatus = resolved is null
            ? "ffmpeg is missing, so audio and clips cannot be mined."
            : $"ffmpeg {resolved.DisplayVersion} is ready ({resolved.SourceLabel}).";
    }

    [RelayCommand]
    private async Task InstallFfmpegAsync()
    {
        if (IsInstallingFfmpeg) return;

        IsInstallingFfmpeg = true;
        FfmpegInstallPercent = 0;
        FfmpegInstallStage = "Starting";

        var progress = new Progress<FfmpegInstallProgress>(p =>
        {
            FfmpegInstallStage = p.Stage;
            if (p.Fraction is { } fraction)
                FfmpegInstallPercent = fraction * 100;
        });

        try
        {
            var result = await FfmpegInstaller.InstallAsync(progress, CancellationToken.None);

            if (result.Success)
            {
                await DetectFfmpegAsync();
            }
            else
            {
                FfmpegAvailable = false;
                FfmpegProbed = true;
                FfmpegStatus = result.Message;
            }
        }
        finally
        {
            IsInstallingFfmpeg = false;
        }
    }

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
                SubtitleSingleLine = defaults.SubtitleSingleLine;
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
                MiningToStudyDeck = defaults.MiningToStudyDeck;
                MiningAutoOnReview = defaults.MiningAutoOnReview;
                MiningSkipIfPresent = defaults.MiningSkipIfPresent;
                DoubleClickAction = defaults.DoubleClickAction;
                SelectedStudyDeck = null;
                ReviewsEnabled = defaults.ReviewsEnabled;
                break;
            case MediaTabIndex:
                ApplyMediaSettings(defaults);
                break;
            case 4:
                PopupTrigger = defaults.PopupTrigger;
                PopupHoverDelayMs = defaults.PopupHoverDelayMs;
                PopupSwitchDelayMs = defaults.PopupSwitchDelayMs;
                PopupAutoHide = defaults.PopupAutoHide;
                PopupAutoHideDelayMs = defaults.PopupAutoHideDelayMs;
                PopupHideAfterAction = defaults.PopupHideAfterAction;
                PopupPosition = defaults.PopupPosition;
                PopupFixedAnchor = defaults.PopupFixedAnchor;
                PopupOffsetPx = defaults.PopupOffsetPx;
                PopupMaxWidthPx = defaults.PopupMaxWidthPx;
                PopupFontScale = defaults.PopupFontScale;
                PopupBgOpacity = OpacityToPercent(defaults.PopupBgOpacity);
                PopupBgColor = defaults.PopupBgColor;
                PopupMaxMeanings = defaults.PopupMaxMeanings;
                PopupFurigana = defaults.PopupFurigana;
                PopupShowPitch = defaults.PopupShowPitch;
                PopupPitchDiagram = defaults.PopupPitchDiagram;
                PopupShowFrequency = defaults.PopupShowFrequency;
                PopupShowConjugation = defaults.PopupShowConjugation;
                PopupShowStateActions = defaults.PopupShowStateActions;
                PopupShowNeverForget = defaults.PopupShowNeverForget;
                PopupShowBlacklist = defaults.PopupShowBlacklist;
                PopupShowSuspend = defaults.PopupShowSuspend;
                PopupShowForget = defaults.PopupShowForget;
                PopupShowDeckMembership = defaults.PopupShowDeckMembership;
                PopupShowReview = defaults.PopupShowReview;
                PopupUseTwoGrades = defaults.PopupUseTwoGrades;
                PopupHeadwordLink = !defaults.PopupDisableHeadwordLink;
                PopupMoveActionsBottom = defaults.PopupMoveActionsBottom;
                PopupShowRotateActions = defaults.PopupShowRotateActions;
                RotateStatesEnabled = defaults.RotateStatesEnabled;
                RotateCycle = defaults.RotateCycle;
                RotateCycleNeverForget = defaults.RotateCycleNeverForget;
                RotateCycleBlacklist = defaults.RotateCycleBlacklist;
                RotateCycleSuspended = defaults.RotateCycleSuspended;
                break;
            case 5:
                var kb = defaults.PopupKeybinds ?? new();
                KeybindReviewAgain = kb.GetValueOrDefault("ReviewAgain", "");
                KeybindReviewHard = kb.GetValueOrDefault("ReviewHard", "");
                KeybindReviewGood = kb.GetValueOrDefault("ReviewGood", "");
                KeybindReviewEasy = kb.GetValueOrDefault("ReviewEasy", "");
                KeybindMine = kb.GetValueOrDefault("Mine", "");
                KeybindNeverForget = kb.GetValueOrDefault("NeverForget", "");
                KeybindBlacklist = kb.GetValueOrDefault("Blacklist", "");
                KeybindSuspend = kb.GetValueOrDefault("Suspend", "");
                KeybindForget = kb.GetValueOrDefault("Forget", "");
                KeybindRotateForward = kb.GetValueOrDefault("RotateForward", "");
                KeybindRotateBackward = kb.GetValueOrDefault("RotateBackward", "");
                KeybindPrevSub = defaults.KeybindPrevSub;
                KeybindNextSub = defaults.KeybindNextSub;
                KeybindLoopSub = defaults.KeybindLoopSub;
                break;
            case 6:
                CacheSize = defaults.CacheSize;
                PluginAutostart = defaults.PluginAutostart;
                PluginStartKey = defaults.PluginStartKey;
                UpdateCheckEnabled = defaults.UpdateCheckEnabled;
                PreparseEnabled = defaults.PreparseEnabled;
                PreparseBatchSize = defaults.PreparseBatchSize;
                StatusOverlayEnabled = defaults.StatusOverlayEnabled;
                DebugLogging = defaults.DebugLogging;
                DebugShowHitboxes = defaults.DebugShowHitboxes;
                MouseZonePercent = defaults.MouseZonePercent;
                SettingsButtonEnabled = defaults.SettingsButtonEnabled;
                SubtitleNavButtonsEnabled = defaults.SubtitleNavButtonsEnabled;
                break;
        }
    }
}
