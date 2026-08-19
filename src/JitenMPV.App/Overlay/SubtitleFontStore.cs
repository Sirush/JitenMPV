using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JitenMPV.App.Overlay;

/// Typefaces, sized fonts and blur filters for the native subtitle renderer, resolved once per
/// (style, code point) and kept until the subtitle font settings change.
internal sealed class SubtitleFontStore : IDisposable
{
    /// A code point cache this large only builds up on text nobody is reading; dropping it is
    /// cheaper than letting a long session hold a typeface per exotic glyph.
    private const int MaxFallbackEntries = 4096;

    /// Held by both the layout pass and the draw operation: an SKFont carries a glyph cache that
    /// cannot be measured from on one thread while the renderer draws with it on another.
    public object Gate { get; } = new();

    private readonly Dictionary<(bool Bold, bool Italic), SKTypeface> _primary = [];
    private readonly Dictionary<(bool Bold, bool Italic, int CodePoint), SKTypeface?> _fallback = [];
    private readonly Dictionary<(SKTypeface Typeface, bool Bold, bool Italic, int ScaleX, int ScaleY), SKFont> _fonts = [];
    private readonly Dictionary<SKTypeface, float> _emScales = [];
    private readonly Dictionary<int, SKMaskFilter> _blurs = [];

    private string _family = "";
    private float _size;

    public void Configure(string family, float size)
    {
        if (string.Equals(_family, family, StringComparison.Ordinal)
            && Math.Abs(_size - size) < 0.001f)
            return;

        _family = family;
        _size = size <= 0 ? 1 : size;
        Invalidate();
    }

    public SKFont Resolve(
        int codePoint, bool bold, bool italic, int scaleX, int scaleY, out ushort glyph)
    {
        var primary = Primary(bold, italic);
        var font = FontFor(primary, bold, italic, scaleX, scaleY);
        glyph = font.GetGlyph(codePoint);
        if (glyph != 0) return font;

        if (Fallback(bold, italic, codePoint) is { } fallback)
        {
            var fallbackFont = FontFor(fallback, bold, italic, scaleX, scaleY);
            var fallbackGlyph = fallbackFont.GetGlyph(codePoint);
            if (fallbackGlyph != 0)
            {
                glyph = fallbackGlyph;
                return fallbackFont;
            }
        }

        return font;
    }

    public SKMaskFilter? Blur(double sigma)
    {
        if (sigma <= 0) return null;

        int key = (int)Math.Round(Math.Clamp(sigma, 0.05, 64) * 16);
        if (_blurs.TryGetValue(key, out var cached)) return cached;

        var filter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, key / 16f);
        _blurs[key] = filter;
        return filter;
    }

    private SKTypeface Primary(bool bold, bool italic)
    {
        if (_primary.TryGetValue((bold, italic), out var cached)) return cached;

        SKTypeface? face = null;
        try
        {
            face = SKTypeface.FromFamilyName(
                _family, Weight(bold), SKFontStyleWidth.Normal, Slant(italic));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        face ??= SKTypeface.Default;
        _primary[(bold, italic)] = face;
        return face;
    }

    private SKTypeface? Fallback(bool bold, bool italic, int codePoint)
    {
        var key = (bold, italic, codePoint);
        if (_fallback.TryGetValue(key, out var cached)) return cached;

        if (_fallback.Count >= MaxFallbackEntries) _fallback.Clear();

        SKTypeface? face = null;
        try
        {
            face = SKFontManager.Default.MatchCharacter(
                _family, Weight(bold), SKFontStyleWidth.Normal, Slant(italic), null, codePoint);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        _fallback[key] = face;
        return face;
    }

    private SKFont FontFor(SKTypeface typeface, bool bold, bool italic, int scaleX, int scaleY)
    {
        int sxPercent = Math.Clamp(scaleX, 1, 1000);
        int syPercent = Math.Clamp(scaleY, 1, 1000);
        var key = (typeface, bold, italic, sxPercent, syPercent);
        if (_fonts.TryGetValue(key, out var cached)) return cached;

        float sy = syPercent / 100f;
        var font = new SKFont(typeface, _size * EmScale(typeface) * sy)
        {
            // The size already carries the vertical scale, so the horizontal one is what is left.
            ScaleX = sxPercent / (float)syPercent,
            Subpixel = true,
            LinearMetrics = true,
            Hinting = SKFontHinting.None,
            Edging = SKFontEdging.Antialias,
            Embolden = bold && typeface.FontWeight < (int)SKFontStyleWeight.SemiBold,
            SkewX = italic && typeface.FontSlant == SKFontStyleSlant.Upright ? -0.25f : 0f
        };

        _fonts[key] = font;
        return font;
    }

    /// An ASS font size is the face's full vertical extent, not its em: libass asks FreeType for a
    /// real-dimension size scaled by hhea/OS-2, which works out to the em being this much smaller.
    /// Without it a CJK face renders about 45% too large for the size the user set.
    private float EmScale(SKTypeface typeface)
    {
        if (_emScales.TryGetValue(typeface, out var cached)) return cached;

        float scale = 1f;
        try
        {
            int unitsPerEm = typeface.UnitsPerEm;
            if (unitsPerEm > 0 && Extent(typeface) is { } extent && extent > 0)
                scale = unitsPerEm / (float)extent;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        _emScales[typeface] = scale;
        return scale;
    }

    private static int? Extent(SKTypeface typeface)
    {
        const uint os2Tag = 0x4F532F32;
        const uint hheaTag = 0x68686561;

        if (typeface.TryGetTableData(os2Tag, out var os2) && os2.Length >= 78)
            return ReadUInt16(os2, 74) + ReadUInt16(os2, 76);

        if (typeface.TryGetTableData(hheaTag, out var hhea) && hhea.Length >= 12)
            return ReadInt16(hhea, 4) - ReadInt16(hhea, 6);

        return null;
    }

    private static int ReadUInt16(byte[] table, int offset)
        => (table[offset] << 8) | table[offset + 1];

    private static int ReadInt16(byte[] table, int offset)
        => (short)((table[offset] << 8) | table[offset + 1]);

    private static SKFontStyleWeight Weight(bool bold)
        => bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;

    private static SKFontStyleSlant Slant(bool italic)
        => italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

    private void Invalidate()
    {
        // A draw op queued before the settings change can still paint the previous frame with
        // these fonts, so they are dropped for the finalizers rather than disposed under it.
        _fonts.Clear();
        _primary.Clear();
        _fallback.Clear();
        _emScales.Clear();
    }

    public void Dispose()
    {
        lock (Gate)
        {
            Invalidate();
            foreach (var blur in _blurs.Values) blur.Dispose();
            _blurs.Clear();
        }
    }
}
