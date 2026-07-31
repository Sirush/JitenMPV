using System.Runtime.InteropServices;

namespace JitenMPV.Core.Fonts;

/// The subtitle font a fresh config starts with
public static class DefaultSubtitleFont
{
    private static readonly string[] LinuxPreference =
    [
        "Noto Sans CJK JP", "Source Han Sans JP", "Noto Sans JP", "IPAexGothic",
        "IPAPGothic", "IPAGothic", "VL PGothic", "VL Gothic", "TakaoPGothic",
        "MotoyaLCedar", "Sazanami Gothic"
    ];

    private const string WindowsDefault = "Yu Gothic";
    private const string MacDefault = "Hiragino Sans";

    private static readonly Lazy<string> Resolved =
        new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Value => Resolved.Value;

    private static string Resolve()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return MacDefault;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return WindowsDefault;

        var installed = FontconfigCatalog.QueryJapaneseFamilies();
        if (installed is null || installed.AllNames.Count == 0) return LinuxPreference[0];

        foreach (var preferred in LinuxPreference)
            if (installed.AllNames.Contains(preferred, StringComparer.OrdinalIgnoreCase))
                return preferred;

        return installed.Primary.Count > 0 ? installed.Primary[0] : LinuxPreference[0];
    }
}
