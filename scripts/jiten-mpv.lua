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
local cached_osd_height = 720

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

local function on_mouse_left(tbl)
    if not plugin_started then return end
    local mx, my = mp.get_mouse_pos()

    if tbl.event == "down" then
        mp.commandv("script-message", "jiten-mouse-left-press", tostring(mx), tostring(my))

        local now = mp.get_time()
        if now - last_click_time < 0.3
           and math.abs(mx - last_click_x) < 10
           and math.abs(my - last_click_y) < 10 then
            mp.commandv("script-message", "jiten-double-click", tostring(mx), tostring(my))
        end
        last_click_time = now
        last_click_x = mx
        last_click_y = my
    elseif tbl.event == "up" then
        mp.commandv("script-message", "jiten-mouse-left-release", tostring(mx), tostring(my))
    end
end

local mouse_timer = nil

local function poll_mouse()
    if not mouse_tracking then return end
    local mx, my = mp.get_mouse_pos()
    if mx == last_mouse_x and my == last_mouse_y then return end
    last_mouse_x = mx
    last_mouse_y = my

    if my >= cached_osd_height * 0.65 then
        was_in_zone = true
        mp.commandv("script-message", "jiten-mouse-move", tostring(mx), tostring(my))
    elseif was_in_zone then
        was_in_zone = false
        mp.commandv("script-message", "jiten-mouse-leave", "0", "0")
    end
end

local function enable_tracking()
    if mouse_tracking then return end
    mouse_tracking = true
    if not mouse_timer then
        mouse_timer = mp.add_periodic_timer(1 / 60, poll_mouse)
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
    if val and val > 0 then cached_osd_height = val end
end)

mp.register_script_message("jiten-enable-tracking", enable_tracking)
mp.register_script_message("jiten-disable-tracking", disable_tracking)
