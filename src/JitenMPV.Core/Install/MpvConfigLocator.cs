using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JitenMPV.Core.Install;

public enum MpvConfigSource
{
    Explicit,
    MpvHome,
    PortableConfig,
    UserConfig
}

public sealed record MpvConfigDir(string FullPath, MpvConfigSource Source)
{
    public string ScriptsDir => Path.Combine(FullPath, "scripts");

    public string SourceLabel => Source switch
    {
        MpvConfigSource.Explicit => "the directory you specified",
        MpvConfigSource.MpvHome => "MPV_HOME",
        MpvConfigSource.PortableConfig => "portable_config beside mpv",
        _ => "your user config directory"
    };
}

/// Finds the directory mpv actually reads configuration from. Getting this wrong fails silently:
/// the script lands somewhere mpv never looks, the install reports success, and nothing happens.
public static class MpvConfigLocator
{
    public static MpvConfigDir Resolve(string? explicitDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return new MpvConfigDir(Path.GetFullPath(explicitDir.Trim()), MpvConfigSource.Explicit);

        var mpvHome = Environment.GetEnvironmentVariable("MPV_HOME");
        if (!string.IsNullOrWhiteSpace(mpvHome))
            return new MpvConfigDir(Path.GetFullPath(mpvHome), MpvConfigSource.MpvHome);

        // A portable_config directory beside mpv.exe makes mpv load configuration exclusively from
        // there and never read %APPDATA%\mpv. The portable builds this project's audience uses are
        // built around it, so this is not an edge case.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && FindPortableConfig() is { } portable)
            return new MpvConfigDir(portable, MpvConfigSource.PortableConfig);

        return new MpvConfigDir(UserConfigDir(), MpvConfigSource.UserConfig);
    }

    private static string UserConfigDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolderOption.DoNotVerify), "mpv");

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg))
            return Path.Combine(xdg, "mpv");

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(home, ".config", "mpv");
    }

    private static string? FindPortableConfig()
    {
        foreach (var dir in MpvDirectories())
        {
            var candidate = Path.Combine(dir, "portable_config");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// Ordered by confidence: the mpv on PATH is the one that will actually run, so its
    /// portable_config wins over any other installation's.
    private static IEnumerable<string> MpvDirectories()
    {
        if (FindOnPath("mpv.exe") is { } onPath)
            yield return onPath;

        if (RunningMpvDirectory() is { } running)
            yield return running;

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);

        yield return Path.Combine(userProfile, "scoop", "apps", "mpv", "current");
        yield return Path.Combine(localAppData, "Programs", "mpv");
        yield return @"C:\Program Files\mpv";
        yield return @"C:\Program Files (x86)\mpv";
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir.Trim(), fileName)))
                    return dir.Trim();
            }
            catch (ArgumentException)
            {
                // PATH entries containing invalid path characters are common enough not to be fatal.
            }
        }

        return null;
    }

    private static string? RunningMpvDirectory()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("mpv"))
            {
                using (process)
                {
                    var file = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(file))
                        return Path.GetDirectoryName(file);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
        {
            // Reading another process's module list can be denied; not worth failing the install over.
        }

        return null;
    }
}
