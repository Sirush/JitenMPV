using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Plugin;

public sealed class SubtitleMeasurer(PluginSettings settings, OsdState osd)
{
    private const int MeasureId = 99;

    /// Every prefix is measured with this glyph appended and the glyph's own ink extent subtracted
    /// back out, which recovers the pen position exactly. Measuring the bare prefix instead reads
    /// its last glyph's ink right edge, which overstates the pen in fonts whose glyphs overhang
    /// their advance (Yu Gothic draws ~44px art on a 34px advance at fs48) and understates it for
    /// trailing whitespace, whose ink is trimmed entirely.
    private const string SentinelGlyph = "国";

    /// Measurement pen origin, kept off the canvas edge so outlines are not clipped out of the
    /// reported bounds.
    private const float MeasureOrigin = 64f;

    /// Shift applied to prefix measurements so that long lines whose ink extends past the play-res
    /// width are not clipped at the right edge. Without this, the sentinel-based advance at the
    /// end of a full-width line gets clamped at the play-res, producing zero-width rects for the
    /// last word.
    private const float MeasureShift = 300f;

    private volatile PluginSettings _settings = settings;
    private readonly BoundedCache<string, List<WordRect>> _cache = new(2000);
    private int _lastOsdVersion = -1;

    /// Distance from one line's top to the next. mpv reports a line's rendered box instead, which
    /// runs taller than this, so anything positioning against a line's lower edge needs the pitch.
    /// Depends only on the font, so it survives every subtitle until the font or geometry changes.
    public double? LinePitch { get; private set; }

    public void UpdateSettings(PluginSettings newSettings)
    {
        var old = _settings;
        _settings = newSettings;

        if (old.FontFamily != newSettings.FontFamily ||
            old.FontSize != newSettings.FontSize ||
            Math.Abs(old.BorderSize - newSettings.BorderSize) > 0.01 ||
            old.SubtitleAlignment != newSettings.SubtitleAlignment ||
            old.SubtitleMarginX != newSettings.SubtitleMarginX ||
            old.SubtitleMarginY != newSettings.SubtitleMarginY)
        {
            _cache.Clear();
            LinePitch = null;
        }
    }

    public async Task<List<WordRect>> MeasureAsync(
        string text, ParseCacheEntry entry,
        MpvIpcClient ipc, CancellationToken ct)
    {
        if (osd.Version != _lastOsdVersion)
        {
            _cache.Clear();
            LinePitch = null;
            _lastOsdVersion = osd.Version;
        }

        var cached = _cache.GetOrDefault(text);
        if (cached is not null)
            return cached;

        var rects = await MeasureInternalAsync(text, entry, ipc, ct);
        _cache.TryAdd(text, rects);
        return rects;
    }

