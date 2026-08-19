using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using JitenMPV.Core.Rendering;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Mpv;

public sealed class MpvIpcClient(string pipePath, ILogger logger) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions MpvJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly MpvConnection _connection = new(logger);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> _pending = new();
    private int _nextRequestId;

    public event Action<string?>? SubtitleTextChanged;
    public event Action<MouseEventArgs>? MouseEvent;
    public event Action<string, JsonElement>? PropertyChanged;
    public event Action<string, string[]>? ScriptMessageReceived;

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _connection.ConnectAsync(pipePath, ct);
    }

    private Task<JsonElement?> SendCommandAsync(object[] command, CancellationToken ct)
    {
        var request = new JsonObject { ["command"] = JsonSerializer.SerializeToNode(command, MpvJson) };
        return SendRawAsync(request, ct);
    }

    private Task<JsonElement?> SendNamedCommandAsync(JsonObject command, CancellationToken ct)
    {
        var request = new JsonObject { ["command"] = command };
        return SendRawAsync(request, ct);
    }

    private async Task<JsonElement?> SendRawAsync(JsonObject request, CancellationToken ct)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        await using var reg = ct.Register(() =>
        {
            _pending.TryRemove(requestId, out _);
            tcs.TrySetCanceled(ct);
        });

        request["request_id"] = requestId;
        var json = request.ToJsonString(MpvJson);
        await _connection.SendLineAsync(json, ct);

        return await tcs.Task;
    }

    public Task ObservePropertyAsync(string propertyName, int observeId, CancellationToken ct)
        => SendCommandAsync(["observe_property", observeId, propertyName], ct);

    public Task SetPropertyAsync(string propertyName, object value, CancellationToken ct)
        => SendCommandAsync(["set_property", propertyName, value], ct);

    public async Task<T?> GetPropertyAsync<T>(string propertyName, CancellationToken ct)
    {
        var result = await SendCommandAsync(["get_property", propertyName], ct);
        return result is null ? default : result.Value.Deserialize<T>();
    }

    public Task<JsonElement?> GetPropertyRawAsync(string propertyName, CancellationToken ct)
        => SendCommandAsync(["get_property", propertyName], ct);

    public async Task<string?> GetClientNameAsync(CancellationToken ct)
    {
        var result = await SendCommandAsync(["client_name"], ct);
        return result?.GetString();
    }

    public async Task<string?> ExpandTextAsync(string text, CancellationToken ct)
    {
        var result = await SendCommandAsync(["expand-text", text], ct);
        return result?.GetString();
    }

    public Task ShowOverlayAsync(int id, string assText, CancellationToken ct)
        => SendNamedCommandAsync(new JsonObject
        {
            ["name"] = "osd-overlay",
            ["id"] = id,
            ["format"] = "ass-events",
            ["data"] = assText,
            ["res_y"] = OverlayRenderer.OverlayResY
        }, ct);

    public async Task<OverlayBounds?> MeasureOverlayAsync(int id, string assText, CancellationToken ct)
    {
        var result = await SendNamedCommandAsync(new JsonObject
        {
            ["name"] = "osd-overlay",
            ["id"] = id,
            ["format"] = "ass-events",
            ["data"] = assText,
            ["res_y"] = OverlayRenderer.OverlayResY,
            ["hidden"] = true,
            ["compute_bounds"] = true
        }, ct);

        if (result is not { } el)
            return null;
        if (el.ValueKind != JsonValueKind.Object)
            return null;
        if (!el.TryGetProperty("x0", out var x0El))
            return null;

        return new OverlayBounds(
            x0El.GetDouble(), el.GetProperty("y0").GetDouble(),
            el.GetProperty("x1").GetDouble(), el.GetProperty("y1").GetDouble());
    }

    /// <param name="flags">mpv screenshot flags: "video" (clean frame), "subtitles", or "window"
    /// (as displayed, including every OSD layer).</param>
    public Task ScreenshotToFileAsync(string path, string flags, CancellationToken ct)
        => SendCommandAsync(["screenshot-to-file", path, flags], ct);

    public Task RemoveOverlayAsync(int id, CancellationToken ct)
        => SendNamedCommandAsync(new JsonObject
        {
            ["name"] = "osd-overlay",
            ["id"] = id,
            ["format"] = "none",
            ["data"] = ""
        }, ct);

    public Task SeekAbsoluteAsync(double seconds, CancellationToken ct)
        => SendCommandAsync(["seek", seconds, "absolute+exact"], ct);

    /// mpv's own subtitle stepping. It only reaches events already demuxed, so prefer a timeline
    /// seek where cue timings are known.
    public Task SubSeekAsync(int skip, CancellationToken ct)
        => SendCommandAsync(["sub-seek", skip], ct);

    public async Task RunAsync(CancellationToken ct)
    {
        await foreach (var line in _connection.ReadLinesAsync(ct))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("request_id", out var reqIdEl))
                {
                    var reqId = reqIdEl.GetInt32();
                    JsonElement? resultData = root.TryGetProperty("data", out var dataEl) ? dataEl.Clone() : null;

                    if (_pending.TryRemove(reqId, out var tcs))
                        tcs.TrySetResult(resultData);
                }
                else if (root.TryGetProperty("event", out var eventEl))
                {
                    HandleEvent(root, eventEl.GetString());
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning("Failed to parse mpv message: {Error}", ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Error handling mpv message");
            }
        }

        foreach (var tcs in _pending.Values)
            tcs.TrySetCanceled();
        _pending.Clear();
    }

    private static readonly Dictionary<string, MouseEventType> MouseEventMap = new()
    {
        ["jiten-mouse-move"] = MouseEventType.Move,
        ["jiten-mouse-left-press"] = MouseEventType.LeftPress,
        ["jiten-mouse-left-release"] = MouseEventType.LeftRelease,
        ["jiten-double-click"] = MouseEventType.DoubleClick,
        ["jiten-mouse-leave"] = MouseEventType.Leave
    };

    private void HandleEvent(JsonElement root, string? eventName)
    {
        switch (eventName)
        {
            case "property-change":
                HandlePropertyChange(root);
                break;
            case "client-message":
                HandleClientMessage(root);
                break;
        }
    }

    private void HandlePropertyChange(JsonElement root)
    {
        if (!root.TryGetProperty("name", out var nameEl)) return;
        var propName = nameEl.GetString();

        if (propName == "sub-text")
        {
            string? stringValue = null;
            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String)
                stringValue = dataEl.GetString();
            SubtitleTextChanged?.Invoke(stringValue);
        }
        else if (propName is not null && root.TryGetProperty("data", out var data))
        {
            PropertyChanged?.Invoke(propName, data.Clone());
        }
    }

    private void HandleClientMessage(JsonElement root)
    {
        if (!root.TryGetProperty("args", out var argsEl) || argsEl.ValueKind != JsonValueKind.Array)
            return;

        var argsLen = argsEl.GetArrayLength();
        if (argsLen < 1) return;

        var messageName = argsEl[0].GetString();
        if (messageName is null) return;

        if (MouseEventMap.TryGetValue(messageName, out var eventType))
        {
            if (eventType == MouseEventType.Leave)
            {
                MouseEvent?.Invoke(new MouseEventArgs(eventType, 0, 0));
                return;
            }

            if (argsLen >= 3
                && double.TryParse(argsEl[1].GetString(), out var x)
                && double.TryParse(argsEl[2].GetString(), out var y))
            {
                MouseEvent?.Invoke(new MouseEventArgs(eventType, x, y));
            }
            return;
        }

        if (ScriptMessageReceived is null) return;
        var args = new string[argsLen - 1];
        for (int i = 1; i < argsLen; i++)
            args[i - 1] = argsEl[i].GetString() ?? "";
        ScriptMessageReceived.Invoke(messageName, args);
    }

    public Task SendScriptMessageAsync(string target, string message, CancellationToken ct)
        => SendCommandAsync(["script-message-to", target, message], ct);

    public Task SendScriptMessageAsync(string target, string message, string value, CancellationToken ct)
        => SendCommandAsync(["script-message-to", target, message, value], ct);

    public Task SendScriptMessageAsync(string target, string message, string arg1, string arg2, CancellationToken ct)
        => SendCommandAsync(["script-message-to", target, message, arg1, arg2], ct);

    public Task SendScriptMessageAsync(string target, string message, IReadOnlyList<string> args, CancellationToken ct)
        => SendCommandAsync(["script-message-to", target, message, .. args], ct);

    public Task KeybindAsync(string key, string command, CancellationToken ct)
        => SendCommandAsync(["keybind", key, command], ct);

    public Task ChangeListAsync(string option, string operation, string value, CancellationToken ct)
        => SendCommandAsync(["change-list", option, operation, value], ct);

    /// mpv's own OSD message, deliberately not the status overlay: this carries setup problems the
    /// user needs to see even after switching the overlay off.
    public Task ShowTextAsync(string text, int durationMs, CancellationToken ct)
        => SendCommandAsync(["show-text", text, durationMs], ct);

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
