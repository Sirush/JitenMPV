using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using JitenMPV.Core.Rendering;
using JitenMPV.Core.Theming;
using SkiaSharp;

namespace JitenMPV.App.Overlay;

/// Draws the laid-out subtitle through the renderer's own Skia canvas. Never interactive: mpv has
/// to keep receiving every pointer event this window sits on top of.
internal sealed class SubtitleOverlayControl : Control
{
    private readonly SubtitlePainter _painter;
    private LaidOutSubtitle? _frame;

    public SubtitleOverlayControl(SubtitleFontStore fonts)
    {
        _painter = new SubtitlePainter(fonts);
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// True once a render pass found no Skia lease, which leaves the surface unable to draw at all.
    public bool LeaseUnavailable => _painter.LeaseUnavailable;

    public void SetFrame(LaidOutSubtitle? frame)
    {
        _frame = frame;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_frame is not { } frame) return;
        context.Custom(new SubtitleDrawOperation(new Rect(Bounds.Size), frame, _painter));
    }

    public void DisposePainter() => _painter.Dispose();
}

internal sealed class SubtitleDrawOperation(
    Rect bounds, LaidOutSubtitle frame, SubtitlePainter painter) : ICustomDrawOperation
{
    public Rect Bounds => bounds;

    public bool HitTest(Point p) => false;

    public bool Equals(ICustomDrawOperation? other) => false;

    public void Render(ImmediateDrawingContext context) => painter.Paint(context, frame);

    public void Dispose()
    {
    }
}

/// Paints one frame in overlay units: shadows, then outlines, then fills, the way libass layers
/// them, so adjacent words of different colours never cut into each other's borders.
internal sealed class SubtitlePainter(SubtitleFontStore fonts) : IDisposable
{
    private const byte DebugFillAlpha = 0x4B;
    private const byte DebugEdgeAlpha = 0xCF;
    private const float DebugEdgeWidth = 1.5f;

    /// Successive hues a golden angle apart, so words next to each other never land on near colours.
    private const double DebugHueStep = 137.508;

    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly SKRect[] _decorations = new SKRect[2];
    private bool _disposed;

    public bool LeaseUnavailable { get; private set; }

    public void Paint(ImmediateDrawingContext context, LaidOutSubtitle frame)
    {
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
            is not ISkiaSharpApiLeaseFeature leaseFeature)
        {
            LeaseUnavailable = true;
            return;
        }

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;

