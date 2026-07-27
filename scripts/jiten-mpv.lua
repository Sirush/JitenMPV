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
local was_in_zone = false

local osd_width, osd_height = 1280, 720
local mouse_zone = 0.65

local owned_keys = { MBTN_LEFT = true, MBTN_LEFT_DBL = true }

-- mpv hands a key to exactly one binding, so claiming these two means the command they would
-- otherwise have run has to be replayed here whenever the plugin reports the click hit no word.
-- Window dragging needs no entry: mpv drives it from the raw button state, not from the binding.
local fallback = { MBTN_LEFT_DBL = "cycle fullscreen" }

local function resolve_fallbacks()
    local bindings = mp.get_property_native("input-bindings")
    if not bindings then return end

    local best = {}
    for _, b in ipairs(bindings) do
        local prio = b.priority or 0
        -- Script-owned bindings (the OSC seek bar) sit in sections that are only enabled while the
        -- pointer is over them, so replaying their commands from here would fire them out of context.
        if owned_keys[b.key] and not b.owner and b.cmd and b.cmd ~= "" and prio >= 0
           and (not best[b.key] or prio >= best[b.key]) then
            best[b.key] = prio
            fallback[b.key] = b.cmd
        end
    end
end

local function run_fallback(key)
    local cmd = fallback[key]
    if cmd then mp.command(cmd) end
end

local bar_refresh

local loop = { enabled = false }

local function loop_arm()
    local a = mp.get_property_number("sub-start")
    local b = mp.get_property_number("sub-end")
    if not a or not b or b <= a then return end
    mp.set_property_number("ab-loop-a", a)
    mp.set_property_number("ab-loop-b", b)
end

local function loop_clear()
    mp.set_property("ab-loop-a", "no")
    mp.set_property("ab-loop-b", "no")
end

-- Looping is mpv's own A-B range pinned to the line's timestamps, so the wrap is frame accurate
-- rather than a polled seek. Toggling it on between two lines arms nothing; the sub-start observer
-- picks the next line up, which is also what re-aims the loop after a step or a manual seek.
local function loop_toggle()
    loop.enabled = not loop.enabled
    if loop.enabled then loop_arm() else loop_clear() end
    bar_refresh()
end

local nav_actions = {
    prev_sub = function() mp.commandv("sub-seek", "-1") end,
    next_sub = function() mp.commandv("sub-seek", "1") end,
    loop_sub = loop_toggle
}

local nav_keys = { prev_sub = "Ctrl+LEFT", next_sub = "Ctrl+RIGHT", loop_sub = "Ctrl+l" }

local function bind_nav_key(name)
    local id = "jiten-" .. name
    mp.remove_key_binding(id)

    -- Not forced: a key the user has already bound in input.conf keeps its own meaning.
    local key = nav_keys[name]
    if key and key ~= "" then mp.add_key_binding(key, id, nav_actions[name]) end
end

local BUTTON_IDLE_S = 0.8
local BUTTON_FADE_S = 0.15
local BUTTON_FRAME_S = 1 / 30
local BUTTON_MARGIN_RATIO = 0.022

-- Deep enough to clear mpv's own bottom bar, which the same pointer movement brings up.
local BUTTON_BOTTOM_MARGIN_RATIO = 0.13
local BUTTON_FONT_RATIO = 0.028
local BUTTON_BG_OPACITY = 0.72
local BUTTON_TEXT_COLOR = "FFFFFF"
local BUTTON_HOVER_COLOR = "FFB366"
local BUTTON_ACTIVE_COLOR = "7AE07A"
local BUTTON_BG_COLOR = "1A1A1A"
local BUTTON_ACTIVE_BG_COLOR = "0A2A0A"

local bar = {
    client = nil,
    settings_enabled = true,
    nav_enabled = true,
    has_subs = false,
    overlay = nil,
    geometry = nil,
    font_size = 16,
    alpha = 0,
    target = 0,
    idle_deadline = 0,
    timer = nil,
    last_step = 0,
    hovered = nil,
    press_consumed = false
}

