using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Theming;

namespace JitenMPV.Core.Rendering;

/// One styled stretch of the subtitle text. The runs of a subtitle cover it in order.
/// <param name="TokenIndex">Position in the parse's token list, or -1 for the unparsed gaps
/// between tokens.</param>
public sealed record SubtitleRun(
    int Start, int Length, WordStyleState Style, int TokenIndex, ReaderToken? Token);

/// Resolves every token's style once, for both the ASS renderer and the native one.
public static class SubtitleRunBuilder
{
    public static (IReadOnlyList<SubtitleRun> Runs,
        Dictionary<(int WordId, byte ReadingIndex), UnderlineBar>? Underlines) Build(
        string text,
        ParseCacheEntry entry,
        StyleResolver styleResolver,
        HashSet<(int WordId, byte ReadingIndex)>? iPlusOneWords,
        HashSet<(int WordId, byte ReadingIndex)>? frequencyWords,
        HashSet<(int WordId, byte ReadingIndex)>? revealedWords)
    {
        var runs = new List<SubtitleRun>(entry.Tokens.Count * 2 + 1);
        Dictionary<(int WordId, byte ReadingIndex), UnderlineBar>? underlines = null;

        int lastEnd = 0;
        for (int i = 0; i < entry.Tokens.Count; i++)
        {
            var token = entry.Tokens[i];
            if (token.Start > lastEnd)
                runs.Add(new SubtitleRun(
                    lastEnd, token.Start - lastEnd, ThemePresets.Unparsed, -1, null));

            var style = styleResolver.Resolve(
                token, entry.VocabStates, iPlusOneWords, frequencyWords, entry.PitchClasses, revealedWords);

            if (style.Underline == true && style.UnderlineColor is { } barColor)
            {
                (underlines ??= [])[(token.WordId, token.ReadingIndex)] = new UnderlineBar(
                    barColor, style.UnderlineThickness ?? UnderlineBarRenderer.DefaultThickness);
            }

            runs.Add(new SubtitleRun(token.Start, token.Length, style, i, token));
            lastEnd = token.Start + token.Length;
        }

        if (lastEnd < text.Length)
            runs.Add(new SubtitleRun(
                lastEnd, text.Length - lastEnd, ThemePresets.Unparsed, -1, null));

        return (runs, underlines);
    }

    public static IReadOnlyList<SubtitleRun> Plain(string text)
        => [new SubtitleRun(0, text.Length, ThemePresets.Unparsed, -1, null)];
}
