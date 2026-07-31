using System.Text.Json.Serialization;
using JitenMPV.Core.Fonts;

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
    public string FontFamily { get; set; } = DefaultSubtitleFont.Value;

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

    /// Unwraps the file's own line breaks, but only for lines whose joined form still fits the
    /// screen: a line libass had to re-wrap would break the hit-test rectangles, which assume one
    /// rendered line per break in the text.
    [JsonPropertyName("subtitle_single_line")]
    public bool SubtitleSingleLine { get; set; }

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

    /// Dwell a word on another subtitle line has to earn before it takes an open popup over, so
    /// the words swept on the way to that popup do not each claim it in passing.
    [JsonPropertyName("popup_switch_delay_ms")]
    public int PopupSwitchDelayMs { get; set; } = 250;

    [JsonPropertyName("popup_auto_hide")]
    public bool PopupAutoHide { get; set; } = true;

    [JsonPropertyName("popup_auto_hide_delay_ms")]
    public int PopupAutoHideDelayMs { get; set; } = 500;

    [JsonPropertyName("popup_hide_after_action")]
    public bool PopupHideAfterAction { get; set; }

    /// Sets the reading as ruby over the headword instead of as a bracketed line above it.
    [JsonPropertyName("popup_furigana")]
    public bool PopupFurigana { get; set; } = true;

    [JsonPropertyName("popup_show_pitch")]
    public bool PopupShowPitch { get; set; } = true;

    /// Draws mora/contour diagrams instead of the raw accent numbers. Nested under PopupShowPitch.
    [JsonPropertyName("popup_pitch_diagram")]
    public bool PopupPitchDiagram { get; set; } = true;

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

    /// Suppresses the jiten.moe vocabulary link behind the headword.
    [JsonPropertyName("popup_disable_headword_link")]
    public bool PopupDisableHeadwordLink { get; set; }

    /// Places the action, rotation and grading rows under the entry instead of above it.
    [JsonPropertyName("popup_move_actions_bottom")]
    public bool PopupMoveActionsBottom { get; set; }

    [JsonPropertyName("popup_show_rotate_actions")]
    public bool PopupShowRotateActions { get; set; }

    [JsonPropertyName("popup_show_review")]
    public bool PopupShowReview { get; set; } = true;

    [JsonPropertyName("popup_use_two_grades")]
    public bool PopupUseTwoGrades { get; set; }

    [JsonPropertyName("popup_position")]
    public PopupPositionMode PopupPosition { get; set; } = PopupPositionMode.AboveSubtitle;

    /// Corner or edge the popup pins to, used only by the Fixed position mode.
    [JsonPropertyName("popup_fixed_anchor")]
    public PopupAnchor PopupFixedAnchor { get; set; } = PopupAnchor.TopCenter;

    /// Distance from the pointer, or from the screen edge when anchored. Has to clear half the
    /// subtitle line height for the popup not to sit on the text the pointer is inside of.
    [JsonPropertyName("popup_offset_px")]
    public int PopupOffsetPx { get; set; } = 60;

    [JsonPropertyName("popup_max_width_px")]
    public int PopupMaxWidthPx { get; set; } = 550;

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

    [JsonPropertyName("rotate_states_enabled")]
    public bool RotateStatesEnabled { get; set; }

    /// Keeps the rotation among the states; otherwise it also passes through a cleared slot.
    [JsonPropertyName("rotate_cycle")]
    public bool RotateCycle { get; set; }

    [JsonPropertyName("rotate_cycle_never_forget")]
    public bool RotateCycleNeverForget { get; set; } = true;

    [JsonPropertyName("rotate_cycle_blacklist")]
    public bool RotateCycleBlacklist { get; set; } = true;

    [JsonPropertyName("rotate_cycle_suspended")]
    public bool RotateCycleSuspended { get; set; }

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

    /// Paints each word's hit-test region over the subtitle in its own colour.
    [JsonPropertyName("debug_show_hitboxes")]
    public bool DebugShowHitboxes { get; set; }

    [JsonPropertyName("mouse_zone_percent")]
    public int MouseZonePercent { get; set; } = 65;

    /// Fades a clickable button into the top-right corner while the pointer moves, as a mouse-only
    /// route to the settings window for users who never learn the Ctrl+j binding.
    [JsonPropertyName("settings_button_enabled")]
    public bool SettingsButtonEnabled { get; set; } = true;

    /// Companion buttons in the bottom corners that step to the previous or next subtitle.
    [JsonPropertyName("subtitle_nav_buttons_enabled")]
    public bool SubtitleNavButtonsEnabled { get; set; } = true;

    /// Always-live mpv keys for subtitle navigation, empty to leave unbound. Unlike the popup
    /// keybinds these do not claim a key that input.conf already binds.
    [JsonPropertyName("keybind_prev_sub")]
    public string KeybindPrevSub { get; set; } = "Ctrl+LEFT";

    [JsonPropertyName("keybind_next_sub")]
    public string KeybindNextSub { get; set; } = "Ctrl+RIGHT";

    /// Replays the current line until pressed again.
    [JsonPropertyName("keybind_loop_sub")]
    public string KeybindLoopSub { get; set; } = "Ctrl+l";

    /// Colours subtitle words by pitch class instead of leaving the SRS colour alone.
    [JsonPropertyName("pitch_coloring_enabled")]
    public bool PitchColoringEnabled { get; set; }

    [JsonPropertyName("pitch_indicator")]
    public PitchIndicatorMode PitchIndicator { get; set; } = PitchIndicatorMode.Text;

    /// Bar thickness for underline mode, in the 720-high overlay space the subtitle is laid out in.
    [JsonPropertyName("pitch_underline_thickness")]
    public double PitchUnderlineThickness { get; set; } = 4;

    /// Keyed by PitchClass name; absent entries fall back to PitchAccent.DefaultColor.
    [JsonPropertyName("pitch_styles")]
    public Dictionary<string, CustomStateStyle>? PitchStyles { get; set; }

    [JsonPropertyName("custom_theme_colors")]
    public Dictionary<string, CustomStateStyle>? CustomThemeColors { get; set; }

    /// Master switch for screenshot/audio capture on mine. No-ops when the account is not Jiten+.
    [JsonPropertyName("media_capture_enabled")]
    public bool MediaCaptureEnabled { get; set; }

    [JsonPropertyName("media_capture_image")]
    public bool MediaCaptureImage { get; set; } = true;

    /// Replays the subtitle's time range as an animated WebP instead of a single frame.
    [JsonPropertyName("media_capture_image_animated")]
    public bool MediaCaptureImageAnimated { get; set; }

    [JsonPropertyName("media_capture_audio")]
    public bool MediaCaptureAudio { get; set; } = true;

    /// Opens the trim/preview window before uploading. Off means mine-and-upload with the defaults.
    [JsonPropertyName("media_review_popup")]
    public bool MediaReviewPopup { get; set; } = true;

    [JsonPropertyName("media_overwrite_prompt")]
    public MediaOverwritePrompt MediaOverwritePrompt { get; set; } = MediaOverwritePrompt.Always;

    [JsonPropertyName("media_image_source")]
    public MediaImageSource MediaImageSource { get; set; } = MediaImageSource.MpvFrame;

    /// Defaults to None: a card that shows the answer teaches nothing, and the sentence text is
    /// already carried by the example-sentence field.
    [JsonPropertyName("media_subtitle_burn")]
    public MediaSubtitleBurn MediaSubtitleBurn { get; set; } = MediaSubtitleBurn.None;

    /// Matches the server's CardMediaImageProcessor.MaxLongEdge, so its re-encode is a no-op resize.
    [JsonPropertyName("media_image_max_edge")]
    public int MediaImageMaxEdge { get; set; } = 1600;

    /// The server re-encodes to WebP q82 regardless, so sending at q82 would compress twice at the
    /// same setting and lose detail for nothing. Sending higher softens that second pass; 100 sends
    /// losslessly, which avoids it entirely at several times the upload size.
    [JsonPropertyName("media_image_quality")]
    public int MediaImageQuality { get; set; } = 95;

    /// Below the server's 300-frame cap; past that the server stores the file unprocessed.
    [JsonPropertyName("media_anim_max_frames")]
    public int MediaAnimMaxFrames { get; set; } = 280;

    [JsonPropertyName("media_anim_target_fps")]
    public int MediaAnimTargetFps { get; set; } = 15;

    [JsonPropertyName("media_anim_min_fps")]
    public int MediaAnimMinFps { get; set; } = 5;

    [JsonPropertyName("media_anim_max_edge")]
    public int MediaAnimMaxEdge { get; set; } = 960;

    /// The server re-encodes clips to WebP q82 whatever arrives, so sending above 82 costs upload
    /// bytes for no gain, and sending below it throws away quality the stored file could have kept.
    [JsonPropertyName("media_anim_quality")]
    public int MediaAnimQuality { get; set; } = 82;

    [JsonPropertyName("media_anim_max_bytes")]
    public int MediaAnimMaxBytes { get; set; } = 2_500_000;

    [JsonPropertyName("media_audio_bitrate_kbps")]
    public int MediaAudioBitrateKbps { get; set; } = 48;

    [JsonPropertyName("media_audio_stereo")]
    public bool MediaAudioStereo { get; set; }

    [JsonPropertyName("media_audio_max_bytes")]
    public int MediaAudioMaxBytes { get; set; } = 1_500_000;

    /// Silence-aware expansion/contraction of the subtitle range before the fixed pads are applied.
    [JsonPropertyName("media_audio_auto_trim")]
    public bool MediaAudioAutoTrim { get; set; } = true;

    [JsonPropertyName("media_audio_pad_lead_ms")]
    public int MediaAudioPadLeadMs { get; set; } = 250;

    [JsonPropertyName("media_audio_pad_tail_ms")]
    public int MediaAudioPadTailMs { get; set; } = 350;

    /// Decoded on each side of the subtitle so the review popup's handles have room to drag into.
    [JsonPropertyName("media_audio_window_margin_s")]
    public double MediaAudioWindowMarginSeconds { get; set; } = 5.0;

    /// Neighbouring subtitle lines offered in the review popup's sentence selector, each side.
    [JsonPropertyName("media_sentence_context_lines")]
    public int MediaSentenceContextLines { get; set; } = 2;

    /// Empty searches the managed install, PATH, then well-known locations.
    [JsonPropertyName("ffmpeg_path")]
    public string FfmpegPath { get; set; } = "";

    /// Suppresses the startup OSD notice for users who have decided to live without ffmpeg.
    [JsonPropertyName("ffmpeg_prompt_dismissed")]
    public bool FfmpegPromptDismissed { get; set; }

    /// Asks GitHub once a day whether a newer release exists. Nothing is ever downloaded without
    /// the user asking for it.
    [JsonPropertyName("update_check_enabled")]
    public bool UpdateCheckEnabled { get; set; } = true;

    /// Read by the Lua script, not by this process: it decides whether to spawn the plugin on
    /// file-loaded. Changes take effect when mpv restarts, since the script reads it once at load.
    [JsonPropertyName("plugin_autostart")]
    public bool PluginAutostart { get; set; } = true;

    /// Key that starts the plugin manually. Bound whether or not autostart is on, so a crashed
    /// plugin can always be brought back.
    [JsonPropertyName("plugin_start_key")]
    public string PluginStartKey { get; set; } = "F10";

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
