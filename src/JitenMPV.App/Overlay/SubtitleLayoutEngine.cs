using System;
using System.Collections.Generic;
using System.Text;
using JitenMPV.Core.Config;
using JitenMPV.Core.Plugin;
using JitenMPV.Core.Rendering;
using JitenMPV.Core.Theming;
using SkiaSharp;

namespace JitenMPV.App.Overlay;

/// One stretch of a visual line drawn with a single font and a single resolved style.
internal sealed record SubtitleDrawPiece(
    string Text, float X, float Baseline, float Width, SKFont Font, WordStyleState Style);

/// A coloured bar under a word, already placed in overlay units.
internal readonly record struct SubtitleBar(
    float X, float Y, float Width, float Height, string ColorRgb);

/// <param name="Scale">Overlay units to the drawing context's own units.</param>
internal sealed record LaidOutSubtitle(
    double Scale,
    IReadOnlyList<SubtitleDrawPiece> Pieces,
    IReadOnlyList<SubtitleBar> Bars,
    List<WordRect> Rects,
    double DefaultOutline,
    bool ShowDebugHitboxes);

/// Turns a frame into positioned glyph runs and 720-space hit rectangles, with no help from mpv:
/// joining, wrapping and measurement all happen against the font this process will draw with.
internal sealed class SubtitleLayoutEngine(SubtitleFontStore fonts)
{
    /// Clearance between the glyphs and the first coloured bar, as a share of the line pitch so it
    /// holds at any font size.
    private const float BarTextGapRatio = 0.05f;
    private const float BarGap = 1f;

    /// Metrics probe: a full-width glyph every Japanese font covers.
    private const int MetricsCodePoint = '国';

    public LaidOutSubtitle? Layout(
        SubtitleFrame frame, PluginSettings settings,
        int osdWidth, int osdHeight, double logicalHeight)
    {
        if (osdWidth <= 0 || osdHeight <= 0 || logicalHeight <= 0) return null;
        if (string.IsNullOrEmpty(frame.Text)) return null;

        float resX = OverlayRenderer.ComputeResX(osdWidth, osdHeight);
        int align = OverlayRenderer.ClampAlign(settings.SubtitleAlignment);
        float available = Math.Max(SubtitleLineJoiner.AvailableWidth(settings, resX), 1f);

        lock (fonts.Gate)
        {
            fonts.Configure(settings.FontFamily, settings.FontSize);

            var model = BuildModel(frame, settings, available);
            var visualLines = WrapLines(model, available);
            if (visualLines.Count == 0) return null;

            var baseFont = fonts.Resolve(MetricsCodePoint, false, false, 100, 100, out _);
            var metrics = baseFont.Metrics;
            float pitch = baseFont.Spacing;
            if (pitch <= 0) pitch = settings.FontSize;
            float ascent = -metrics.Ascent;

            var barSpace = BarSpacing(frame, model, visualLines, metrics, pitch);
            float blockHeight = pitch * visualLines.Count;
            foreach (var space in barSpace) blockHeight += space;
            float posY = align switch
            {
                >= 7 => settings.SubtitleMarginY,
                >= 4 => OverlayRenderer.ResY / 2f,
                _ => OverlayRenderer.ResY - settings.SubtitleMarginY
            };
            float blockTop = align switch
            {
                >= 7 => posY,
                >= 4 => posY - blockHeight / 2f,
                _ => posY - blockHeight
            };

            var pieces = new List<SubtitleDrawPiece>();
            var rects = new List<WordRect>();

            float lineTop = blockTop;
            for (int li = 0; li < visualLines.Count; li++)
            {
                var (start, end) = visualLines[li];
                end = TrimTrailingSpace(model, start, end);
                float baseline = lineTop + ascent;
                float lineWidth = Width(model, start, end);
                float penX = (align % 3) switch
                {
                    1 => settings.SubtitleMarginX,
                    0 => resX - settings.SubtitleMarginX - lineWidth,
                    _ => (resX - lineWidth) / 2f
                };

                AppendLine(model, start, end, penX, baseline, lineTop, pitch, pieces, rects);
                lineTop += pitch + barSpace[li];
            }

            var placed = WordRect.AssignHitRegions(rects);
            var bars = BuildBars(frame, placed, metrics, pitch);

            return new LaidOutSubtitle(
                logicalHeight / OverlayRenderer.ResY, pieces, bars, placed,
                settings.BorderSize, frame.ShowDebugHitboxes);
        }
    }

