using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Media;
using JitenMPV.Core.Fonts;

namespace JitenMPV.App.Fonts;

/// The installed fonts offered as subtitle families. Enumeration source is per platform on purpose:
/// on Linux it has to be fontconfig, because that is what libass resolves the subtitle font name
/// against, and the toolkit's own font manager answers for a different renderer.
public static class SystemFontCatalog
{
    /// Both kana blocks plus two kanji. Han unification means glyph coverage cannot tell a Japanese
    /// face from a Chinese or Korean one, so this only rejects fonts that cannot render Japanese at
    /// all; a listed font may still draw Chinese kanji forms.
    private static readonly int[] JapaneseProbeCodepoints = [0x3042, 0x30A2, 0x8A9E, 0x76F4];

    private static readonly Lazy<FontFamilyNames> Catalog =
        new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    /// Blocks on a first call that walks every installed font, so keep it off the UI thread.
    public static IReadOnlyList<string> JapaneseFamilies => Catalog.Value.Primary;

    /// False only when the font was positively established to be unusable: an empty catalog means
    /// enumeration failed, and a warning drawn from that would be guesswork.
    public static bool CanRenderJapanese(string family)
    {
        var names = Catalog.Value.AllNames;
        return names.Count == 0
               || names.Contains(family.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static FontFamilyNames Build()
        => FontconfigCatalog.QueryJapaneseFamilies() is { Primary.Count: > 0 } fromFontconfig
            ? fromFontconfig
            : ScanFontManager();

    private static FontFamilyNames ScanFontManager()
    {
        var manager = FontManager.Current;
        var names = new List<string>();

        foreach (var family in manager.SystemFonts)
        {
            if (!manager.TryGetGlyphTypeface(new Typeface(family), out var glyphs)) continue;
            if (!JapaneseProbeCodepoints.All(glyphs.CharacterToGlyphMap.ContainsGlyph)) continue;
            names.Add(family.Name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return new FontFamilyNames(names, names);
    }
}
