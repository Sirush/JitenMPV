using System.Text.Json;
using JitenMPV.Core.Mpv;

namespace JitenMPV.Core.Media;

public static class TrackIndexResolver
{
    /// The selected track's index among tracks of its own type, which is what ffmpeg's
    /// <c>0:a:&lt;n&gt;</c> / <c>0:s:&lt;n&gt;</c> stream specifiers count. Null when none is selected.
    public static int? Find(JsonElement? trackList, string type)
    {
        if (trackList is not { ValueKind: JsonValueKind.Array } list)
            return null;

        var index = 0;
        foreach (var track in list.EnumerateArray())
        {
            if (!track.TryGetProperty("type", out var typeEl) || typeEl.GetString() != type)
                continue;

            if (track.TryGetProperty("selected", out var selected)
                && selected.ValueKind == JsonValueKind.True)
                return index;

            index++;
        }

        return null;
    }

    public static async Task<int?> FindAsync(MpvIpcClient ipc, string type, CancellationToken ct)
    {
        try
        {
            return Find(await ipc.GetPropertyRawAsync("track-list", ct), type);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