    /// Per-character glyphs and advances for the text as it will be drawn, which is the joined form
    /// whenever the file's own breaks were only there for readability and the joined line still fits.
    private sealed class CharModel
    {
        public required string Text { get; init; }
        public required SubtitleRun?[] Runs { get; init; }
        public required SKFont?[] Fonts { get; init; }
        public required float[] Advances { get; init; }
    }

    private CharModel BuildModel(SubtitleFrame frame, PluginSettings settings, float available)
    {
        var sourceRuns = MapRuns(frame);

        if (settings.SubtitleSingleLine && frame.Text.Contains('\n'))
        {
            var (joined, map) = JoinLines(frame.Text);
            if (joined.Length > 0 && !string.Equals(joined, frame.Text, StringComparison.Ordinal))
            {
                var runs = new SubtitleRun?[joined.Length];
                for (int i = 0; i < joined.Length; i++)
                    runs[i] = map[i] >= 0 ? sourceRuns[map[i]] : null;

                var candidate = Measure(joined, runs);
                if (Width(candidate, 0, joined.Length) <= available)
                    return candidate;
            }
        }

        return Measure(frame.Text, sourceRuns);
    }

    private static SubtitleRun?[] MapRuns(SubtitleFrame frame)
    {
        var runs = new SubtitleRun?[frame.Text.Length];
        foreach (var run in frame.Runs)
        {
            int end = Math.Min(run.Start + run.Length, frame.Text.Length);
            for (int i = Math.Max(run.Start, 0); i < end; i++) runs[i] = run;
        }
        return runs;
    }

    private CharModel Measure(string text, SubtitleRun?[] runs)
    {
        var glyphFonts = new SKFont?[text.Length];
        var advances = new float[text.Length];

        for (int i = 0; i < text.Length;)
        {
            bool pair = char.IsHighSurrogate(text[i])
                        && i + 1 < text.Length
                        && char.IsLowSurrogate(text[i + 1]);
            int codePoint = pair ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
            var style = runs[i]?.Style ?? ThemePresets.Unparsed;

            var font = fonts.Resolve(
                codePoint, style.Bold == true, style.Italic == true,
                style.ScaleX ?? 100, style.ScaleY ?? 100, out var glyph);

            glyphFonts[i] = font;
            advances[i] = MeasureGlyph(font, glyph);

            if (pair)
            {
                glyphFonts[i + 1] = null;
                advances[i + 1] = 0;
            }

            i += pair ? 2 : 1;
        }

        return new CharModel { Text = text, Runs = runs, Fonts = glyphFonts, Advances = advances };
    }

    private static float MeasureGlyph(SKFont font, ushort glyph)
    {
        Span<ushort> single = [glyph];
        return font.MeasureText(single, null);
    }

    /// Joins the file's own breaks the way SubtitleLineJoiner does, carrying an index back to the
    /// original text so the resolved styles survive the shift; -1 marks an inserted space.
    private static (string Text, int[] Source) JoinLines(string text)
    {
        var sb = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);

