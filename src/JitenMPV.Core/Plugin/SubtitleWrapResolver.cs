using System.Text;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Rendering;

namespace JitenMPV.Core.Plugin;

/// Whether the caller may suppress libass wrapping for the text this resolve produced.
public enum WrapOutcome
{
    /// The text fits as it stands; no break was inserted.
    NotNeeded,

    /// Breaks were inserted and every segment fits, so the drawn text can own its own wrapping.
    Resolved,

    /// The probes went unanswered. libass has to keep wrapping this text, or it runs off screen.
    Failed
}

/// <param name="Breaks">Insertion points in the source text, ascending. Each adds one character.</param>
public sealed record WrapResult(WrapOutcome Outcome, string Text, IReadOnlyList<int> Breaks);

/// libass auto-wraps a line that does not fit, but SubtitleMeasurer can only place hitboxes on the
/// explicit lines it splits on. This turns libass's invisible breaks into real \n's: every logical
/// line is measured with the display's own tags, the measured block height says how many visual
/// lines libass would draw, and each break is found by measurement. The overlay is then rendered
/// with \q2 so the drawn text is exactly what was measured. <see cref="Rebase"/> moves a parse onto
/// the returned text, keeping tokens whole across a break so a wrapped word stays one word with one
/// colour and one hitbox rect per line it occupies.
public sealed class SubtitleWrapResolver(PluginSettings settings, OsdState osd)
{
    /// One id reused across probes: every probe is awaited before the next is sent, and mpv answers
    /// each osd-overlay command with the bounds of the data that command carried.
    private const int MeasureId = 60;

    private const string SentinelGlyph = "国";
    private const float MeasureOrigin = 64f;

