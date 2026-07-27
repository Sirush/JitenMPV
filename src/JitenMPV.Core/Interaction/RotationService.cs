using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Interaction;

/// Cycles a card through the master / blacklist / suspend states, mirroring the Reader
/// extension's rotation actions.
public sealed class RotationService(PluginSettings settings)
{
    private volatile PluginSettings _settings = settings;

    public void UpdateSettings(PluginSettings newSettings) => _settings = newSettings;

    public bool Enabled => _settings.RotateStatesEnabled && BuildCycle().Count > 0;

    public bool ShowActions => Enabled && _settings.PopupShowRotateActions;

    /// The slots in the order the Reader offers them; a null slot clears every state.
    public List<PopupAction?> BuildCycle()
    {
        var s = _settings;

        List<PopupAction?> cycle = [];
        if (s.RotateCycleNeverForget) cycle.Add(PopupAction.NeverForget);
        if (s.RotateCycleBlacklist) cycle.Add(PopupAction.Blacklist);
        if (s.RotateCycleSuspended) cycle.Add(PopupAction.Suspend);
        if (cycle.Count > 0 && !s.RotateCycle) cycle.Add(null);

        return cycle;
    }

    /// <param name="next">The state to end up on, or null to end up cleared.</param>
    /// <returns>False when rotation is off or nothing is configured to rotate through.</returns>
    public bool TryGetNext(KnownState state, int direction, out PopupAction? next)
    {
        next = null;
        if (!_settings.RotateStatesEnabled) return false;

        var cycle = BuildCycle();
        if (cycle.Count == 0) return false;

        // A state excluded from the cycle is not a position in it, so rotation restarts from the
        // end the user is heading towards.
        int index = cycle.IndexOf(StateOf(state));
        int target = index < 0
            ? direction > 0 ? 0 : cycle.Count - 1
            : (index + direction) % cycle.Count;
        if (target < 0) target += cycle.Count;

        next = cycle[target];
        return true;
    }

    /// The rotatable state the card currently carries, or null when it carries none.
    public static PopupAction? StateOf(KnownState state) => state switch
    {
        KnownState.Mastered => PopupAction.NeverForget,
        KnownState.Blacklisted => PopupAction.Blacklist,
        KnownState.Suspended => PopupAction.Suspend,
        _ => null
    };

    public static string Label(PopupAction? state) => state switch
    {
        PopupAction.NeverForget => "Master",
        PopupAction.Blacklist => "Blacklist",
        PopupAction.Suspend => "Suspend",
        _ => "Clear"
    };
}
