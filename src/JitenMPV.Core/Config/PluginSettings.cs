using System.Text.Json.Serialization;

namespace JitenMPV.Core.Config;

public sealed class PluginSettings
{
    [JsonPropertyName("api_base_url")]
    public string ApiBaseUrl { get; set; } = "https://api.jiten.moe";

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("font_family")]
    public string FontFamily { get; set; } = "Yu Gothic";

    [JsonPropertyName("font_size")]
    public int FontSize { get; set; } = 48;

    [JsonPropertyName("border_size")]
    public double BorderSize { get; set; } = 3.0;

    [JsonPropertyName("bottom_margin")]
    public int BottomMargin { get; set; } = 50;

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
    public List<int> BlurStates { get; set; } = [0];

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
    public bool AutopauseEnabled { get; set; }

    [JsonPropertyName("autopause_delay_ms")]
    public int AutopauseDelayMs { get; set; }

    [JsonPropertyName("mining_enabled")]
    public bool MiningEnabled { get; set; } = true;

    [JsonPropertyName("mining_capture_sentence")]
    public bool MiningCaptureSentence { get; set; } = true;

    [JsonPropertyName("inline_review_enabled")]
    public bool InlineReviewEnabled { get; set; } = true;
}
