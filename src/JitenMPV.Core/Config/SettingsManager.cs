using System.Text.Json;

namespace JitenMPV.Core.Config;

public static class SettingsManager
{
    public static readonly string ConfigDir = AppPaths.ConfigDir;

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<PluginSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPath))
            return new PluginSettings();

        try
        {
            var json = await File.ReadAllTextAsync(ConfigPath, ct);
            var settings = JsonSerializer.Deserialize<PluginSettings>(json, JsonOptions) ?? new PluginSettings();
            ApplyLegacyMigrations(json, settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new PluginSettings();
        }
    }

    /// Reads keys that no longer exist on PluginSettings, so they must come from the raw document.
    /// Migrated values are dropped from disk by the next save.
    private static void ApplyLegacyMigrations(string json, PluginSettings settings)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

        if (doc.RootElement.TryGetProperty("bottom_margin", out var bottomMargin)
            && bottomMargin.ValueKind == JsonValueKind.Number
            && bottomMargin.TryGetInt32(out var margin)
            && margin != 50 && settings.SubtitleMarginY == 50 && settings.SubtitleAlignment == 2)
        {
            settings.SubtitleMarginY = margin;
        }

        if (!doc.RootElement.TryGetProperty("reviews_enabled", out _)
            && doc.RootElement.TryGetProperty("inline_review_enabled", out var legacyReviews)
            && legacyReviews.ValueKind == JsonValueKind.False)
        {
            settings.ReviewsEnabled = false;
        }
    }

    public static async Task SaveAsync(PluginSettings settings, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);

        var tmpPath = ConfigPath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, ConfigPath, overwrite: true);
    }
}