-- Clicking the settings button can only reach a plugin that has told us its IPC client name, so an
-- unnamed plugin means no button rather than a button that does nothing. Subtitle stepping is
-- mpv's own, and stays available while the plugin is down.
local buttons = {
    {
        label = "Jiten Settings",
        align = 9,
        available = function() return bar.settings_enabled and bar.client ~= nil end,
        action = function() mp.commandv("script-message-to", bar.client, "jiten-open-settings") end
    },
    {
        label = "« Prev sub",
        align = 1,
        available = function() return bar.nav_enabled and bar.has_subs end,
        action = nav_actions.prev_sub
    },
    {
        label = "Loop sub",
        align = 1,
        available = function() return bar.nav_enabled and bar.has_subs end,
        active = function() return loop.enabled end,
        action = nav_actions.loop_sub
    },
    {
        label = "Next sub »",
        align = 3,
        available = function() return bar.nav_enabled and bar.has_subs end,
        action = nav_actions.next_sub
    }
}

local function bar_available()
    if osd_width <= 0 or osd_height <= 0 then return false end
    for _, b in ipairs(buttons) do
        if b.available() then return true end
    end
    return false
end

local function alpha_tag(opacity)
    return string.format("%02X", math.floor((1 - opacity) * 255 + 0.5))
end

local function button_text_ass(b, color, opacity)
    return string.format(
        "{\\an%d\\pos(%d,%d)\\fs%d\\bord1\\shad0\\1c&H%s&\\3c&H000000&\\alpha&H%s&}%s",
        b.align, b.text_x, b.text_y, bar.font_size, color, alpha_tag(opacity), b.label)
end

-- Laying the overlay out at the window's own resolution keeps ASS units and the pixels
-- mp.get_mouse_pos reports on the same scale, so the measured box is the hit box.
local function bar_measure()
    local key = osd_width .. "x" .. osd_height
    if bar.geometry == key then return end

    if not bar.overlay then
        bar.overlay = mp.create_osd_overlay("ass-events")
    end

    local ov = bar.overlay
    local margin = math.floor(osd_height * BUTTON_MARGIN_RATIO)
    local bottom_margin = math.floor(osd_height * BUTTON_BOTTOM_MARGIN_RATIO)
    bar.font_size = math.max(12, math.floor(osd_height * BUTTON_FONT_RATIO))

    -- The text is inset by the padding so that the box drawn around it, not the glyphs, is what
    -- ends up a margin away from the corner. The measured bounds already reserve descender space
    -- below the glyphs, so padding the bottom as much as the top reads as a lopsided box.
    local pad_x = math.floor(bar.font_size * 0.55)
    local pad_top = math.floor(bar.font_size * 0.32)
    local pad_bottom = math.floor(bar.font_size * 0.06)
    local gap = math.floor(bar.font_size * 0.4)

    ov.res_x = osd_width
    ov.res_y = osd_height

    -- Buttons sharing a corner queue inward from it in list order. Slots are handed out whether or
    -- not the button is currently shown, so availability changing never shifts the others and the
    -- cached layout stays valid.
    local edges = {}

    for _, b in ipairs(buttons) do
        local left = b.align == 1
        local edge = edges[b.align] or (left and margin or (osd_width - margin))
        b.text_x = left and (edge + pad_x) or (edge - pad_x)
        b.text_y = b.align == 9 and (margin + pad_top)
                                or (osd_height - bottom_margin - pad_bottom)

        ov.hidden = true
        ov.compute_bounds = true
        ov.data = button_text_ass(b, BUTTON_TEXT_COLOR, 1)
        local bounds = ov:update()
        ov.hidden = false
        ov.compute_bounds = false

        local x0, y0, x1, y1
        if bounds and bounds.x1 and bounds.x1 > bounds.x0 then
            x0, y0, x1, y1 = bounds.x0, bounds.y0, bounds.x1, bounds.y1
        else
            local width = #b.label * bar.font_size * 0.5
            x0 = left and b.text_x or (b.text_x - width)
            x1 = x0 + width
            y0 = b.align == 9 and b.text_y or (b.text_y - bar.font_size)
            y1 = y0 + bar.font_size
        end

        b.rect = {
            x0 = math.floor(x0 - pad_x),
            y0 = math.floor(y0 - pad_top),
            x1 = math.floor(x1 + pad_x),
            y1 = math.floor(y1 + pad_bottom)
        }

        edges[b.align] = left and (b.rect.x1 + gap) or (b.rect.x0 - gap)
    end

    bar.geometry = key
