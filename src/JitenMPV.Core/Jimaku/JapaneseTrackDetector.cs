using System.Text.Json;
using System.Text.RegularExpressions;

namespace JitenMPV.Core.Jimaku;

public sealed record SubtitleTrack(
    int Id,
    string? Lang,
    string? Title,
    string? ExternalFilename,
    string? Codec,
    bool Selected,
    bool Default,
    bool Forced);

public enum JapaneseTrackState
{
    Selected,
    Available,
    Missing
}

public static partial class JapaneseTrackDetector
{
    private static readonly HashSet<string> BitmapCodecs =
        ["hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvdsub", "dvb_subtitle", "dvbsub", "xsub"];

    public static List<SubtitleTrack> ParseTrackList(JsonElement trackList)
    {
        var tracks = new List<SubtitleTrack>();
        if (trackList.ValueKind != JsonValueKind.Array) return tracks;

        foreach (var track in trackList.EnumerateArray())
        {
            if (track.ValueKind != JsonValueKind.Object || String(track, "type") != "sub") continue;
            if (!track.TryGetProperty("id", out var id) || !id.TryGetInt32(out var trackId)) continue;

            tracks.Add(new SubtitleTrack(
                trackId,
                String(track, "lang"),
                String(track, "title"),
                String(track, "external-filename"),
                String(track, "codec"),
                Bool(track, "selected"),
                Bool(track, "default"),
                Bool(track, "forced")));
        }
        return tracks;
    }

    public static bool IsBitmap(SubtitleTrack track)
        => track.Codec is { } codec && BitmapCodecs.Contains(codec.ToLowerInvariant());

    public static bool IsJapanese(SubtitleTrack track)
    {
        if (track.Lang is { Length: > 0 } lang && JapaneseLang().IsMatch(lang)) return true;
        if (track.Title is { Length: > 0 } title
            && (JapaneseTitle().IsMatch(title) || title.Contains("日本語", StringComparison.Ordinal)))
            return true;
        return track.ExternalFilename is { Length: > 0 } file && JapaneseFileTag().IsMatch(Path.GetFileName(file));
    }

    /// Picture tracks do not count: they carry no text to colour.
    public static (JapaneseTrackState State, SubtitleTrack? Track) Find(
        IReadOnlyList<SubtitleTrack> tracks, Func<SubtitleTrack, bool>? isJapanese = null)
    {
        isJapanese ??= IsJapanese;
        var usable = tracks.Where(t => !IsBitmap(t) && isJapanese(t)).ToList();
        if (usable.FirstOrDefault(t => t.Selected) is { } selected)
            return (JapaneseTrackState.Selected, selected);

        var best = usable
            .OrderByDescending(t => (t.Forced || SignsTitle().IsMatch(t.Title ?? "") ? -2 : 0) + (t.Default ? 1 : 0))
            .FirstOrDefault();
        return best is null ? (JapaneseTrackState.Missing, null) : (JapaneseTrackState.Available, best);
    }

    public static bool LooksJapanese(string text)
    {
        var kana = 0;
        var letters = 0;
        foreach (var c in text)
        {
            if (c is >= '぀' and <= 'ヿ') kana++;
            if (char.IsLetter(c)) letters++;
        }
        return kana >= 20 && kana * 10 >= letters;
    }

    private static string? String(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Bool(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^(?:ja|jp|jpn)(?:[-_].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex JapaneseLang();

    [GeneratedRegex(@"(?<![A-Za-z])(?:japanese|jpn|jap|jp)(?![A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex JapaneseTitle();

    [GeneratedRegex(@"[._\-\[ ](?:ja|jp|jpn|japanese)(?:[-_][a-z]{2})?[._\]\[ ]", RegexOptions.IgnoreCase)]
    private static partial Regex JapaneseFileTag();

    [GeneratedRegex(@"\b(?:signs?|songs?|forced)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SignsTitle();
}
