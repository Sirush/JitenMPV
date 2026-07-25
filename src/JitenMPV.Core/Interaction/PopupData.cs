using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Interaction;

public sealed record PopupData
{
    public required string Spelling { get; init; }
    public required string Reading { get; init; }
    public int FrequencyRank { get; init; }
    public required IReadOnlyList<string> PartsOfSpeech { get; init; }
    public required IReadOnlyList<int> PitchAccents { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> MeaningsChunks { get; init; }
    public required IReadOnlyList<string> Conjugations { get; init; }
    public KnownState State { get; init; }
    public int WordId { get; init; }
    public byte ReadingIndex { get; init; }

    // State action visibility
    public bool ShowNeverForget { get; init; }
    public bool ShowBlacklist { get; init; }
    public bool ShowSuspend { get; init; }
    public bool ShowForget { get; init; }
    public bool ShowStateActions => ShowNeverForget || ShowBlacklist || ShowSuspend || ShowForget;
    public bool IsNeverForgotten { get; init; }
    public bool IsBlacklisted { get; init; }
    public bool IsSuspended { get; init; }

    // Review visibility
    public bool ShowReview { get; init; }
    public bool UseTwoGrades { get; init; }

    // Popup appearance
    public string PopupBgColor { get; init; } = "#1A1A1A";
    public int PopupBgOpacity { get; init; } = 200;
    public double FontScale { get; init; } = 1.0;
    public PopupPositionMode PositionMode { get; init; } = PopupPositionMode.AboveSubtitle;
}