end

local function bar_render()
    if not bar.overlay then return end

    local events = {}
    for _, b in ipairs(buttons) do
        if b.rect and b.available() then
            local on = b.active ~= nil and b.active()
            local text_color = BUTTON_TEXT_COLOR
            if bar.hovered == b then
                text_color = BUTTON_HOVER_COLOR
            elseif on then
                text_color = BUTTON_ACTIVE_COLOR
            end

            events[#events + 1] = string.format(
                "{\\an7\\pos(0,0)\\bord0\\shad0\\1c&H%s&\\alpha&H%s&\\p1}m %d %d l %d %d l %d %d l %d %d{\\p0}",
                on and BUTTON_ACTIVE_BG_COLOR or BUTTON_BG_COLOR,
                alpha_tag(bar.alpha * BUTTON_BG_OPACITY),
                b.rect.x0, b.rect.y0, b.rect.x1, b.rect.y0,
                b.rect.x1, b.rect.y1, b.rect.x0, b.rect.y1)
            events[#events + 1] = button_text_ass(b, text_color, bar.alpha)
        end
    end

    bar.overlay.data = table.concat(events, "\n")
    bar.overlay:update()
end

local function bar_stop_timer()
    if bar.timer then
        bar.timer:kill()
        bar.timer = nil
    end
end

local function bar_step()
    local now = mp.get_time()
    local step = (now - bar.last_step) / BUTTON_FADE_S
    bar.last_step = now

    if bar.alpha < bar.target then
        bar.alpha = math.min(bar.target, bar.alpha + step)
    else
        bar.alpha = math.max(bar.target, bar.alpha - step)
    end

    if bar.alpha <= 0 then
        bar.alpha = 0
        bar_stop_timer()
        if bar.overlay then bar.overlay:remove() end
        return
    end

    bar_render()
    if bar.alpha == bar.target then bar_stop_timer() end
end

local function bar_animate()
    if bar.timer then return end
    bar.last_step = mp.get_time()
    bar.timer = mp.add_periodic_timer(BUTTON_FRAME_S, bar_step)
end

local function bar_hide(immediate)
    bar.target = 0
    bar.hovered = nil
    if immediate then
        bar_stop_timer()
        bar.alpha = 0
        if bar.overlay then bar.overlay:remove() end
    elseif bar.alpha > 0 then
        bar_animate()
    end
end

local function button_at(mx, my)
    for _, b in ipairs(buttons) do
        local r = b.rect
        if r and b.available()
           and mx >= r.x0 and mx <= r.x1 and my >= r.y0 and my <= r.y1 then
            return b
        end
    end
    return nil
end

local function bar_on_move(mx, my)
    if not bar_available() then
        if bar.alpha > 0 then bar_hide(true) end
        return
    end

    bar_measure()
    local hovered = button_at(mx, my)
    local hover_changed = hovered ~= bar.hovered
    bar.hovered = hovered
    bar.idle_deadline = mp.get_time() + BUTTON_IDLE_S

    if bar.target ~= 1 then
        bar.target = 1
        bar_animate()
    elseif hover_changed then
        bar_render()
    end
end

-- The pointer resting on a button keeps the bar up; fading out from under it would take the click
-- the user is about to make with it.
local function bar_tick()
    if bar.target == 1 and not bar.hovered and mp.get_time() >= bar.idle_deadline then
        bar.target = 0
        bar_animate()
    end
end

local function bar_hit(mx, my)
    if bar.alpha <= 0 or not bar_available() then return nil end
    return button_at(mx, my)
end

-- Re-lays the bar out and drops buttons that just became unavailable.
function bar_refresh(relayout)
    if relayout then bar.geometry = nil end
    if bar.alpha <= 0 then return end

    if not bar_available() then
        bar_hide(true)
        return
    end

    bar_measure()
    if bar.hovered and not bar.hovered.available() then bar.hovered = nil end
    bar_render()
end

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
        bar.client = nil
        bar_refresh()
        mp.msg.warn("JitenMPV plugin exited; press F10 or load a file to restart it")
    end)
end

local function send(...)
    mp.commandv("script-message", ...)
end

