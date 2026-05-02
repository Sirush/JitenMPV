namespace JitenMPV.Core.Theming;

public sealed class WordStyleState
{
    public string? TextColor { get; init; }
    public string? OutlineColor { get; init; }
    public string? TextColorBgr => TextColor is not null ? RgbToBgr(TextColor) : null;
    public string? OutlineColorBgr => OutlineColor is not null ? RgbToBgr(OutlineColor) : null;
    public double? OutlineSize { get; init; }
    public int? TextOpacity { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }

    private static string RgbToBgr(string hexRgb)
    {
        var hex = hexRgb.TrimStart('#');
        if (hex.Length != 6) return hex;
        return string.Concat(hex.AsSpan(4, 2), hex.AsSpan(2, 2), hex.AsSpan(0, 2));
    }
}