        int lineStart = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '\n') continue;

            int start = lineStart;
            int end = i;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            lineStart = i + 1;
            if (end <= start) continue;

            if (sb.Length > 0 && SubtitleLineJoiner.NeedsSpace(sb[^1], text[start]))
            {
                sb.Append(' ');
                map.Add(-1);
            }

            for (int k = start; k < end; k++)
            {
                sb.Append(text[k]);
                map.Add(k);
            }
        }

        return (sb.ToString(), map.ToArray());
    }

    private static List<(int Start, int End)> WrapLines(CharModel model, float available)
    {
        var lines = new List<(int, int)>();
        int lineStart = 0;

        for (int i = 0; i <= model.Text.Length; i++)
        {
            if (i != model.Text.Length && model.Text[i] != '\n') continue;
            WrapOne(model, lineStart, i, available, lines);
            lineStart = i + 1;
        }

        return lines;
    }

    private static void WrapOne(
        CharModel model, int lineStart, int lineEnd, float available, List<(int, int)> lines)
    {
        if (lineEnd <= lineStart)
        {
            lines.Add((lineStart, lineEnd));
            return;
        }

        int start = lineStart;
        while (start < lineEnd)
        {
            float width = 0;
            int i = start;
            while (i < lineEnd)
            {
                float advance = model.Advances[i];
                if (i > start && width + advance > available) break;
                width += advance;
                i++;
            }

            if (i >= lineEnd)
            {
                lines.Add((start, lineEnd));
                return;
            }

            int at = KinsokuRules.SnapToBreakOpportunity(model.Text, i, start);
            if (at <= start) at = i;

            // A surrogate pair is one glyph; a break between its halves would draw two replacements.
            if (char.IsLowSurrogate(model.Text[at])) at++;
            if (at <= start) at = Math.Min(start + 1, lineEnd);

            lines.Add((start, at));
            start = at;
        }
    }

    private static int TrimTrailingSpace(CharModel model, int start, int end)
    {
        while (end > start && char.IsWhiteSpace(model.Text[end - 1])) end--;
        return end;
    }

    private static float Width(CharModel model, int start, int end)
    {
        float width = 0;
        for (int i = start; i < end; i++) width += model.Advances[i];
        return width;
    }

    private static void AppendLine(
        CharModel model, int start, int end,
        float penX, float baseline, float lineTop, float pitch,
        List<SubtitleDrawPiece> pieces, List<WordRect> rects)
    {
        int i = start;
        while (i < end)
        {
            var run = model.Runs[i];
            var font = model.Fonts[i];
            int groupEnd = i + 1;
            while (groupEnd < end
                   && ReferenceEquals(model.Runs[groupEnd], run)
                   && (model.Fonts[groupEnd] is null || ReferenceEquals(model.Fonts[groupEnd], font)))
                groupEnd++;

            float width = Width(model, i, groupEnd);
            if (font is not null && width > 0)
            {
                pieces.Add(new SubtitleDrawPiece(
                    model.Text[i..groupEnd], penX, baseline, width, font,
                    run?.Style ?? ThemePresets.Unparsed));
            }

            if (run?.Token is { } token)
            {
                // One rect per line a token reaches across, so a wrapped word stays clickable at
                // both ends and still points at the single word behind it.
                var last = rects.Count > 0 ? rects[^1] : null;
                if (last is not null && last.TokenIndex == run.TokenIndex && last.Y == lineTop)
                    rects[^1] = last with { Width = penX + width - last.X };
                else
                    rects.Add(new WordRect(
                        run.TokenIndex, token.WordId, token.ReadingIndex,
                        penX, lineTop, Math.Max(width, 1f), pitch));
            }

            penX += width;
            i = groupEnd;
        }
    }

    /// Room to add below each line so its bars land in the gap instead of on the glyphs beneath
    /// them, which is only ever the part of the stack the font's own leading cannot already hold.
    /// The last line has nothing under it to collide with and so never claims any.
    private static float[] BarSpacing(
        SubtitleFrame frame, CharModel model, List<(int Start, int End)> lines,
        SKFontMetrics metrics, float pitch)
    {
        var extra = new float[lines.Count];
        if (frame.UnderlineBars is null) return extra;

        float barTop = -metrics.Ascent + metrics.Descent + pitch * BarTextGapRatio;

        for (int li = 0; li < lines.Count - 1; li++)
        {
            var (start, end) = lines[li];
            float tallest = 0;
            SubtitleRun? seen = null;

            for (int i = start; i < end; i++)
            {
                var run = model.Runs[i];
                if (run is null || ReferenceEquals(run, seen)) continue;
                seen = run;

                if (run.Token is not { } token
                    || !frame.UnderlineBars.TryGetValue(
                        (token.WordId, token.ReadingIndex), out var bars))
                    continue;

                float stack = 0;
                foreach (var bar in bars)
                    if (bar.Thickness > 0)
                        stack += (float)bar.Thickness + BarGap;

                if (stack > tallest) tallest = stack;
            }

            extra[li] = Math.Max(0, barTop + tallest - pitch);
        }

        return extra;
    }

    private static List<SubtitleBar> BuildBars(
        SubtitleFrame frame, List<WordRect> rects, SKFontMetrics metrics, float pitch)
    {
        var bars = new List<SubtitleBar>();
        if (frame.UnderlineBars is null) return bars;

        foreach (var rect in rects)
        {
            if (rect.Width <= 0) continue;
            if (!frame.UnderlineBars.TryGetValue((rect.WordId, rect.ReadingIndex), out var wordBars))
                continue;

            // Placed from the font's own descent rather than from the line slot, so a bar clears
            // the deepest glyph on the line without reaching into the line below.
            float top = rect.Y + (-metrics.Ascent) + metrics.Descent + pitch * BarTextGapRatio;
            foreach (var bar in wordBars)
            {
                if (bar.Thickness <= 0) continue;
                bars.Add(new SubtitleBar(
                    rect.X, top, rect.Width, (float)bar.Thickness, bar.Color));
                top += (float)bar.Thickness + BarGap;
            }
        }

        return bars;
    }
}
