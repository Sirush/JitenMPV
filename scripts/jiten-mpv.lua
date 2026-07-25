local utils = require "mp.utils"

local function get_exe_path()
    local appdata = os.getenv("APPDATA")
    if appdata then
        return utils.join_path(utils.join_path(appdata, "jiten-mpv"), "JitenMPV.App.exe")
    end
    local home = os.getenv("HOME") or ""
    return utils.join_path(
        utils.join_path(utils.join_path(home, ".local"), "share"),
        utils.join_path("jiten-mpv", "JitenMPV.App"))
end

local plugin_started = false
local mouse_tracking = false
local last_mouse_x, last_mouse_y = -1, -1
local last_click_time = 0
local last_click_x, last_click_y = 0, 0
local was_in_zone = false

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

    -- test-mpv.bat starts the plugin itself on this pipe; a second instance would fight it for
    -- the connection. The latch stays set so mouse tracking still reports events.
    if mp.get_opt("jiten_external") then
        mp.msg.info("JitenMPV: using externally started plugin on " .. ipc_path)
        return
    end

    local exe = get_exe_path()
    mp.msg.info("Spawning JitenMPV: " .. exe .. " plugin " .. ipc_path)
    -- Not detached: the exit callback is the only way to learn the plugin died, and without
    -- clearing the latch neither file-loaded nor F10 could ever respawn it.
    mp.command_native_async({
        name = "subprocess",
        playback_only = false,
        args = { exe, "plugin", ipc_path }
    }, function()
        plugin_started = false
        mp.msg.warn("JitenMPV plugin exited; press F10 or load a file to restart it")
    end)
end

local function send(...)
    mp.commandv("script-message", ...)
end

local function on_mouse_left(tbl)
    if not plugin_started then return end
    local mx, my = mp.get_mouse_pos()

    if tbl.event == "down" then
        send("jiten-mouse-left-press", tostring(mx), tostring(my))

        local now = mp.get_time()
        if now - last_click_time < 0.3
           and math.abs(mx - last_click_x) < 10
           and math.abs(my - last_click_y) < 10 then
            send("jiten-double-click", tostring(mx), tostring(my))
        end
        last_click_time = now
        last_click_x = mx
        last_click_y = my
    elseif tbl.event == "up" then
        send("jiten-mouse-left-release", tostring(mx), tostring(my))
    end
end

local mouse_timer = nil

local mouse_zone = 0.65
local osd_height = 720

local function poll_mouse()
    if not mouse_tracking or not plugin_started then return end
    local mx, my = mp.get_mouse_pos()
    if mx == last_mouse_x and my == last_mouse_y then return end
    last_mouse_x = mx
    last_mouse_y = my

    if my >= osd_height * mouse_zone then
        was_in_zone = true
        send("jiten-mouse-move", tostring(mx), tostring(my))
    elseif was_in_zone then
        was_in_zone = false
        send("jiten-mouse-leave", "0", "0")
    end
end

local function enable_tracking()
    if mouse_tracking then return end
    mouse_tracking = true
    if not mouse_timer then
        mouse_timer = mp.add_periodic_timer(1 / 15, poll_mouse)
    else
        mouse_timer:resume()
    end
end

local function disable_tracking()
    mouse_tracking = false
    last_mouse_x = -1
    last_mouse_y = -1
    was_in_zone = false
    if mouse_timer then mouse_timer:stop() end
end

mp.register_event("file-loaded", initialize)
mp.add_key_binding("F10", "jiten-mpv-toggle", initialize)
mp.add_key_binding("MBTN_LEFT", "jiten-mouse-left", on_mouse_left, { complex = true })

mp.observe_property("osd-height", "number", function(_, val)
    if val and val > 0 then osd_height = val end
end)

mp.register_script_message("jiten-enable-tracking", enable_tracking)
mp.register_script_message("jiten-disable-tracking", disable_tracking)
mp.register_script_message("jiten-set-mouse-zone", function(pct)
    mouse_zone = tonumber(pct) / 100.0
end)

-- Keybind system
local keybind_config = {}
local keybinds_active = false

mp.register_script_message("jiten-set-keybind", function(action, key)
    keybind_config[action] = key
end)

mp.register_script_message("jiten-reset-keybinds", function()
    if keybinds_active then
        for action, _ in pairs(keybind_config) do
            mp.remove_key_binding("jiten-kb-" .. action)
        end
        keybinds_active = false
    end
    keybind_config = {}
end)

mp.register_script_message("jiten-enable-keybinds", function()
    if keybinds_active then return end
    keybinds_active = true
    for action, key in pairs(keybind_config) do
        mp.add_forced_key_binding(key, "jiten-kb-" .. action, function()
            send("jiten-keybind-action", action)
        end)
    end
end)

mp.register_script_message("jiten-disable-keybinds", function()
    if not keybinds_active then return end
    keybinds_active = false
    for action, _ in pairs(keybind_config) do
        mp.remove_key_binding("jiten-kb-" .. action)
    end
end)

enable_tracking()
