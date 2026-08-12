namespace JitenMPV.Core.Theming;

public sealed class WordStyleState
{
    public string? TextColor { get; init; }
    public string? OutlineColor { get; init; }
    public string? ShadowColor { get; init; }

    private string? _textColorBgr;
    private string? _outlineColorBgr;
    private string? _shadowColorBgr;
    private bool _bgrComputed;

    public string? TextColorBgr { get { EnsureBgr(); return _textColorBgr; } }
    public string? OutlineColorBgr { get { EnsureBgr(); return _outlineColorBgr; } }
    public string? ShadowColorBgr { get { EnsureBgr(); return _shadowColorBgr; } }

    private void EnsureBgr()
    {
        if (_bgrComputed) return;
        _textColorBgr = TextColor is not null ? RgbToBgr(TextColor) : null;
        _outlineColorBgr = OutlineColor is not null ? RgbToBgr(OutlineColor) : null;
        _shadowColorBgr = ShadowColor is not null ? RgbToBgr(ShadowColor) : null;
        _bgrComputed = true;
    }

    public double? OutlineSize { get; init; }
    public double? ShadowDepth { get; init; }
    public int? TextOpacity { get; init; }
    public int? OutlineOpacity { get; init; }
    public int? ShadowOpacity { get; init; }
    public double? Blur { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }

    /// Set to draw the underline as a bar on the underline overlay instead of as ASS `\u`, which
    /// can only paint in the primary colour. Null keeps the underline the colour of the text.
    public string? UnderlineColor { get; init; }

    /// Bar height in overlay units; ignored unless UnderlineColor is set.
    public double? UnderlineThickness { get; init; }

    public bool? Strikethrough { get; init; }
    public int? ScaleX { get; init; }
    public int? ScaleY { get; init; }

    public WordStyleState MergeOver(WordStyleState baseStyle) => new()
    {
        TextColor = TextColor ?? baseStyle.TextColor,
        OutlineColor = OutlineColor ?? baseStyle.OutlineColor,
        ShadowColor = ShadowColor ?? baseStyle.ShadowColor,
        OutlineSize = OutlineSize ?? baseStyle.OutlineSize,
        ShadowDepth = ShadowDepth ?? baseStyle.ShadowDepth,
        TextOpacity = TextOpacity ?? baseStyle.TextOpacity,
        OutlineOpacity = OutlineOpacity ?? baseStyle.OutlineOpacity,
        ShadowOpacity = ShadowOpacity ?? baseStyle.ShadowOpacity,
        Blur = Blur ?? baseStyle.Blur,
        Bold = Bold ?? baseStyle.Bold,
        Italic = Italic ?? baseStyle.Italic,
        Underline = Underline ?? baseStyle.Underline,
        UnderlineColor = UnderlineColor ?? baseStyle.UnderlineColor,
        UnderlineThickness = UnderlineThickness ?? baseStyle.UnderlineThickness,
        Strikethrough = Strikethrough ?? baseStyle.Strikethrough,
        ScaleX = ScaleX ?? baseStyle.ScaleX,
        ScaleY = ScaleY ?? baseStyle.ScaleY
    };

    internal static string NormalizeHex(string hexRgb)
    {
        var hex = hexRgb.TrimStart('#');
        return hex.Length switch
        {
            3 => $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}",
            8 => hex[..6],
            6 => hex,
            _ => "ffffff"
        };
    }

    public static string ToAssBgr(string hexRgb) => RgbToBgr(hexRgb);

    internal static string RgbToBgr(string hexRgb)
    {
        var hex = NormalizeHex(hexRgb);
        return string.Concat(hex.AsSpan(4, 2), hex.AsSpan(2, 2), hex.AsSpan(0, 2));
    }
}