    private async Task<List<WordRect>> MeasureInternalAsync(
        string text, ParseCacheEntry entry, MpvIpcClient ipc, CancellationToken ct)
    {
        var s = _settings;
        var styleTags = OverlayRenderer.BuildStyleTags(s);
        float resX = OverlayRenderer.ComputeResX(osd.Width, osd.Height);
        var posTags = OverlayRenderer.BuildPositionTags(resX, s);

        var lines = SplitLines(text);
        var rects = new List<WordRect>();
        int align = OverlayRenderer.ClampAlign(s.SubtitleAlignment);
        int nextId = MeasureId;
        int AllocId() => nextId++;

        // \q2 keeps a measurement that would not fit at the left edge from wrapping, which would
        // report the play-res width instead of the text's. \shad0\blur0 keeps a user's OSD shadow
        // or blur style out of the reported ink bounds.
        string MeasureTags() => $@"{{\an7\pos({MeasureOrigin:F0},{MeasureOrigin:F0})\q2{styleTags}\shad0\blur0}}";

        // \q2 on the display probe too: the overlay it is measured against renders with \q2
        // (SubtitleWrapResolver owns the wrapping), so fullBounds must match what displays.
        var fullAss = $@"{{\an{align}{posTags}\q2{styleTags}\shad0\blur0}}{AssTagBuilder.EscapeText(text)}";
        var fullBounds = await ipc.MeasureOverlayAsync(AllocId(), fullAss, ct);
        if (fullBounds is null)
        {
            await RemoveOverlaysAsync(ipc, nextId, ct);
            return rects;
        }

        var lineInk = new OverlayBounds?[lines.Count];
        var lineCentered = new OverlayBounds?[lines.Count];
        var inkTasks = new Dictionary<int, (Task<OverlayBounds?> Ink, Task<OverlayBounds?> Centered)>();
        for (int li = 0; li < lines.Count; li++)
        {
            var (lineText, _) = lines[li];
            if (lineText.Length == 0) continue;

            var escapedLine = AssTagBuilder.EscapeText(lineText);
            inkTasks[li] = (
                ipc.MeasureOverlayAsync(AllocId(), $"{MeasureTags()}{escapedLine}", ct),
                ipc.MeasureOverlayAsync(AllocId(), $@"{{\an{align}{posTags}\q2{styleTags}\shad0\blur0}}{escapedLine}", ct));
        }

        await Task.WhenAll(inkTasks.Values.SelectMany(t => new[] { t.Ink, t.Centered }));
        foreach (var (li, tasks) in inkTasks)
        {
            lineInk[li] = await tasks.Ink;
            lineCentered[li] = await tasks.Centered;
        }

        int firstIdx = -1, lastIdx = -1;
        for (int li = 0; li < lines.Count; li++)
        {
            if (lineInk[li] is not { Height: > 0 }) continue;
            if (firstIdx < 0) firstIdx = li;
            lastIdx = li;
        }

        if (firstIdx < 0)
        {
            await RemoveOverlaysAsync(ipc, nextId, ct);
            return rects;
        }

        // Line slots are one font line apart regardless of each line's ink, so the spacing is the
        // block height minus the last line's own ink height, spread over the slots between them.
        float lineSpacing = (float)(fullBounds.Height / Math.Max(lines.Count, 1));
        if (lastIdx > firstIdx)
        {
            float derived = (float)((fullBounds.Height - lineInk[lastIdx]!.Height) / (lastIdx - firstIdx));
            if (derived > 1f)
            {
                lineSpacing = derived;
                LinePitch = derived;
            }
        }

        // A one-line subtitle carries no spacing of its own to read, so it is measured against a
        // stacked pair of the same glyph, whose extra height is exactly one line.
        if (LinePitch is null)
        {
            var probeTags = MeasureTags();
            var oneLine = await ipc.MeasureOverlayAsync(AllocId(), $"{probeTags}{SentinelGlyph}", ct);
            var twoLines = await ipc.MeasureOverlayAsync(
                AllocId(), $@"{probeTags}{SentinelGlyph}\N{SentinelGlyph}", ct);

            if (oneLine is not null && twoLines is not null && twoLines.Height - oneLine.Height > 1)
                LinePitch = twoLines.Height - oneLine.Height;
        }

        var linePrefixes = new List<int>?[lines.Count];
        for (int li = 0; li < lines.Count; li++)
        {
            var (lineText, lineStartIdx) = lines[li];
            if (lineText.Length == 0 || lineInk[li] is null || lineCentered[li] is null) continue;

            int lineEnd = lineStartIdx + lineText.Length;
            var positions = new SortedSet<int> { 0, lineText.Length };
            foreach (var token in entry.Tokens)
            {
                int tokenEnd = token.Start + token.Length;
                if (token.Start >= lineEnd || tokenEnd <= lineStartIdx) continue;

                // Tokens straddling the break to the next visual line contribute their segment
                // ends here, so the segment rects below get a measured advance at each end.
                positions.Add(Math.Clamp(token.Start, lineStartIdx, lineEnd) - lineStartIdx);
                positions.Add(Math.Clamp(tokenEnd, lineStartIdx, lineEnd) - lineStartIdx);
            }

            var prefixes = positions.Where(p => p > 0 && p <= lineText.Length).ToList();
            if (prefixes.Count > 0) linePrefixes[li] = prefixes;
        }

        float? sentinelInkX1 = null;
        if (linePrefixes.Any(p => p is not null))
        {
            var sentinelBounds = await ipc.MeasureOverlayAsync(
                AllocId(), $"{MeasureTags()}{SentinelGlyph}", ct);
            if (sentinelBounds is not null && sentinelBounds.X1 > 0)
                sentinelInkX1 = (float)sentinelBounds.X1;
        }

        for (int li = 0; li < lines.Count; li++)
        {
            if (linePrefixes[li] is not { } prefixPositions) continue;

            var (lineText, lineStartIdx) = lines[li];
            int lineEnd = lineStartIdx + lineText.Length;
            var lineTokens = entry.Tokens
                .Select((t, i) => (Index: i, Token: t))
                .Where(x => x.Token.Start < lineEnd && x.Token.Start + x.Token.Length > lineStartIdx)
                .ToList();

            if (lineTokens.Count == 0) continue;
            if (lineInk[li] is not { X1: > 0 } || lineCentered[li] is null) continue;

            var prefixTasks = new Dictionary<int, Task<OverlayBounds?>>();
            var prefixTexts = new Dictionary<int, string>();
            foreach (var pos in prefixPositions)
            {
                var prefixText = AssTagBuilder.EscapeText(lineText[..pos]);
                if (sentinelInkX1 is not null) prefixText += SentinelGlyph;
                prefixTexts[pos] = prefixText;
                // Shift the measurement left so that prefixes extending past the play-res width
                // are not clipped at the right edge. The shift cancels out in BuildAdvances.
                string shiftedTags = $@"{{\an7\pos({MeasureOrigin - MeasureShift:F0},{MeasureOrigin})\q2{styleTags}\shad0\blur0}}";
                prefixTasks[pos] = ipc.MeasureOverlayAsync(AllocId(), $"{shiftedTags}{prefixText}", ct);
            }

            await Task.WhenAll(prefixTasks.Values);

            // A short prefix's ink lands entirely off-canvas at the shifted pen and mpv reports
            // null for it. Such a prefix can never reach the play-res right edge at the normal pen,
            // so re-measure it there; its X1 is unshifted and needs no compensation.
            var adjustedX1s = new Dictionary<int, float?>();
            var retryPositions = new List<int>();
            foreach (var pos in prefixPositions)
            {
                var bounds = await prefixTasks[pos];
                if (bounds is null)
                    retryPositions.Add(pos);
                else
                    adjustedX1s[pos] = (float)bounds.X1 + MeasureShift;
            }

            if (retryPositions.Count > 0)
            {
                var retryTasks = retryPositions
                    .Select(pos => (Pos: pos, Task: ipc.MeasureOverlayAsync(
                        AllocId(), $"{MeasureTags()}{prefixTexts[pos]}", ct)))
                    .ToList();
                await Task.WhenAll(retryTasks.Select(t => t.Task));
                foreach (var (pos, task) in retryTasks)
                    adjustedX1s[pos] = (float?)(await task)?.X1;
            }

            float border = (float)s.BorderSize;
            var advances = BuildAdvances(adjustedX1s, sentinelInkX1, border);

            // The centred line's ink left is offset from its pen origin by the first glyph's side
            // bearing (large for opening brackets) plus any leading whitespace; the an7 measurement
            // of the same line carries the identical offset from MeasureOrigin, so subtracting it
            // recovers the on-screen pen origin that prefix pen positions are relative to.
            float penOrigin = (float)lineCentered[li]!.X0 - ((float)lineInk[li]!.X0 - MeasureOrigin);

            float lineY = (float)fullBounds.Y0 + (li - firstIdx) * lineSpacing;
            float lineHeight = (float)lineInk[li]!.Height;

            // A token wrapped across the line break contributes one rect per line it occupies, so a
            // half at either end of the break stays clickable and points at the same word.
            foreach (var (idx, token) in lineTokens)
            {
                int segStart = Math.Max(token.Start, lineStartIdx);
                int segEnd = Math.Min(token.Start + token.Length, lineEnd);
                int localStart = segStart - lineStartIdx;
                int localEnd = segEnd - lineStartIdx;
                if (localEnd <= localStart) continue;

                float x0 = penOrigin + advances.GetValueOrDefault(localStart, border) - border;
                float x1 = penOrigin + advances.GetValueOrDefault(localEnd, border) - border;

                rects.Add(new WordRect(idx, token.WordId, token.ReadingIndex,
                    x0, lineY, Math.Max(x1 - x0, 1), lineHeight));
            }
        }

        await RemoveOverlaysAsync(ipc, nextId, ct);
        return WordRect.AssignHitRegions(rects);
    }

