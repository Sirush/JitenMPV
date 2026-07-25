namespace JitenMPV.Core.Api.Models;

public static class KnownStates
{
    /// The API returns every state a card holds, with the tier last for scheduled cards
    /// (`[Due, Young]`) but the parked flags last for others (`[Young, Suspended]`). Taking the
    /// first element therefore drops Suspended and Redundant, so collapse by specificity instead.
    private static readonly KnownState[] Priority =
    [
        KnownState.Blacklisted,
        KnownState.Mastered,
        KnownState.Redundant,
        KnownState.Suspended,
        KnownState.Due,
        KnownState.Mature,
        KnownState.Young,
        KnownState.New
    ];

    public static KnownState Collapse(IReadOnlyList<KnownState> states)
    {
        if (states.Count == 0) return KnownState.New;
        if (states.Count == 1) return states[0];

        foreach (var candidate in Priority)
            if (states.Contains(candidate))
                return candidate;

        return states[0];
    }
}