local function on_mouse_left(tbl)
    local mx, my = mp.get_mouse_pos()

    if tbl.event == "down" then
        -- Reassigned on every press: focus moving to the settings window can eat the release that
        -- would otherwise clear it, and a stale flag would swallow the next click's release.
        local hit = bar_hit(mx, my)
        bar.press_consumed = hit ~= nil
        if hit then
            hit.action()
            return
        end
        if not plugin_started then
            run_fallback("MBTN_LEFT")
            return
        end
        send("jiten-mouse-left-press", tostring(mx), tostring(my))
    elseif tbl.event == "up" then
        if bar.press_consumed then
            bar.press_consumed = false
            return
        end
        if plugin_started then
            send("jiten-mouse-left-release", tostring(mx), tostring(my))
        end
    end
end

local function on_double_click()
    local mx, my = mp.get_mouse_pos()
    if bar_hit(mx, my) then return end
    if not plugin_started then
        run_fallback("MBTN_LEFT_DBL")
        return
    end
    send("jiten-double-click", tostring(mx), tostring(my))
end

local mouse_timer = nil

local function poll_mouse()
    if not mouse_tracking then return end
    local mx, my = mp.get_mouse_pos()

    if mx ~= last_mouse_x or my ~= last_mouse_y then
        last_mouse_x = mx
        last_mouse_y = my
        bar_on_move(mx, my)

        if plugin_started then
            if my >= osd_height * mouse_zone then
                was_in_zone = true
                send("jiten-mouse-move", tostring(mx), tostring(my))
            elseif was_in_zone then
                was_in_zone = false
                send("jiten-mouse-leave", "0", "0")
            end
        end
    end

    bar_tick()
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
    bar_hide(true)
end

mp.register_event("file-loaded", initialize)
mp.add_key_binding("F10", "jiten-mpv-toggle", initialize)

resolve_fallbacks()
mp.add_forced_key_binding("MBTN_LEFT", "jiten-mouse-left", on_mouse_left, { complex = true })
mp.add_forced_key_binding("MBTN_LEFT_DBL", "jiten-mouse-dbl", on_double_click)

mp.register_script_message("jiten-passthrough-click", function() run_fallback("MBTN_LEFT") end)
mp.register_script_message("jiten-passthrough-dbl", function() run_fallback("MBTN_LEFT_DBL") end)

mp.observe_property("osd-width", "number", function(_, val)
    if val and val > 0 and val ~= osd_width then
        osd_width = val
        bar_refresh(true)
    end
end)

mp.observe_property("osd-height", "number", function(_, val)
    if val and val > 0 and val ~= osd_height then
        osd_height = val
        bar_refresh(true)
    end
end)

-- No selected subtitle track means sub-seek has nothing to step through, so the group goes away.
mp.observe_property("sid", "native", function(_, val)
    local has_subs = val ~= nil and val ~= false
    if has_subs ~= bar.has_subs then
        bar.has_subs = has_subs
        bar_refresh()
    end
end)

-- Fires when another line becomes current, which is the loop's cue to move with it. Looping back
-- to A leaves the value alone, so a running loop does not re-arm itself.
mp.observe_property("sub-start", "number", function(_, val)
    if loop.enabled and val then loop_arm() end
end)

-- A loop range from the previous file would trap playback in a stretch of this one.
mp.register_event("file-loaded", function()
    if not loop.enabled then return end
    loop.enabled = false
    loop_clear()
    bar_refresh()
end)

for name in pairs(nav_keys) do bind_nav_key(name) end

mp.register_script_message("jiten-set-nav-key", function(name, key)
    if not nav_actions[name] then return end
    nav_keys[name] = key or ""
    bind_nav_key(name)
end)

mp.register_script_message("jiten-enable-tracking", enable_tracking)
mp.register_script_message("jiten-disable-tracking", disable_tracking)
mp.register_script_message("jiten-set-mouse-zone", function(pct)
    mouse_zone = tonumber(pct) / 100.0
end)

mp.register_script_message("jiten-set-client", function(name)
    bar.client = (name ~= nil and name ~= "") and name or nil
    bar_refresh()
end)

mp.register_script_message("jiten-set-buttons", function(settings_value, nav_value)
    bar.settings_enabled = settings_value == "1"
    bar.nav_enabled = nav_value == "1"
    bar_refresh()
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