    private static Task RemoveOverlaysAsync(MpvIpcClient ipc, int nextId, CancellationToken ct)
        => Task.WhenAll(
            Enumerable.Range(MeasureId, nextId - MeasureId)
                .Select(id => ipc.RemoveOverlayAsync(id, ct)));

    /// Maps each prefix position to its pen advance plus one outline width, relative to the line's
    /// pen origin. Prefixes are measured with the sentinel appended, so subtracting the sentinel's
    /// own ink right edge leaves exactly the pen position where the sentinel was placed. A null
    /// sentinel falls back to the bare ink right edge, which drifts by the last glyph's overhang.
    /// The X1 values are already compensated for the measurement shift (or came from an unshifted
    /// retry), so they are in the same coordinate space as the line's pen origin.
    internal static Dictionary<int, float> BuildAdvances(
        IReadOnlyDictionary<int, float?> prefixX1s,
        float? sentinelInkX1, float border)
    {
        var advances = new Dictionary<int, float> { [0] = border };
        foreach (var (pos, x1) in prefixX1s)
        {
            if (x1 is null)
                advances[pos] = border;
            else if (sentinelInkX1 is { } sx)
                advances[pos] = (float)x1 - sx + border;
            else
                advances[pos] = (float)x1 - MeasureOrigin;
        }
        return advances;
    }

    private static List<(string Text, int StartIdx)> SplitLines(string text)
    {
        var lines = new List<(string, int)>();
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == '\n')
            {
                lines.Add((text[start..i], start));
                start = i + 1;
            }
        }
        return lines;
    }
}
