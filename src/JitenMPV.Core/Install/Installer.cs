using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Install;

public sealed record InstallOptions
{
    public string? MpvConfigDir { get; init; }

    /// Refreshes only the mpv Lua script, leaving the executable alone. The self-updater uses this
    /// so the script stays in lockstep with the binary that replaced it.
    public bool LuaOnly { get; init; }

    public bool DryRun { get; init; }
}

public sealed record InstallResult(
    bool Success, IReadOnlyList<string> Steps, string? Error = null, string? Warning = null);

public static class Installer
{
    private const string LuaResourceName = "JitenMPV.Core.Resources.jiten-mpv.lua";
    private const string LuaFileName = "jiten-mpv.lua";

    public static string ExecutableName => AppPaths.ExecutableName("JitenMPV.App");

    public static string InstalledExecutablePath => Path.Combine(AppPaths.AppDir, ExecutableName);

    /// True when both halves of an install are present. The executable alone is not enough: mpv
    /// never loads the plugin without the script, and the script alone spawns a path that is empty.
    public static bool IsInstalled(string? mpvConfigDir = null)
        => File.Exists(InstalledExecutablePath)
           && File.Exists(Path.Combine(MpvConfigLocator.Resolve(mpvConfigDir).ScriptsDir, LuaFileName));

    public static InstallResult Install(InstallOptions options)
    {
        var steps = new List<string>();

        try
        {
            var config = MpvConfigLocator.Resolve(options.MpvConfigDir);
            var scriptsDir = config.ScriptsDir;
            steps.Add($"mpv config directory: {config.FullPath} ({config.SourceLabel})");
            steps.Add($"Script directory:     {scriptsDir}");

            if (!options.LuaOnly)
            {
                steps.Add($"Program directory:    {AppPaths.AppDir}");
                if (CopyExecutable(options.DryRun) is { } copyNote)
                    steps.Add(copyNote);
            }

            if (!options.DryRun)
                Directory.CreateDirectory(scriptsDir);

            var scriptPath = Path.Combine(scriptsDir, LuaFileName);
            if (!options.DryRun)
                WriteEmbeddedScript(scriptPath);

            steps.Add($"Script installed:     {scriptPath}");
            return new InstallResult(true, steps, Warning: MissingJapaneseFontWarning());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            return new InstallResult(false, steps, ex.Message);
        }
    }

    public static InstallResult Uninstall(InstallOptions options, bool removeProgram)
    {
        var steps = new List<string>();

        try
        {
            var scriptPath = Path.Combine(
                MpvConfigLocator.Resolve(options.MpvConfigDir).ScriptsDir, LuaFileName);

            if (File.Exists(scriptPath))
            {
                if (!options.DryRun) File.Delete(scriptPath);
                steps.Add($"Removed script:       {scriptPath}");
            }
            else
            {
                steps.Add($"No script at:         {scriptPath}");
            }

            if (removeProgram && File.Exists(InstalledExecutablePath))
            {
                // Running from the copy being deleted is the normal case on Windows, where an open
                // executable cannot be removed; saying so beats a bare access-denied.
                if (!options.DryRun) File.Delete(InstalledExecutablePath);
                steps.Add($"Removed program:      {InstalledExecutablePath}");
            }

            steps.Add($"Settings kept in:     {AppPaths.ConfigDir}");
            return new InstallResult(true, steps);
        }
        catch (IOException ex)
        {
            return new InstallResult(false, steps,
                $"{ex.Message} (close mpv and any running JitenMPV first)");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new InstallResult(false, steps, ex.Message);
        }
    }

    /// The dictionary popup falls back through several CJK families, but no fallback helps a system
    /// that has none installed: the kana render as boxes. Nothing here can fix that, so it is said
    /// at install time rather than discovered mid-episode.
    /// <returns>Null when a Japanese font exists, or when fontconfig cannot answer.</returns>
    private static string? MissingJapaneseFontWarning()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("fc-list")
            {
                ArgumentList = { ":lang=ja", "family" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null) return null;

            var families = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000) || process.ExitCode != 0) return null;
            if (families.Trim().Length > 0) return null;

            return "Warning: no Japanese font is installed, so the dictionary popup will show "
                   + "boxes instead of kana. Install one, for example fonts-noto-cjk.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // No fontconfig on this system, so nothing can be concluded either way.
            return null;
        }
    }

    /// <returns>A description of what happened, or null when there was nothing to do.</returns>
    private static string? CopyExecutable(bool dryRun)
    {
        // Environment.ProcessPath is the real executable in a single-file build, where
        // Assembly.Location returns an empty string.
        var source = Environment.ProcessPath;
        if (string.IsNullOrEmpty(source))
            throw new InvalidOperationException("Could not determine the running executable's path.");

        var destination = InstalledExecutablePath;

        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination),
                StringComparison.OrdinalIgnoreCase))
            return "Program already in place, not copied.";

        // Copying one file is only valid for a single-file publish. A development build leaves its
        // assemblies beside the host, and copying the host alone installs something that cannot
        // start, with no error until mpv tries to spawn it.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "JitenMPV.Core.dll")))
            throw new InvalidOperationException(
                "This is a development build, which is more than one file. Install from a published "
                + "build instead: dotnet publish src/JitenMPV.App -c Release -r <rid>");

        if (dryRun) return $"Would copy:           {source}";

        Directory.CreateDirectory(AppPaths.AppDir);
        File.Copy(source, destination, overwrite: true);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(destination,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        return $"Program copied:       {destination}";
    }

    /// Overwrites unconditionally: the embedded copy is the source of truth, and this doubles as
    /// the script's update path when a new binary is installed over an old one.
    private static void WriteEmbeddedScript(string destination)
    {
        using var resource = typeof(Installer).Assembly.GetManifestResourceStream(LuaResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource {LuaResourceName} is missing from this build.");

        using var file = File.Create(destination);
        resource.CopyTo(file);
    }

    public static string CurrentVersion
    {
        get
        {
            // The entry assembly carries the version the release was tagged with; this library's
            // own version is not what a user is told they are running.
            var assembly = Assembly.GetEntryAssembly() ?? typeof(Installer).Assembly;

            // Informational version is the only one carrying a prerelease suffix; AssemblyVersion
            // silently drops it, so `1.2.3-beta` would read as 1.2.3.
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // The SDK appends the source-revision id after a '+'.
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}
