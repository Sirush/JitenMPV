using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private string _pipePath { get; } = pipePath;

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _connection.ConnectAsync(_pipePath, ct);
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

    public Task ShowOverlayAsync(int id, string assText, CancellationToken ct)
        => SendNamedCommandAsync(new JsonObject
        {
            ["name"] = "osd-overlay",
            ["id"] = id,
            ["format"] = "ass-events",
            ["data"] = assText,
            ["res_y"] = 720
        }, ct);

    public Task RemoveOverlayAsync(int id, CancellationToken ct)
        => SendNamedCommandAsync(new JsonObject
        {
            ["name"] = "osd-overlay",
            ["id"] = id,
            ["format"] = "none",
            ["data"] = ""
        }, ct);

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
        }

        foreach (var tcs in _pending.Values)
            tcs.TrySetCanceled();
        _pending.Clear();
    }

    private void HandleEvent(JsonElement root, string? eventName)
    {
        if (eventName != "property-change") return;
        if (!root.TryGetProperty("name", out var nameEl)) return;

        var propName = nameEl.GetString();
        string? stringValue = null;
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String)
            stringValue = dataEl.GetString();

        if (propName == "sub-text")
            SubtitleTextChanged?.Invoke(stringValue);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
