using System.Text.Json;
using System.Text.Json.Serialization;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Plus;

public sealed class JitenPlusCacheFile
{
    [JsonPropertyName("tier")] public int Tier { get; set; }
    [JsonPropertyName("used_bytes")] public long UsedBytes { get; set; }
    [JsonPropertyName("max_bytes")] public long MaxBytes { get; set; }
    [JsonPropertyName("fetched_at")] public DateTimeOffset FetchedAt { get; set; }
}

/// Owns %APPDATA%/jiten-mpv/jitenplus.json. Kept out of config.json so the settings window's save,
/// which rewrites that whole file, can never clobber a tier refresh from the plugin process.
public static class JitenPlusCache
{
    private static readonly string CachePath = Path.Combine(SettingsManager.ConfigDir, "jitenplus.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static JitenPlusSnapshot Load()
    {
        try
        {
            if (!File.Exists(CachePath)) return JitenPlusSnapshot.Unknown;

            var file = JsonSerializer.Deserialize<JitenPlusCacheFile>(
                File.ReadAllText(CachePath), JsonOptions);
            if (file is null) return JitenPlusSnapshot.Unknown;

            var tier = Enum.IsDefined(typeof(JitenPlusTier), file.Tier)
                ? (JitenPlusTier)file.Tier
                : JitenPlusTier.None;

            return new JitenPlusSnapshot(tier, file.UsedBytes, file.MaxBytes, file.FetchedAt,
                FromCache: true, Error: null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return JitenPlusSnapshot.Unknown;
        }
    }

    public static void Save(JitenPlusSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(SettingsManager.ConfigDir);
            var json = JsonSerializer.Serialize(new JitenPlusCacheFile
            {
                Tier = (int)snapshot.Tier,
                UsedBytes = snapshot.UsedBytes,
                MaxBytes = snapshot.MaxBytes,
                FetchedAt = snapshot.FetchedAt
            }, JsonOptions);

            var tmpPath = CachePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, CachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale cache only costs one extra status round-trip next start.
        }
    }
}
