using System.Text;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Plugin;

/// libass auto-wraps a line that does not fit (smart wrap) when the overlay leaves the wrap style
/// at its default, but SubtitleMeasurer can only place hitboxes on the explicit lines it splits on.
/// This turns libass's invisible breaks into real \n's: every logical line is measured with the
/// display's own tags, the measured block height says how many visual lines libass would draw, and
/// the first character of each of those lines is found by measurement. The wrapped text comes back
/// with a companion entry whose token offsets were re-based to the inserted breaks, so the tokens
/// stay whole even when one lies across a break: rendering keeps the word in one colour and the
/// measurer gives it one hitbox rect per line it occupies, all pointing at the same word. The
/// overlay is then rendered with \q2, which disables automatic wrapping and keeps the drawn text
/// identical to measurement.
public sealed class SubtitleWrapResolver(PluginSettings settings, OsdState osd)
{
    /// Base of this resolver's temporary measurement overlays. Grows upward with each probe like
    /// SubtitleMeasurer's block does; shared ids would only be a hazard if two measurement passes
    /// were in flight together, and the joiner, this resolver and the measurer each await the
    /// previous one before starting. The one region it must never sweep is its own range, which
    /// the removal below bounds exactly.
    private const int MeasureId = 60;

    private const string SentinelGlyph = "国";
    private const float MeasureOrigin = 64f;

    private volatile PluginSettings _settings = settings;
    private double? _pitch;

    public void UpdateSettings(PluginSettings newSettings)
    {
        var old = _settings;
        _settings = newSettings;

        if (old.FontFamily != newSettings.FontFamily ||
            old.FontSize != newSettings.FontSize ||
            Math.Abs(old.BorderSize - newSettings.BorderSize) > 0.01)
        {
            _pitch = null;
        }
    }

    /// Returns the text with <c>\n</c> inserted at each wrap point, or the input unchanged when
    /// nothing wraps. A probe failure degrades to the input: sub-parity with today, where hitboxes
    /// simply stop moving with the wrapped text.
    public async Task<WrappedSubtitle?> ResolveAsync(
        string text, ParseCacheEntry entry, MpvIpcClient ipc, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var lines = SplitLines(text);
        var resolved = new string[lines.Count];
        var breaks = new List<int>();
        bool changed = false;
        int offset = 0;
        var ids = new IdAllocator(MeasureId);
        for (int i = 0; i < lines.Count; i++)
        {
            (resolved[i], var localBreaks) = await WrapLineAsync(lines[i], ipc, ids, ct);
            changed |= !ReferenceEquals(resolved[i], lines[i]);
            foreach (int at in localBreaks) breaks.Add(offset + at);
            offset += lines[i].Length + 1;
        }

        if (ids.Next > MeasureId)
            await RemoveOverlaysAsync(ipc, ids.Next, ct);

        if (!changed) return null;
        return new WrappedSubtitle(string.Join('\n', resolved), RebaseEntry(entry, breaks));
    }

    private async Task<(string Text, List<int> Breaks)> WrapLineAsync(
        string line, MpvIpcClient ipc, IdAllocator ids, CancellationToken ct)
    {
        if (line.Length == 0) return (line, []);

        var s = _settings;
        var styleTags = OverlayRenderer.BuildStyleTags(s);
        float resX = OverlayRenderer.ComputeResX(osd.Width, osd.Height);
        int align = OverlayRenderer.ClampAlign(s.SubtitleAlignment);
        var posTags = OverlayRenderer.BuildPositionTags(resX, s, align);

        // The display's own tags without \q2: wrapping must happen here exactly like it happens
        // on screen, whose wrap decisions depend on the same style, position and play-res width.
        var fullAss = $@"{{\an{align}{posTags}{styleTags}\shad0\blur0}}{AssTagBuilder.EscapeText(line)}";
        var fullBounds = await ipc.MeasureOverlayAsync(ids.Next++, fullAss, ct);
        if (fullBounds is null) return (line, []);

        if (await EnsurePitchAsync(ipc, ids, ct) is not { } pitch || pitch <= 0)
            return (line, []);

        int visualLines = Math.Max(1, (int)Math.Round(fullBounds.Height / pitch));
        if (visualLines <= 1) return (line, []);

        var breaks = new List<int>(visualLines - 1);
        int searchFrom = 1;
        for (int target = 2; target <= visualLines && searchFrom < line.Length; target++)
        {
            var found = await FindLineStartAsync(
                line, target, searchFrom, ipc, ids, ct, pitch);
            if (found is not { } at) break;

            breaks.Add(at);
            searchFrom = at + 1;
        }

        if (breaks.Count == 0) return (line, []);

        // \n here is what AssTagBuilder later escapes to \N, so the drawn overlay carries the same
        // breaks these positions describe.
        var sb = new StringBuilder(line.Length + breaks.Count);
        int prev = 0;
        foreach (int at in breaks)
        {
            sb.Append(line, prev, at - prev).Append('\n');
            prev = at;
        }
        sb.Append(line, prev, line.Length - prev);
        return (sb.ToString(), breaks);
    }

