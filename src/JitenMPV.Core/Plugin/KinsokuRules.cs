namespace JitenMPV.Core.Plugin;

/// Where a Japanese line may break, shared by the mpv-measured wrap resolver and the native
/// layout engine so both put their breaks in the same places.
public static class KinsokuRules
{
    /// U+3000 and up covers kana, kanji and the full-width punctuation that surrounds them,
    /// matching SubtitleLineJoiner's split between text that wraps anywhere and text that does not.
    public const char CjkStart = '　';

    /// 行頭禁則: characters a row may not open with. Sentence-trailing punctuation and closing
    /// brackets belong to the text before them, and the small kana, the prolonged sound mark and
    /// the iteration marks are part of the syllable they follow.
    public const string NeverLeads =
        "、。，．・：；？！?!)]}）］｝〉》」』】〕〙〗〞’”｠»‼⁇⁈⁉" +
        "ぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶｧｨｩｪｫｬｭｮｯ" +
        "ー～〜ｰ々〻ゝゞヽヾ";

    /// 行末禁則: characters a row may not close with, since they introduce what comes after them.
    public const string NeverTrails = "([{（［｛〈《「『【〔〘〖〝‘“｟«";

    /// A Latin word breaks only at a space while CJK breaks between any two characters; kinsoku
    /// then holds back the characters that may not open or close a row.
    public static bool IsBreakOpportunity(string line, int at)
    {
        char prev = line[at - 1];
        bool breakable = char.IsWhiteSpace(prev) || prev >= CjkStart || line[at] >= CjkStart;
        return breakable && !NeverLeads.Contains(line[at]) && !NeverTrails.Contains(prev);
    }

    /// Walks a break back onto a position both libass and Japanese typesetting allow. Walking back
    /// only shortens the row, so the result still fits. A run with no position at all - one long
    /// Latin word, or punctuation stacked back to the previous break - keeps the index that fits,
    /// since overflowing is what libass does with it too.
    public static int SnapToBreakOpportunity(string line, int at, int min)
    {
        for (int i = at; i > min; i--)
            if (IsBreakOpportunity(line, i)) return i;
        return at;
    }
}
