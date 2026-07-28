using System.Diagnostics;
using System.Runtime.InteropServices;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Media;

public enum FfmpegSource
{
    Configured,
    Managed,
    Path,
    WellKnown
}

/// <param name="ExecutablePath">Absolute, except for the bare PATH candidate.</param>
public sealed record FfmpegResolution(string ExecutablePath, string Version, FfmpegSource Source)
{
    /// Drops git-describe and vendor suffixes, which run long enough to wrap the status line:
    /// "n8.1.2-31-g8c9502e9b0-20260727" and "5.1.2-essentials_build-www.gyan.dev" both become the
    /// version number alone. The full string is what gets recorded for provenance.
    public string DisplayVersion => Version.Split('-')[0];

    /// Distinguishes the fixes: a stale configured path, a broken managed install and a missing
    /// PATH entry all read as "found"/"not found" otherwise.
    public string SourceLabel => Source switch
    {
        FfmpegSource.Configured => "your chosen path",
        FfmpegSource.Managed    => "installed by JitenMPV",
        FfmpegSource.Path       => "found on your PATH",
        _                       => ExecutablePath
    };
}

/// Resolves the ffmpeg executable once per process. Every media path goes through this so a single
/// probe (and a single configured path) serves pre-parsing and capture alike.
public static class FfmpegLocator
{
    private static readonly SemaphoreSlim ProbeLock = new(1, 1);
    private static FfmpegResolution? _resolution;
    private static string? _probedFor;
    private static bool _probed;

    /// The resolution from the last successful probe, for the settings readout.
    public static FfmpegResolution? Current => _resolution;

    public static string? Version => _resolution?.Version;

    public static void Invalidate()
    {
        _probed = false;
        _probedFor = null;
        _resolution = null;
    }

    /// <param name="configuredPath">Empty searches the managed install, PATH, then well-known locations.</param>
    public static async Task<FfmpegResolution?> ResolveAsync(string? configuredPath, CancellationToken ct)
    {
        var configured = configuredPath?.Trim() ?? "";

        if (_probed && _probedFor == configured)
            return _resolution;

        await ProbeLock.WaitAsync(ct);
        try
        {
            if (_probed && _probedFor == configured)
                return _resolution;

            _resolution = await ProbeChainAsync(configured, ct);
            _probedFor = configured;
            _probed = true;
            return _resolution;
        }
        finally
        {
            ProbeLock.Release();
        }
    }

    private static async Task<FfmpegResolution?> ProbeChainAsync(string configured, CancellationToken ct)
    {
        foreach (var (candidate, source) in Candidates(configured))
        {
            // Only the bare PATH name needs a process to rule out; spawning one per absent absolute
            // path would cost a probe timeout each on the machines least likely to have ffmpeg.
            if (source != FfmpegSource.Path && !File.Exists(candidate))
                continue;

            if (await ProbeAsync(candidate, ct) is { } version)
                return new FfmpegResolution(candidate, version, source);
        }

        return null;
    }

    /// A configured path that no longer works falls through to the rest of the chain rather than
    /// failing outright; SourceLabel is what tells the user their setting was bypassed.
    private static IEnumerable<(string Path, FfmpegSource Source)> Candidates(string configured)
    {
        if (!string.IsNullOrEmpty(configured))
            yield return (configured, FfmpegSource.Configured);

        yield return (AppPaths.ManagedFfmpegExe, FfmpegSource.Managed);
        yield return ("ffmpeg", FfmpegSource.Path);

        foreach (var path in WellKnownPaths())
            yield return (path, FfmpegSource.WellKnown);
    }

    /// Locations a GUI-spawned process cannot see: mpv launched from Finder or a desktop shortcut
    /// inherits no login-shell PATH, which is what hides a Homebrew ffmpeg from the probe above.
    private static IEnumerable<string> WellKnownPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            yield return Path.Combine(userProfile, "scoop", "shims", "ffmpeg.exe");
            yield return @"C:\ProgramData\chocolatey\bin\ffmpeg.exe";
            yield return @"C:\ffmpeg\bin\ffmpeg.exe";
            yield break;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/opt/homebrew/bin/ffmpeg";
            yield return "/usr/local/bin/ffmpeg";
            yield return "/opt/local/bin/ffmpeg";
            yield break;
        }

        yield return "/usr/bin/ffmpeg";
        yield return "/usr/local/bin/ffmpeg";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "ffmpeg");
    }

    /// Validates one specific binary without consulting or disturbing the cached chain, so a fresh
    /// install can be checked even when a configured path would otherwise win.
    public static Task<string?> ProbeVersionAsync(string path, CancellationToken ct)
        => ProbeAsync(path, ct);

    private static async Task<string?> ProbeAsync(string path, CancellationToken ct)
    {
        Process? proc = null;
        try
        {
            proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.StartInfo.ArgumentList.Add("-version");

            proc.Start();
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            await proc.WaitForExitAsync(timeoutCts.Token);

            if (proc.ExitCode != 0) return null;

            var banner = (await stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                         ?? (await stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return ParseVersion(banner) ?? "unknown";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            // A candidate that hangs past the timeout would otherwise outlive the probe holding
            // its redirected pipes open.
            TryKill(proc);
            proc?.Dispose();
        }
    }

    private static void TryKill(Process? proc)
    {
        try
        {
            if (proc is { HasExited: false })
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or never started.
        }
    }

    private static string? ParseVersion(string? banner)
    {
        if (string.IsNullOrWhiteSpace(banner)) return null;
        var parts = banner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[0] == "ffmpeg" && parts[1] == "version" ? parts[2] : null;
    }
}
