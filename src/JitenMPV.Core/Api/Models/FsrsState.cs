namespace JitenMPV.Core.Api.Models;

/// The scheduler's card state, as returned by srs/review and srs/batch-review. Distinct from
/// KnownState and NOT interchangeable with it: the two enums overlap numerically but disagree
/// from value 3 upward.
public enum FsrsState
{
    New = 0,
    Learning = 1,
    Review = 2,
    Relearning = 3,
    Blacklisted = 4,
    Mastered = 5,
    Suspended = 6
}

public static class FsrsStateMapper
{
    /// Matches the API's own threshold in UserController.ComputeEffectiveCategory.
    private const double MatureIntervalDays = 21;

    /// Mirrors ComputeEffectiveCategory: the terminal states map directly, while a scheduled card
    /// is Young or Mature depending on whether its next interval reaches the maturity threshold.
    /// Without a due date the interval is unknown, so a scheduled card is reported as Young.
    public static KnownState ToKnownState(FsrsState state, DateTimeOffset? nextDue, DateTimeOffset nowUtc)
        => state switch
        {
            FsrsState.New => KnownState.New,
            FsrsState.Blacklisted => KnownState.Blacklisted,
            FsrsState.Mastered => KnownState.Mastered,
            FsrsState.Suspended => KnownState.Suspended,
            _ => nextDue is { } due && (due - nowUtc).TotalDays >= MatureIntervalDays
                ? KnownState.Mature
                : KnownState.Young
        };
}
