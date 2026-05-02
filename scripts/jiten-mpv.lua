local utils = require "mp.utils"

local function get_exe_path()
    local appdata = os.getenv("APPDATA")
    if appdata then
        return utils.join_path(appdata, "jiten-mpv", "JitenMPV.App.exe")
    end
    local home = os.getenv("HOME") or ""
    return utils.join_path(home, ".local", "share", "jiten-mpv", "JitenMPV.App")
end

local plugin_started = false

local function initialize()
    if plugin_started then return end

    local ipc_path = mp.get_property("input-ipc-server")
    if not ipc_path or ipc_path == "" then
        if package.config:sub(1, 1) == "\\" then
            ipc_path = "\\\\.\\pipe\\mpv-jiten-" .. mp.get_property("pid")
        else
            ipc_path = "/tmp/mpv-jiten-" .. mp.get_property("pid")
        end
        mp.set_property("input-ipc-server", ipc_path)
    end

    plugin_started = true
    local exe = get_exe_path()
    mp.msg.info("Spawning JitenMPV: " .. exe .. " plugin " .. ipc_path)
    mp.command_native({
        name = "subprocess",
        playback_only = false,
        detach = true,
        args = { exe, "plugin", ipc_path }
    })
end

mp.register_event("file-loaded", initialize)
mp.add_key_binding("F10", "jiten-mpv-toggle", initialize)
