using System.Text.Json;

namespace JitenMPV.Core.Config;

public static class SettingsManager
{
    public static readonly string ConfigDir = Path.Combine(
                                                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                            "jiten-mpv");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<PluginSettings> LoadAsync()
    {
        if (!File.Exists(ConfigPath))
            return new PluginSettings();

        var json = await File.ReadAllTextAsync(ConfigPath);
        return JsonSerializer.Deserialize<PluginSettings>(json, JsonOptions) ?? new PluginSettings();
    }

    public static async Task SaveAsync(PluginSettings settings)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(ConfigPath, json);
    }
}