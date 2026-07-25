using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Plugin;

public sealed class SubtitleMeasurer(PluginSettings settings, OsdState osd)
{
    private const int MeasureId = 99;
    private volatile PluginSettings _settings = settings;
    private readonly BoundedCache<string, List<WordRect>> _cache = new(2000);
    private int _lastOsdVersion = -1;

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
        }
    }

    public async Task<List<WordRect>> MeasureAsync(
        string text, ParseCacheEntry entry,
        MpvIpcClient ipc, CancellationToken ct)
    {
        if (osd.Version != _lastOsdVersion)
        {
            _cache.Clear();
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
        int maxOverlayId = MeasureId;

        int align = OverlayRenderer.ClampAlign(s.SubtitleAlignment);
        var fullAss = $@"{{\an{align}{posTags}{styleTags}}}{AssTagBuilder.EscapeText(text)}";
        var fullBounds = await ipc.MeasureOverlayAsync(MeasureId, fullAss, ct);
        if (fullBounds is null) return rects;

        float lineHeight = (float)(fullBounds.Height / lines.Count);

        for (int li = 0; li < lines.Count; li++)
        {
            var (lineText, lineStartIdx) = lines[li];
            if (lineText.Length == 0) continue;

            var lineTokens = entry.Tokens
                .Select((t, i) => (Index: i, Token: t))
                .Where(x => x.Token.Start >= lineStartIdx
                         && x.Token.Start + x.Token.Length <= lineStartIdx + lineText.Length)
                .ToList();

            if (lineTokens.Count == 0) continue;

            var escapedLine = AssTagBuilder.EscapeText(lineText);
            var lineAss = $@"{{\an7\pos(0,0){styleTags}}}{escapedLine}";
            var lineCenteredAss = $@"{{\an{align}{posTags}{styleTags}}}{escapedLine}";

            var lineBoundsTask = ipc.MeasureOverlayAsync(MeasureId, lineAss, ct);
            var lineCenteredTask = ipc.MeasureOverlayAsync(MeasureId + 1, lineCenteredAss, ct);
            maxOverlayId = Math.Max(maxOverlayId, MeasureId + 1);
            await Task.WhenAll(lineBoundsTask, lineCenteredTask);

            var lineBounds = await lineBoundsTask;
            var lineCentered = await lineCenteredTask;
            if (lineBounds is null || lineBounds.X1 <= 0 || lineCentered is null) continue;

            float totalX1 = (float)lineBounds.X1;
            float visibleLeft = (float)lineCentered.X0;
            float visibleWidth = (float)lineCentered.Width;
            float lineY = (float)(fullBounds.Y0 + li * lineHeight);

            var positions = new SortedSet<int>();
            foreach (var (_, token) in lineTokens)
            {
                positions.Add(token.Start - lineStartIdx);
                positions.Add(token.Start - lineStartIdx + token.Length);
            }

            var prefixPositions = positions.Where(p => p > 0 && p <= lineText.Length).ToList();
            var prefixTasks = new Dictionary<int, Task<OverlayBounds?>>();
            int measureIdOffset = 2;
            foreach (var pos in prefixPositions)
            {
                var prefixAss = $@"{{\an7\pos(0,0){styleTags}}}{AssTagBuilder.EscapeText(lineText[..pos])}";
                int overlayId = MeasureId + measureIdOffset;
                prefixTasks[pos] = ipc.MeasureOverlayAsync(overlayId, prefixAss, ct);
                maxOverlayId = Math.Max(maxOverlayId, overlayId);
                measureIdOffset++;
            }

            await Task.WhenAll(prefixTasks.Values);

            var advances = new Dictionary<int, float> { [0] = 0 };
            foreach (var pos in prefixPositions)
            {
                var bounds = await prefixTasks[pos];
                advances[pos] = bounds is not null ? (float)(bounds.X1 / totalX1) * visibleWidth : 0;
            }

            foreach (var (idx, token) in lineTokens)
            {
                int localStart = token.Start - lineStartIdx;
                int localEnd = localStart + token.Length;

                float x0 = visibleLeft + advances.GetValueOrDefault(localStart);
                float x1 = visibleLeft + advances.GetValueOrDefault(localEnd);

                rects.Add(new WordRect(idx, token.WordId, token.ReadingIndex,
                    x0, lineY, Math.Max(x1 - x0, 1), lineHeight));
            }
        }

        await Task.WhenAll(
            Enumerable.Range(MeasureId, maxOverlayId - MeasureId + 1)
                .Select(id => ipc.RemoveOverlayAsync(id, ct)));
        return rects;
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
