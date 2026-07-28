using System.Runtime.InteropServices;

namespace JitenMPV.Core.Config;

/// Windows collapses both roots into %APPDATA%\jiten-mpv, Unix does not, so they must stay
/// distinct: AppDir holds the executable and anything we install beside it, ConfigDir holds
/// config.json and logs. AppDir must match get_exe_path() in scripts/jiten-mpv.lua exactly, or
/// mpv spawns a path that does not exist and the plugin silently never starts.
public static class AppPaths
{
    private const string FolderName = "jiten-mpv";

    public static string AppDir { get; } = ResolveAppDir();

    public static string ConfigDir { get; } = ResolveConfigDir();

    public static string ManagedFfmpegDir => Path.Combine(AppDir, "ffmpeg");

    public static string ManagedFfmpegExe => Path.Combine(ManagedFfmpegDir, ExecutableName("ffmpeg"));

    public static string ExecutableName(string baseName)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? baseName + ".exe" : baseName;

    private static string ResolveAppDir()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(RoamingAppData(), FolderName)
            : Path.Combine(XdgOrDefault("XDG_DATA_HOME", ".local", "share"), FolderName);

    private static string ResolveConfigDir()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(RoamingAppData(), FolderName)
            : Path.Combine(XdgOrDefault("XDG_CONFIG_HOME", ".config"), FolderName);

    /// Resolved from the environment rather than `SpecialFolder.ApplicationData`, which returns an
    /// empty string on Unix when the directory does not exist yet and would silently yield a path
    /// relative to the working directory. The Lua script reads these same variables directly, so
    /// any disagreement here splits config between two locations.
    private static string XdgOrDefault(string variable, params string[] fallback)
    {
        var configured = Environment.GetEnvironmentVariable(variable);

        // The XDG spec requires relative values to be ignored rather than resolved against cwd.
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathRooted(configured))
            return configured;

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine([home, .. fallback]);
    }

    private static string RoamingAppData()
        => Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
}
