using System.Text.Json;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Tracking;

/// A watched episode to write to Jiten. Episode is null for a movie, which is its own unit.
public sealed record EpisodeMark(
    int DeckId,
    int? Episode,
    string Title,
    bool MarkEarlier,
    bool MarkShowCompleted,
    bool KeepShowStatus,
    DeckStatus? ShowStatusBefore);

/// Marks are stored before they are sent, so one cut off by mpv quitting or the network going
/// down is retried on the next start.
public sealed class PendingMarks(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const int MaxPending = 50;

    private readonly Lock _lock = new();

    public static string DefaultPath => Path.Combine(AppPaths.ConfigDir, "pending-episodes.json");

    public IReadOnlyList<EpisodeMark> All()
    {
        lock (_lock)
            return Load();
    }

    public void Add(EpisodeMark mark)
    {
        lock (_lock)
        {
            var marks = Load();
            marks.RemoveAll(m => SameTarget(m, mark));
            marks.Add(mark);
            if (marks.Count > MaxPending)
                marks.RemoveRange(0, marks.Count - MaxPending);
            Save(marks);
        }
    }

    public void Remove(EpisodeMark mark)
    {
        lock (_lock)
        {
            var marks = Load();
            if (marks.RemoveAll(m => SameTarget(m, mark)) > 0)
                Save(marks);
        }
    }

    private static bool SameTarget(EpisodeMark a, EpisodeMark b)
        => a.DeckId == b.DeckId && a.Episode == b.Episode;

    private List<EpisodeMark> Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<EpisodeMark>>(File.ReadAllText(path), JsonOptions) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    private void Save(List<EpisodeMark> marks)
    {
        if (marks.Count == 0)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmpPath = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(marks, JsonOptions));
        File.Move(tmpPath, path, overwrite: true);
    }
}