    private volatile PluginSettings _settings = settings;
    private readonly BoundedCache<string, IReadOnlyList<int>> _cache = new(2000);
    private int _lastOsdVersion = -1;
    private LineMetrics? _metrics;

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
            _metrics = null;
        }
    }

    /// Resolves where the text would wrap without needing a parse: a plain-rendered subtitle (no
    /// Japanese in the line, or a parse that failed) is drawn with the same suppressed wrapping and
    /// would otherwise run off screen unbroken.
    public async Task<WrapResult> ResolveAsync(string text, MpvIpcClient ipc, CancellationToken ct)
    {
        if (osd.Version != _lastOsdVersion)
        {
            _cache.Clear();
            _metrics = null;
            _lastOsdVersion = osd.Version;
        }

        if (string.IsNullOrEmpty(text)) return new WrapResult(WrapOutcome.NotNeeded, text, []);

        if (_cache.TryGetValue(text, out var cached) && cached is not null)
            return Materialize(text, cached);

        var lines = SplitLines(text);
        var breaks = new List<int>();
        int offset = 0;
        bool probed = false;
        foreach (var line in lines)
        {
            var localBreaks = await WrapLineAsync(line, ipc, ct);
            if (localBreaks is null)
            {
                await ipc.RemoveOverlayAsync(MeasureId, ct);
                return new WrapResult(WrapOutcome.Failed, text, []);
            }

            probed = true;
            foreach (int at in localBreaks) breaks.Add(offset + at);
            offset += line.Length + 1;
        }

        if (probed) await ipc.RemoveOverlayAsync(MeasureId, ct);

        _cache.TryAdd(text, breaks);
        return Materialize(text, breaks);
    }

    private static WrapResult Materialize(string text, IReadOnlyList<int> breaks)
    {
        if (breaks.Count == 0) return new WrapResult(WrapOutcome.NotNeeded, text, breaks);

        // \n here is what AssTagBuilder later escapes to \N, so the drawn overlay carries the same
        // breaks these positions describe.
        var sb = new StringBuilder(text.Length + breaks.Count);
        int prev = 0;
        foreach (int at in breaks)
        {
            sb.Append(text, prev, at - prev).Append('\n');
            prev = at;
        }
        sb.Append(text, prev, text.Length - prev);
        return new WrapResult(WrapOutcome.Resolved, sb.ToString(), breaks);
    }

    /// Break positions within one logical line, or null when a probe went unanswered.
    private async Task<List<int>?> WrapLineAsync(string line, MpvIpcClient ipc, CancellationToken ct)
    {
        if (line.Length == 0) return [];

        // The display's own tags without \q2: wrapping has to happen here exactly like it happens
        // on screen, whose wrap decisions depend on the same style, position and play-res width.
        var fullBounds = await ipc.MeasureOverlayAsync(MeasureId, DisplayAss(line), ct);

        // Nothing was drawn, so there is nothing to wrap; a whitespace-only line reads this way too.
        if (fullBounds is null) return [];

        if (await EnsureMetricsAsync(ipc, ct) is not { } metrics) return null;

        int visualLines = metrics.CountLines(fullBounds.Height);
        if (visualLines <= 1) return [];

        var breaks = new List<int>(visualLines - 1);
        int prevBreak = 0;
        int searchFrom = 1;
        for (int target = 2; target <= visualLines && searchFrom <= line.Length; target++)
        {
            if (await FindOverflowAsync(line, target, searchFrom, ipc, ct, metrics) is not { } overflow)
                break;

            int at = KinsokuRules.SnapToBreakOpportunity(line, overflow - 1, prevBreak);
            if (at <= prevBreak) break;

            breaks.Add(at);
            prevBreak = at;
            searchFrom = overflow + 1;
        }

        return breaks;
    }

    /// Length of the shortest prefix that no longer fits on <paramref name="target"/> minus one
    /// lines, or null when the line never reaches that many. One character below it is the last
    /// that still fits, which is where the line has to break.
    private async Task<int?> FindOverflowAsync(
        string line, int target, int lo, MpvIpcClient ipc, CancellationToken ct, LineMetrics metrics)
    {
        if (lo > line.Length) return null;

        int hi = line.Length;
        int? found = null;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (await VisualLinesAsync(line[..mid], ipc, ct, metrics) >= target)
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

    /// Number of visual lines the prefix occupies when wrapped with the display's own tags.
    private async Task<int> VisualLinesAsync(
        string prefix, MpvIpcClient ipc, CancellationToken ct, LineMetrics metrics)
    {
        var bounds = await ipc.MeasureOverlayAsync(MeasureId, DisplayAss(prefix), ct);
        return bounds is { Height: > 0 } ? metrics.CountLines(bounds.Height) : 1;
    }

    /// <param name="SingleLine">Height of a one-line block. A block's box is its ink plus its
    /// border, which already runs well over a line pitch tall, so it has to be subtracted before
    /// the remainder can be divided into lines. Dividing the raw height instead reads a single
    /// line as two as soon as that overhang passes half a pitch, which is what the render scale
    /// of a small window does to it.</param>
    private readonly record struct LineMetrics(double Pitch, double SingleLine)
    {
        public int CountLines(double height)
            => Math.Max(1, 1 + (int)Math.Round((height - SingleLine) / Pitch));
    }

    private string DisplayAss(string text)
    {
        var s = _settings;
        float resX = OverlayRenderer.ComputeResX(osd.Width, osd.Height);
        int align = OverlayRenderer.ClampAlign(s.SubtitleAlignment);
        var posTags = OverlayRenderer.BuildPositionTags(resX, s, align);
        return $@"{{\an{align}{posTags}{OverlayRenderer.BuildStyleTags(s)}\shad0\blur0}}{AssTagBuilder.EscapeText(text)}";
    }

    /// Distance from one line's top to the next, read from two stacked glyphs of the subtitle's
    /// own font. Measured under \q2 so the pair cannot wrap past each other.
    private async Task<LineMetrics?> EnsureMetricsAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        if (_metrics is { } cached) return cached;

        var styleTags = OverlayRenderer.BuildStyleTags(_settings);
        string probeTags = $@"{{\an7\pos({MeasureOrigin:F0},{MeasureOrigin:F0})\q2{styleTags}\shad0\blur0}}";
        var oneLine = await ipc.MeasureOverlayAsync(MeasureId, $"{probeTags}{SentinelGlyph}", ct);
        var twoLines = await ipc.MeasureOverlayAsync(
            MeasureId, $@"{probeTags}{SentinelGlyph}\N{SentinelGlyph}", ct);

        if (oneLine is not { } one || twoLines is not { } two || two.Height - one.Height <= 1)
            return null;

        _metrics = new LineMetrics(two.Height - one.Height, one.Height);
        return _metrics;
    }

    /// Copies the entry's tokens onto the wrapped text: each inserted break shifts every later
    /// character by one, so a token's start moves by the number of breaks at or before it. Tokens
    /// are not split here - a word straddling a break keeps its whole range, which is what gives
    /// it one colour and one hitbox per line segment while still pointing at a single word.
    public static ParseCacheEntry Rebase(ParseCacheEntry entry, IReadOnlyList<int> breaks)
    {
        if (breaks.Count == 0) return entry;

        var shifted = new List<ReaderToken>(entry.Tokens.Count);
        int bi = 0;
        foreach (var token in entry.Tokens)
        {
            while (bi < breaks.Count && breaks[bi] <= token.Start) bi++;

            // A break inside the token's span adds one character (the inserted \n) to the word in
            // the wrapped text, so Length and End have to grow by that count or the trailing glyphs
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
