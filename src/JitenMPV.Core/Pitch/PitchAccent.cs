using System.Text.RegularExpressions;

namespace JitenMPV.Core.Pitch;

public enum PitchClass
{
    Unknown = 0,
    Heiban,
    Atamadaka,
    Nakadaka,
    Odaka
}

/// <param name="Pattern">
/// One entry per mora plus a trailing entry for the following particle; true is high.
/// </param>
public sealed record PitchDiagram(
    IReadOnlyList<string> Morae,
    IReadOnlyList<bool> Pattern,
    PitchClass Class);

public static partial class PitchAccent
{
    /// Small yoon vowels attach to the preceding mora. Small tsu does count as its own mora.
    private static readonly HashSet<char> SmallNonMora =
        ['ゃ', 'ゅ', 'ょ', 'ャ', 'ュ', 'ョ', 'ァ', 'ィ', 'ゥ', 'ェ', 'ォ'];

    public static List<string> SplitMorae(string reading)
    {
        List<string> morae = [];

        foreach (var ch in reading)
        {
            if (morae.Count > 0 && SmallNonMora.Contains(ch))
                morae[^1] += ch;
            else
                morae.Add(ch.ToString());
        }

        return morae;
    }

    /// Readings can carry kanji, bracketed notes and latin text, none of which are morae.
    public static string CleanReading(string reading)
        => NonKanaPattern().Replace(reading, string.Empty);

    /// Accent 0 is unaccented; otherwise the number is the mora after which the pitch drops.
    /// A word accented on its final mora drops onto the following particle, which is odaka —
    /// including one-mora words, where that is the only reading of accent 1.
    public static PitchClass Classify(int accent, int moraCount)
    {
        if (moraCount <= 0 || accent < 0) return PitchClass.Unknown;
        if (accent == 0) return PitchClass.Heiban;
        if (accent == moraCount) return PitchClass.Odaka;
        if (accent == 1) return PitchClass.Atamadaka;
        if (accent < moraCount) return PitchClass.Nakadaka;

        return PitchClass.Unknown;
    }

    public static PitchClass ClassifyReading(string reading, int accent)
        => Classify(accent, SplitMorae(CleanReading(reading)).Count);

    public static PitchDiagram? BuildDiagram(string reading, int accent)
    {
        var morae = SplitMorae(CleanReading(reading));
        if (morae.Count == 0 || accent < 0) return null;

        var pattern = new bool[morae.Count + 1];

        if (accent == 0)
        {
            for (var i = 1; i <= morae.Count; i++)
                pattern[i] = true;
        }
        else
        {
            pattern[0] = accent == 1;
            for (var i = 1; i < morae.Count; i++)
                pattern[i] = i < accent;
        }

        return new PitchDiagram(morae, pattern, Classify(accent, morae.Count));
    }

    /// Matches the Reader extension's pitch palette.
    public static string DefaultColor(PitchClass pitchClass) => pitchClass switch
    {
        PitchClass.Heiban => "#d20ca3",
        PitchClass.Atamadaka => "#ea9316",
        PitchClass.Nakadaka => "#27a2ff",
        PitchClass.Odaka => "#0cd24d",
        _ => "#cccccc"
    };

    public static readonly PitchClass[] Styleable =
        [PitchClass.Heiban, PitchClass.Atamadaka, PitchClass.Nakadaka, PitchClass.Odaka];

    [GeneratedRegex(@"[一-鿿㐀-䶿０-ｚ\[\]A-Za-z0-9]")]
    private static partial Regex NonKanaPattern();
}
