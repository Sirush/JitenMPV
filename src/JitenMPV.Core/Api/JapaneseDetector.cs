namespace JitenMPV.Core.Api;

public static class JapaneseDetector
{
    public static bool ContainsJapanese(string text)
    {
        foreach (var c in text)
        {
            if (c is (>= '぀' and <= 'ゟ')
                  or (>= '゠' and <= 'ヿ')
                  or (>= '一' and <= '鿿')
                  or (>= '㐀' and <= '䶿'))
                return true;
        }
        return false;
    }
}
