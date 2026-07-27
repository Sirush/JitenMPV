using System.Text;

namespace JitenMPV.Core.Text;

/// <param name="Ruby">Empty for runs that carry no reading of their own, such as okurigana.</param>
public sealed record FuriganaSegment(string Text, string Ruby);

/// Splits the ruby notation the API returns as a word's reading — 逃[に]げる, 竜宮町[りゅうぐうちょう],
/// 食[た]べ物[もの] — into base/ruby pairs, matching what jiten.moe's convertToRuby renders.
public static class FuriganaParser
{
    /// Null when the notation carries no reading or its bases do not rebuild the spelling, which is
    /// the caller's signal to fall back to showing the reading as plain text.
    public static IReadOnlyList<FuriganaSegment>? ForSpelling(string spelling, string reading)
    {
        var segments = Parse(reading);
        if (!segments.Any(s => s.Ruby.Length > 0)) return null;

        return string.Concat(segments.Select(s => s.Text)) == spelling ? segments : null;
    }

    /// The reading as bare kana, for callers that cannot draw ruby. Bases the notation left
    /// unannotated stay as they are rather than being dropped.
    public static string ToKana(string reading)
        => string.Concat(Parse(reading).Select(s => s.Ruby.Length > 0 ? s.Ruby : s.Text));

    public static List<FuriganaSegment> Parse(string reading)
    {
        var segments = new List<FuriganaSegment>();
        var plain = new StringBuilder();

        for (var i = 0; i < reading.Length; i++)
        {
            var group = reading[i] == '[' ? FindReading(reading, i) : null;
            if (group is null)
            {
                plain.Append(reading[i]);
                continue;
            }

            var (ruby, close) = group.Value;
            var pending = plain.ToString();
            plain.Clear();
            i = close;

            // The reading annotates the trailing kanji run only; any kana ahead of it is okurigana
            // already written out in the base, so it stays a segment of its own.
            var runStart = pending.Length;
            while (runStart > 0 && IsRubyBase(pending[runStart - 1])) runStart--;

            if (runStart > 0) segments.Add(new FuriganaSegment(pending[..runStart], string.Empty));

            var baseText = pending[runStart..];
            segments.Add(baseText.Length > 0
                ? new FuriganaSegment(baseText, ruby)
                : new FuriganaSegment(ruby, string.Empty));
        }

        if (plain.Length > 0) segments.Add(new FuriganaSegment(plain.ToString(), string.Empty));

        return segments;
    }

    /// A bracket holding anything but kana is literal text, not an annotation.
    private static (string Ruby, int Close)? FindReading(string reading, int open)
    {
        var close = reading.IndexOf(']', open + 1);
        if (close < 0) return null;

        var ruby = reading[(open + 1)..close];
        return ruby.Length > 0 && ruby.All(IsKana) ? (ruby, close) : null;
    }

    private static bool IsKana(char c)
        => c is (>= '぀' and <= 'ゟ') or (>= '゠' and <= 'ヿ');

    /// Mirrors the base class of jiten.moe's ruby regex: kanji, the iteration mark, the ヵ/ヶ
    /// counters and fullwidth alphanumerics.
    private static bool IsRubyBase(char c)
        => c is (>= '一' and <= '鿿') or (>= '㐀' and <= '䶿') or (>= '豈' and <= '﫿')
            or (>= '０' and <= 'ｚ') or '々' or 'ヵ' or 'ヶ';
}