        lock (fonts.Gate)
        {
            // A render pass queued before teardown can still reach this after the paints are gone.
            if (_disposed) return;

            int saved = canvas.Save();
            canvas.Scale((float)frame.Scale);

            foreach (var piece in frame.Pieces) PaintShadow(canvas, piece);
            foreach (var piece in frame.Pieces) PaintOutline(canvas, piece, frame.DefaultOutline);
            foreach (var piece in frame.Pieces) PaintFill(canvas, piece);

            foreach (var bar in frame.Bars)
            {
                Reset(SKPaintStyle.Fill, Color(bar.ColorRgb, 255), null);
                canvas.DrawRect(bar.X, bar.Y, bar.Width, bar.Height, _paint);
            }

            if (frame.ShowDebugHitboxes) PaintDebugHitboxes(canvas, frame.Rects);

            canvas.RestoreToCount(saved);
        }
    }

    private void PaintShadow(SKCanvas canvas, SubtitleDrawPiece piece)
    {
        if (piece.Style.ShadowDepth is not { } depth || depth <= 0) return;

        float offset = (float)depth;
        Reset(SKPaintStyle.Fill,
            Color(piece.Style.ShadowColor ?? "#000000", piece.Style.ShadowOpacity ?? 255),
            fonts.Blur(Sigma(piece.Style)));
        canvas.DrawText(piece.Text, piece.X + offset, piece.Baseline + offset, piece.Font, _paint);

        int count = Decorations(piece);
        for (int i = 0; i < count; i++)
        {
            var rect = _decorations[i];
            rect.Offset(offset, offset);
            canvas.DrawRect(rect, _paint);
        }
    }

    private void PaintOutline(SKCanvas canvas, SubtitleDrawPiece piece, double defaultOutline)
    {
        double outline = piece.Style.OutlineSize ?? defaultOutline;
        if (outline <= 0) return;

        // A centred stroke puts half its width inside the glyph, so the visible border is the ASS
        // \bord value only when the stroke is drawn at twice it.
        _paint.Reset();
        _paint.IsAntialias = true;
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = (float)outline * 2f;
        _paint.StrokeJoin = SKStrokeJoin.Round;
        _paint.Color = Color(piece.Style.OutlineColor ?? "#000000", piece.Style.OutlineOpacity ?? 255);
        _paint.MaskFilter = fonts.Blur(Sigma(piece.Style));
        canvas.DrawText(piece.Text, piece.X, piece.Baseline, piece.Font, _paint);

        int count = Decorations(piece);
        for (int i = 0; i < count; i++) canvas.DrawRect(_decorations[i], _paint);
    }

    private void PaintFill(SKCanvas canvas, SubtitleDrawPiece piece)
    {
        var color = Color(piece.Style.TextColor ?? "#ffffff", piece.Style.TextOpacity ?? 255);
        Reset(SKPaintStyle.Fill, color, fonts.Blur(Sigma(piece.Style)));
        canvas.DrawText(piece.Text, piece.X, piece.Baseline, piece.Font, _paint);

        int count = Decorations(piece);
        for (int i = 0; i < count; i++) canvas.DrawRect(_decorations[i], _paint);
    }

    /// Fills <see cref="_decorations"/> with the piece's underline and strikethrough and returns how
    /// many it wrote. libass carries both inside the glyph bitmap, so every pass has to draw them or
    /// an underlined word loses the border and shadow the rest of its text keeps.
    private int Decorations(SubtitleDrawPiece piece)
    {
        var metrics = piece.Font.Metrics;
        int count = 0;

        // A coloured underline is drawn as a bar of its own; adding this one would stack a second
        // line under it in the text colour.
        if (piece.Style.Underline == true && piece.Style.UnderlineColor is null)
        {
            float thickness = metrics.UnderlineThickness ?? piece.Font.Size / 18f;
            float position = metrics.UnderlinePosition ?? metrics.Descent * 0.5f;
            _decorations[count++] = SKRect.Create(
                piece.X, piece.Baseline + position, piece.Width, thickness);
        }

        if (piece.Style.Strikethrough == true)
        {
            float thickness = metrics.StrikeoutThickness ?? piece.Font.Size / 18f;
            float position = metrics.StrikeoutPosition ?? metrics.Ascent * 0.4f;
            _decorations[count++] = SKRect.Create(
                piece.X, piece.Baseline + position, piece.Width, thickness);
        }

        return count;
    }

    private void PaintDebugHitboxes(SKCanvas canvas, IReadOnlyList<WordRect> rects)
    {
        for (int i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            var hue = HueToRgb(i * DebugHueStep % 360.0);
            var (x0, y0, x1, y1) = rect.HitRegion;

            Reset(SKPaintStyle.Fill, hue.WithAlpha(DebugFillAlpha), null);
            canvas.DrawRect(x0, y0, x1 - x0, y1 - y0, _paint);

            _paint.Reset();
            _paint.IsAntialias = true;
            _paint.Style = SKPaintStyle.Stroke;
            _paint.StrokeWidth = DebugEdgeWidth;
            _paint.Color = hue.WithAlpha(DebugEdgeAlpha);
            canvas.DrawRect(x0, y0, x1 - x0, y1 - y0, _paint);
            canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, _paint);
        }
    }

    private void Reset(SKPaintStyle style, SKColor color, SKMaskFilter? blur)
    {
        _paint.Reset();
        _paint.IsAntialias = true;
        _paint.Style = style;
        _paint.Color = color;
        _paint.MaskFilter = blur;
    }

    /// libass blurs in play-res units and so does this, so the ASS blur value is the gaussian sigma.
    private static double Sigma(WordStyleState style)
        => style.Blur is { } blur && blur > 0 ? blur : 0;

    /// ASS stores opacity where 255 is opaque, which is already what Skia's alpha channel means.
    private static SKColor Color(string hexRgb, int opacity)
    {
        var bgr = WordStyleState.ToAssBgr(hexRgb);
        byte b = byte.Parse(bgr.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(bgr.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte r = byte.Parse(bgr.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SKColor(r, g, b, (byte)Math.Clamp(opacity, 0, 255));
    }

    private static SKColor HueToRgb(double hue)
    {
        const double saturation = 0.9;
        double sector = hue / 60.0;
        double secondary = 1 - saturation * Math.Abs(sector % 2 - 1);
        double lowest = 1 - saturation;

        var (r, g, b) = (int)sector switch
        {
            0 => (1.0, secondary, lowest),
            1 => (secondary, 1.0, lowest),
            2 => (lowest, 1.0, secondary),
            3 => (lowest, secondary, 1.0),
            4 => (secondary, lowest, 1.0),
            _ => (1.0, lowest, secondary)
        };

        return new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    public void Dispose()
    {
        lock (fonts.Gate)
        {
            _disposed = true;
            _paint.Dispose();
        }
    }
}
