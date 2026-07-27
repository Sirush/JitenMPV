using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;
using JitenMPV.Core.Pitch;

namespace JitenMPV.Core.Interaction;

public sealed record PopupData
{
    public required string Spelling { get; init; }
    public required string Reading { get; init; }
    public int FrequencyRank { get; init; }
    public required IReadOnlyList<string> PartsOfSpeech { get; init; }
    public required IReadOnlyList<int> PitchAccents { get; init; }
    /// One diagram per accepted accent; empty when diagrams are off or the reading has no kana.
    public IReadOnlyList<PitchDiagramRow> PitchDiagrams { get; init; } = [];
    public required IReadOnlyList<IReadOnlyList<string>> MeaningsChunks { get; init; }
    public required IReadOnlyList<string> Conjugations { get; init; }
    public KnownState State { get; init; }
    /// Every state the card holds (a scheduled young card is both Due and Young).
    public required IReadOnlyList<KnownState> States { get; init; }
    public int WordId { get; init; }
    public byte ReadingIndex { get; init; }
    public bool HeadwordLinkEnabled { get; init; }
    public bool MoveActionsBottom { get; init; }

    // State action visibility
    public bool ShowNeverForget { get; init; }
    public bool ShowBlacklist { get; init; }
    public bool ShowSuspend { get; init; }
    public bool ShowForget { get; init; }
    public bool ShowStateActions => ShowNeverForget || ShowBlacklist || ShowSuspend || ShowForget;
    public bool ShowActionRow => ShowStateActions || ShowMine;
    public bool IsNeverForgotten { get; init; }
    public bool IsBlacklisted { get; init; }
    public bool IsSuspended { get; init; }

    // Mining
    public bool ShowMine { get; init; }
    public bool IsMined { get; init; }
    /// Populated only when mining is not configured to go straight to a fixed deck.
    public IReadOnlyList<DeckOption> DeckOptions { get; init; } = [];
    public bool ShowDeckPicker => ShowMine && DeckOptions.Count > 0;

    public IReadOnlyList<DeckMembershipRow> DeckMembership { get; init; } = [];

    // State rotation
    public bool ShowRotate { get; init; }
    public string RotateForwardLabel { get; init; } = "";
    public string RotateBackwardLabel { get; init; } = "";
    /// False when the cycle is one slot long, where both directions would land on the same state.
    public bool ShowRotateBackward { get; init; }

    // Review visibility
    public bool ShowReview { get; init; }
    public bool UseTwoGrades { get; init; }

    // Popup appearance
    public string PopupBgColor { get; init; } = "#1A1A1A";
    public int PopupBgOpacity { get; init; } = 200;
    public double FontScale { get; init; } = 1.0;
    public PopupPositionMode PositionMode { get; init; } = PopupPositionMode.AboveSubtitle;
}

public sealed record DeckOption(int DeckId, string Name);

/// Carries the colour alongside the shape so the diagram tracks any recoloured pitch classes
/// rather than drifting from the subtitle colouring.
public sealed record PitchDiagramRow(PitchDiagram Diagram, string Color);
