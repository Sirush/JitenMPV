using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JitenMPV.Core.Fonts;

/// <param name="Primary">One canonical name per font, for offering a choice.</param>
/// <param name="AllNames">Every name a font answers to, for deciding whether a name will resolve.</param>
public sealed record FontFamilyNames(IReadOnlyList<string> Primary, IReadOnlyList<string> AllNames);

/// Family names as fontconfig knows them. libass resolves subtitle font names against the same
/// database, so a name listed here is one mpv can actually render with; Skia's own enumeration
/// answers for the toolkit's renderer instead and need not agree.
public static class FontconfigCatalog
{
    private const int QueryTimeoutMs = 5000;

    public static bool IsSupportedPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <returns>Families covering Japanese, or null when fontconfig could not answer.</returns>
    public static FontFamilyNames? QueryJapaneseFamilies() => Parse(Run(":lang=ja"));

    private static string? Run(string pattern)
    {
        if (!IsSupportedPlatform) return null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("fc-list")
            {
                ArgumentList = { pattern, "family" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(QueryTimeoutMs) || process.ExitCode != 0) return null;
            return output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // No fontconfig on this system, so nothing can be concluded either way.
            return null;
        }
    }

    /// fc-list prints one line per font, listing that font's family names comma-separated with the
    /// canonical one first and localized aliases after it.
    private static FontFamilyNames? Parse(string? output)
    {
        if (output is null) return null;

        var primary = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split('\n'))
        {
            var names = line.Split(',');
            bool isFirst = true;

            foreach (var raw in names)
            {
                var name = raw.Trim();
                if (name.Length == 0) continue;

                all.Add(name);
                if (isFirst) primary.Add(name);
                isFirst = false;
            }
        }

        return new FontFamilyNames([.. primary], [.. all]);
    }
}
