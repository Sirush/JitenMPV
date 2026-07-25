namespace JitenMPV.Core.Plugin;

public static class SentenceFormatter
{
    private const int MaxSentenceLength = 150;
    private const int MarkerOverhead = 4;

    /// Wraps the mined word in ** markers and trims the surrounding context to the server's
    /// 150-character limit, keeping the word centred. Returns null when the word is not present
    /// or no context fits around it.
    public static string? WithMarkers(string? sentence, string? surfaceForm)
    {
        if (string.IsNullOrEmpty(sentence) || string.IsNullOrEmpty(surfaceForm))
            return null;

        var index = sentence.IndexOf(surfaceForm, StringComparison.Ordinal);
        if (index < 0) return null;

        var before = sentence[..index];
        var after = sentence[(index + surfaceForm.Length)..];

        var marked = $"{before}**{surfaceForm}**{after}";
        if (marked.Length <= MaxSentenceLength) return marked;

        var budget = MaxSentenceLength - surfaceForm.Length - MarkerOverhead;
        if (budget <= 0) return null;

        var halfBudget = budget / 2;
        var trimmedBefore = before.Length > halfBudget ? before[^halfBudget..] : before;
        var remaining = budget - trimmedBefore.Length;
        var trimmedAfter = after.Length > remaining ? after[..remaining] : after;

        return $"{trimmedBefore}**{surfaceForm}**{trimmedAfter}";
    }
}
