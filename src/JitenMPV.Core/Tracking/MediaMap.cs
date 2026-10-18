using System.Text.Json;
using System.Text.Json.Serialization;
using JitenMPV.Core.Config;
using JitenMPV.Core.MediaInfo;

namespace JitenMPV.Core.Tracking;

public sealed class MediaMapEntry
{
    [JsonPropertyName("deck_id")]
    public int? DeckId { get; set; }

    /// Added to the episode number parsed from the filename.
    [JsonPropertyName("episode_offset")]
    public int EpisodeOffset { get; set; }

    [JsonPropertyName("ignore")]
    public bool Ignore { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

/// Deck choices the user made in the picker, keyed by series folder (or title when there is none).
public sealed class MediaMap(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Lock _lock = new();
    private Dictionary<string, MediaMapEntry>? _entries;

    public static string DefaultPath => Path.Combine(AppPaths.ConfigDir, "media-map.json");

    /// The season is part of the key because a "Season 2" folder resolves to its series folder.
    public static string? KeyFor(MediaIdentity identity)
    {
        var (kind, name) = identity.FolderTitle is { } folder
            ? ("folder", folder)
            : ("title", identity.SeriesTitle ?? identity.Title);
        if (string.IsNullOrWhiteSpace(name)) return null;

        var key = $"{kind}:{string.Join(' ', name.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))}";
        return identity.Season is > 1 and var season ? $"{key}#s{season}" : key;
    }

    public MediaMapEntry? Get(string key)
    {
        lock (_lock)
            return Load().GetValueOrDefault(key);
    }

    public void Set(string key, MediaMapEntry entry)
    {
        lock (_lock)
        {
            Load()[key] = entry;
            Save();
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            if (Load().Remove(key))
                Save();
        }
    }

    private Dictionary<string, MediaMapEntry> Load()
    {
        if (_entries is not null) return _entries;
        try
        {
            _entries = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, MediaMapEntry>>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _entries = null;
        }
        return _entries ??= [];
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmpPath = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(_entries, JsonOptions));
        File.Move(tmpPath, path, overwrite: true);
    }
}
