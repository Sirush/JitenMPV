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

    public KeybindManager(MpvIpcClient ipc, ILogger logger)
    {
        _ipc = ipc;
        _logger = logger;
    }

    public async Task ConfigureKeybindsAsync(Dictionary<string, string>? keybinds, bool reviewsEnabled,
        CancellationToken ct)
    {
        bool wasEnabled = _enabled;
        if (wasEnabled)
            await DisableKeybindsAsync(ct);

        await _ipc.SendScriptMessageAsync(LuaTarget, "jiten-reset-keybinds", ct);

        _keybinds = (keybinds ?? [])
            .Where(kv => reviewsEnabled || !PopupActions.IsReviewKeybind(kv.Key))
            .ToDictionary();

        foreach (var (action, key) in _keybinds)
            await _ipc.SendScriptMessageAsync(LuaTarget, "jiten-set-keybind", action, key, ct);

        _logger.LogDebug("Configured {Count} keybinds", _keybinds.Count);

        if (wasEnabled)
            await EnableKeybindsAsync(ct);
    }

    public async Task EnableKeybindsAsync(CancellationToken ct)
    {
        if (_enabled || _keybinds.Count == 0) return;
        _enabled = true;
        await _ipc.SendScriptMessageAsync(LuaTarget, "jiten-enable-keybinds", ct);
    }

    public async Task DisableKeybindsAsync(CancellationToken ct)
    {
        if (!_enabled) return;
        _enabled = false;
        await _ipc.SendScriptMessageAsync(LuaTarget, "jiten-disable-keybinds", ct);
    }
}
