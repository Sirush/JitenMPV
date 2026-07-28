using System.Text.Json;
using System.Text.Json.Serialization;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Update;

/// Bookkeeping for the update check, deliberately kept out of config.json: saving settings writes
/// that whole file back, so a check running in the plugin process would race the settings window
/// and one of the two would lose its changes.
public sealed class UpdateState
{
    [JsonPropertyName("last_check_utc")]
    public DateTimeOffset? LastCheckUtc { get; set; }

    [JsonPropertyName("known_latest_version")]
    public string? KnownLatestVersion { get; set; }

    [JsonPropertyName("known_latest_tag")]
    public string? KnownLatestTag { get; set; }

    [JsonPropertyName("etag")]
    public string? ETag { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string Path => System.IO.Path.Combine(AppPaths.ConfigDir, "update-state.json");

    public static async Task<UpdateState> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(Path)) return new UpdateState();

            var json = await File.ReadAllTextAsync(Path, ct);
            return JsonSerializer.Deserialize<UpdateState>(json, JsonOptions) ?? new UpdateState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new UpdateState();
        }
    }

    public static async Task SaveAsync(UpdateState state, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ConfigDir);
            await File.WriteAllTextAsync(Path, JsonSerializer.Serialize(state, JsonOptions), ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the throttle costs one request on the next launch, which is not worth
            // interrupting playback over.
        }
    }
}