    /// Copies the entry's tokens onto the wrapped text: each inserted break shifts every later
    /// character by one, so a token's start moves by the number of breaks at or before it. Tokens
    /// are not split here - a word straddling a break keeps its whole range, which is what gives
    /// it one colour and one hitbox per line segment while still pointing at a single word.
    private static ParseCacheEntry RebaseEntry(ParseCacheEntry entry, List<int> breaks)
    {
        if (breaks.Count == 0) return entry;

        var shifted = new List<ReaderToken>(entry.Tokens.Count);
        int bi = 0;
        foreach (var token in entry.Tokens)
        {
            while (bi < breaks.Count && breaks[bi] <= token.Start) bi++;

            // A break inside the token's span adds one character (the inserted \n) to the word in
            // the wrapped text, so Length and End must grow by that count or the trailing glyphs
            // fall off the styled run and off the token's last hitbox segment. bi is left alone so
            // the next token's start still shifts by every break at or before it.
            int bj = bi;
            while (bj < breaks.Count && breaks[bj] < token.Start + token.Length) bj++;
            int inside = bj - bi;

            shifted.Add(new ReaderToken
            {
                WordId = token.WordId,
                ReadingIndex = token.ReadingIndex,
                Start = token.Start + bi,
                End = token.End + bi + inside,
                Length = token.Length + inside,
                Conjugations = token.Conjugations
            });
        }

        return ParseCacheEntry.FromTokens(
            shifted, entry.VocabStates, entry.FrequencyRanks, entry.VocabDetails, entry.PitchClasses);
    }

    /// First character index whose measured prefix already spans <paramref name="target"/> visual
    /// lines, or null when the full line never reaches that many (a rounding boundary last line).
    private async Task<int?> FindLineStartAsync(
        string line, int target, int lo, MpvIpcClient ipc,
        IdAllocator ids, CancellationToken ct, double pitch)
    {
        if (lo >= line.Length) return null;

        int hi = line.Length - 1;
        int? found = null;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (await VisualLinesAsync(line[..mid], ipc, ids, ct, pitch) >= target)
            {
                found = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return found;
    }

    /// Number of visual lines the prefix occupies when wrapped with the display's own tags. The
    /// blocks stack at the line pitch, so block height over pitch is the line count; the last one
    /// rounds down when it falls short of a full pitch.
    private async Task<int> VisualLinesAsync(
        string prefix, MpvIpcClient ipc, IdAllocator ids, CancellationToken ct, double pitch)
    {
        var s = _settings;
        var styleTags = OverlayRenderer.BuildStyleTags(s);
        float resX = OverlayRenderer.ComputeResX(osd.Width, osd.Height);
        int align = OverlayRenderer.ClampAlign(s.SubtitleAlignment);
        var posTags = OverlayRenderer.BuildPositionTags(resX, s, align);
        var ass = $@"{{\an{align}{posTags}{styleTags}\shad0\blur0}}{AssTagBuilder.EscapeText(prefix)}";
        var bounds = await ipc.MeasureOverlayAsync(ids.Next++, ass, ct);
        return bounds is { Height: > 0 } ? Math.Max(1, (int)Math.Round(bounds.Height / pitch)) : 1;
    }

    /// Distance from one line's top to the next, read from two stacked glyphs of the subtitle's
    /// own font. Validated to a \q2 block so the pair cannot wrap past each other.
    private async Task<double?> EnsurePitchAsync(
        MpvIpcClient ipc, IdAllocator ids, CancellationToken ct)
    {
        if (_pitch is { } cached) return cached;

        var s = _settings;
        var styleTags = OverlayRenderer.BuildStyleTags(s);
        string ProbeTags() => $@"{{\an7\pos({MeasureOrigin:F0},{MeasureOrigin:F0})\q2{styleTags}\shad0\blur0}}";
        var oneLine = await ipc.MeasureOverlayAsync(ids.Next++, $"{ProbeTags()}{SentinelGlyph}", ct);
        var twoLines = await ipc.MeasureOverlayAsync(
            ids.Next++, $@"{ProbeTags()}{SentinelGlyph}\N{SentinelGlyph}", ct);

        if (oneLine is not { } one || twoLines is not { } two || two.Height - one.Height <= 1)
            return null;

        _pitch = two.Height - one.Height;
        return _pitch;
    }

    /// Async methods cannot take ref parameters, and the ids are threaded through several awaits.
    private sealed class IdAllocator(int start)
    {
        public int Next { get; set; } = start;
    }

    private static Task RemoveOverlaysAsync(MpvIpcClient ipc, int nextId, CancellationToken ct)
        => Task.WhenAll(
            Enumerable.Range(MeasureId, nextId - MeasureId)
                .Select(id => ipc.RemoveOverlayAsync(id, ct)));

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == '\n')
            {
                lines.Add(text[start..i]);
                start = i + 1;
            }
        }
        return lines;
    }
}

/// The resolve result: the display text with its breaks and the parse entry the caller should
/// keep, whose token offsets were moved onto that text.
public sealed record WrappedSubtitle(string Text, ParseCacheEntry Entry);