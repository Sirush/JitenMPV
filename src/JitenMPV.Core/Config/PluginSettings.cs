using System.Text.Json.Serialization;

namespace JitenMPV.Core.Config;

public sealed class PluginSettings
{
    [JsonPropertyName("api_base_url")]
    public string ApiBaseUrl { get; set; } = "https://api.jiten.moe";

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("api_timeout_seconds")]
    public int ApiTimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("font_family")]
    public string FontFamily { get; set; } = "Yu Gothic";

    [JsonPropertyName("font_size")]
    public int FontSize { get; set; } = 48;

    [JsonPropertyName("border_size")]
    public double BorderSize { get; set; } = 3.0;

    [JsonPropertyName("subtitle_alignment")]
    public int SubtitleAlignment { get; set; } = 2;

    [JsonPropertyName("subtitle_margin_x")]
    public int SubtitleMarginX { get; set; } = 0;

    [JsonPropertyName("subtitle_margin_y")]
    public int SubtitleMarginY { get; set; } = 50;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "Default";

    [JsonPropertyName("i_plus_one_enabled")]
    public bool IPlusOneEnabled { get; set; } = true;

    [JsonPropertyName("i_plus_one_min_tokens")]
    public int IPlusOneMinTokens { get; set; } = 3;

    [JsonPropertyName("i_plus_one_max_frequency_rank")]
    public int IPlusOneMaxFrequencyRank { get; set; } = 15000;

    [JsonPropertyName("frequency_marking_enabled")]
    public bool FrequencyMarkingEnabled { get; set; }

    [JsonPropertyName("frequency_top_n")]
    public int FrequencyTopN { get; set; } = 10000;

    [JsonPropertyName("frequency_mark_all_states")]
    public bool FrequencyMarkAllStates { get; set; }

    [JsonPropertyName("blur_enabled")]
    public bool BlurEnabled { get; set; }

    [JsonPropertyName("blur_strength")]
    public double BlurStrength { get; set; } = 6;

    [JsonPropertyName("blur_reveal_on_hover")]
    public bool BlurRevealOnHover { get; set; } = true;

    [JsonPropertyName("blur_states")]
    public List<int> BlurStates { get; set; } = [2, 3, 5, 6];

    [JsonPropertyName("blur_reveal_delay_ms")]
    public int BlurRevealDelayMs { get; set; } = 200;

    [JsonPropertyName("popup_trigger")]
    public PopupTriggerMode PopupTrigger { get; set; } = PopupTriggerMode.Hover;

    [JsonPropertyName("popup_hover_delay_ms")]
    public int PopupHoverDelayMs { get; set; } = 30;

    [JsonPropertyName("popup_auto_hide")]
    public bool PopupAutoHide { get; set; } = true;

    [JsonPropertyName("popup_auto_hide_delay_ms")]
    public int PopupAutoHideDelayMs { get; set; } = 500;

    [JsonPropertyName("popup_hide_after_action")]
    public bool PopupHideAfterAction { get; set; }

    [JsonPropertyName("popup_show_pitch")]
    public bool PopupShowPitch { get; set; } = true;

    [JsonPropertyName("popup_show_frequency")]
    public bool PopupShowFrequency { get; set; } = true;

    [JsonPropertyName("popup_show_conjugation")]
    public bool PopupShowConjugation { get; set; } = true;

    [JsonPropertyName("popup_show_state_actions")]
    public bool PopupShowStateActions { get; set; } = true;

    [JsonPropertyName("popup_show_never_forget")]
    public bool PopupShowNeverForget { get; set; } = true;

    [JsonPropertyName("popup_show_blacklist")]
    public bool PopupShowBlacklist { get; set; } = true;

    [JsonPropertyName("popup_show_suspend")]
    public bool PopupShowSuspend { get; set; }

    [JsonPropertyName("popup_show_forget")]
    public bool PopupShowForget { get; set; }

    [JsonPropertyName("popup_show_deck_membership")]
    public bool PopupShowDeckMembership { get; set; } = true;

    [JsonPropertyName("popup_show_review")]
    public bool PopupShowReview { get; set; } = true;

    [JsonPropertyName("popup_use_two_grades")]
    public bool PopupUseTwoGrades { get; set; }

    [JsonPropertyName("popup_position")]
    public PopupPositionMode PopupPosition { get; set; } = PopupPositionMode.AboveSubtitle;

    [JsonPropertyName("popup_font_scale")]
    public double PopupFontScale { get; set; } = 0.85;

    [JsonPropertyName("popup_bg_opacity")]
    public int PopupBgOpacity { get; set; } = 200;

    [JsonPropertyName("autopause_enabled")]
    public bool AutopauseEnabled { get; set; } = true;

    [JsonPropertyName("autopause_delay_ms")]
    public int AutopauseDelayMs { get; set; }

    [JsonPropertyName("mining_enabled")]
    public bool MiningEnabled { get; set; } = true;

    /// Attaches the current subtitle line (and the media title as source) to a mined word.
    [JsonPropertyName("mining_capture_sentence")]
    public bool MiningCaptureSentence { get; set; } = true;

    [JsonPropertyName("mining_study_deck_id")]
    public int? MiningStudyDeckId { get; set; }

    /// When set, mining goes straight to MiningStudyDeckId; otherwise the popup offers a picker.
    [JsonPropertyName("mining_to_study_deck")]
    public bool MiningToStudyDeck { get; set; }

    [JsonPropertyName("mining_auto_on_review")]
    public bool MiningAutoOnReview { get; set; }

    /// Skips the request when the word is already in the target deck, so re-mining cannot bump
    /// its occurrence count or overwrite the sentence already attached to it.
    [JsonPropertyName("mining_skip_if_present")]
    public bool MiningSkipIfPresent { get; set; } = true;

    [JsonPropertyName("double_click_action")]
    public DoubleClickAction DoubleClickAction { get; set; } = DoubleClickAction.Mine;

    /// Master switch for SRS grading: gates the popup grade buttons, the review keybinds and the
    /// action dispatch. Mirrors the Reader extension's jitenDisableReviews.
    [JsonPropertyName("reviews_enabled")]
    public bool ReviewsEnabled { get; set; } = true;

    [JsonPropertyName("cache_size")]
    public int CacheSize { get; set; } = 2000;

    [JsonPropertyName("popup_max_meanings")]
    public int PopupMaxMeanings { get; set; } = 10;

    [JsonPropertyName("popup_bg_color")]
    public string PopupBgColor { get; set; } = "#1A1A1A";

    [JsonPropertyName("preparse_enabled")]
    public bool PreparseEnabled { get; set; } = true;

    [JsonPropertyName("preparse_batch_size")]
    public int PreparseBatchSize { get; set; } = 60000;

    [JsonPropertyName("status_overlay_enabled")]
    public bool StatusOverlayEnabled { get; set; } = true;

    [JsonPropertyName("debug_logging")]
    public bool DebugLogging { get; set; }

    [JsonPropertyName("mouse_zone_percent")]
    public int MouseZonePercent { get; set; } = 65;

    [JsonPropertyName("custom_theme_colors")]
    public Dictionary<string, CustomStateStyle>? CustomThemeColors { get; set; }

    [JsonPropertyName("popup_keybinds")]
    public Dictionary<string, string>? PopupKeybinds { get; set; } = new()
    {
        ["ReviewAgain"] = "1",
        ["ReviewHard"] = "2",
        ["ReviewGood"] = "3",
        ["ReviewEasy"] = "4",
        ["NeverForget"] = "m",
        ["Blacklist"] = "b",
        ["Suspend"] = "s",
        ["Forget"] = "f",
        ["Mine"] = "d"
    };

}
