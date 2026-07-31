using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Interaction;

public sealed class KeybindManager
{
    private const string LuaTarget = "jiten_mpv";

    private readonly MpvIpcClient _ipc;
    private readonly ILogger _logger;
    private Dictionary<string, string> _keybinds = new();
    private bool _enabled;

    /// The popup is what the keys act on, so its visibility is what the binding follows. Tracked
    /// apart from _enabled: nothing is bound while the map is empty, but a reconfigure that fills
    /// it has to bind straight away rather than wait for the next popup.
    private bool _wanted;

    public KeybindManager(MpvIpcClient ipc, ILogger logger)
    {
        _ipc = ipc;
        _logger = logger;
    }

    public async Task ConfigureKeybindsAsync(PluginSettings settings, CancellationToken ct)
    {
        bool wasWanted = _wanted;
        await DisableKeybindsAsync(ct);

        await _ipc.SendScriptMessageAsync(LuaTarget, "jiten-reset-keybinds", ct);

        _keybinds = (settings.PopupKeybinds ?? [])
            .Where(kv => settings.ReviewsEnabled || !PopupActions.IsReviewKeybind(kv.Key))
            .Where(kv => settings.MiningEnabled || kv.Key != nameof(PopupAction.Mine))
            .ToDictionary();

        foreach (var (action, key) in _keybinds)
            await _ipc.SendScriptMessageAsync(LuaTarget, "jiten-set-keybind", action, key, ct);

        _logger.LogDebug("Configured {Count} keybinds", _keybinds.Count);

        if (wasWanted)
            await EnableKeybindsAsync(ct);
    }

    public async Task EnableKeybindsAsync(CancellationToken ct)
    {
        _wanted = true;
        if (_enabled || _keybinds.Count == 0) return;
        _enabled = true;
        await _ipc.SendScriptMessageAsync(LuaTarget, "jiten-enable-keybinds", ct);
    }

    public async Task DisableKeybindsAsync(CancellationToken ct)
    {
        _wanted = false;
        if (!_enabled) return;
        _enabled = false;
        await _ipc.SendScriptMessageAsync(LuaTarget, "jiten-disable-keybinds", ct);
    }
}
