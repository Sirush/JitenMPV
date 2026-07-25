using System.Text.Json;

namespace JitenMPV.Core.Config;

public static class SettingsManager
{
    public static readonly string ConfigDir = Path.Combine(
                                                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                            "jiten-mpv");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<PluginSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPath))
            return new PluginSettings();

        PluginSettings settings;
        try
        {
            var json = await File.ReadAllTextAsync(ConfigPath, ct);
            settings = JsonSerializer.Deserialize<PluginSettings>(json, JsonOptions) ?? new PluginSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new PluginSettings();
        }

        if (settings.BottomMargin != 50 && settings.SubtitleMarginY == 50
            && settings.SubtitleAlignment == 2)
        {
            settings.SubtitleMarginY = settings.BottomMargin;
        }

        return settings;
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