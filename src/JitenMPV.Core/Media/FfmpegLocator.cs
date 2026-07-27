using System.Diagnostics;

namespace JitenMPV.Core.Media;

/// Resolves the ffmpeg executable once per process. Every media path goes through this so a single
/// probe (and a single configured path) serves pre-parsing and capture alike.
public static class FfmpegLocator
{
    private static readonly SemaphoreSlim ProbeLock = new(1, 1);
    private static string? _resolvedPath;
    private static string? _resolvedFrom;
    private static string? _version;
    private static bool _probed;

    /// The ffmpeg version banner from the successful probe, for the settings readout.
    public static string? Version => _version;

    public static void Invalidate()
    {
        _probed = false;
        _resolvedPath = null;
        _resolvedFrom = null;
        _version = null;
    }

    /// <param name="configuredPath">Empty resolves "ffmpeg" from PATH.</param>
    public static async Task<string?> ResolveAsync(string? configuredPath, CancellationToken ct)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredPath) ? "ffmpeg" : configuredPath.Trim();

        if (_probed && _resolvedFrom == candidate)
            return _resolvedPath;

        await ProbeLock.WaitAsync(ct);
        try
        {
            if (_probed && _resolvedFrom == candidate)
                return _resolvedPath;

            var version = await ProbeAsync(candidate, ct);
            _resolvedPath = version is null ? null : candidate;
            _version = version;
            _resolvedFrom = candidate;
            _probed = true;
            return _resolvedPath;
        }
        finally
        {
            ProbeLock.Release();
        }
    }

    private static async Task<string?> ProbeAsync(string path, CancellationToken ct)
    {
        try
        {
            using var proc = new Process();
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
        catch
        {
            return null;
        }
    }

    private static string? ParseVersion(string? banner)
    {
        if (string.IsNullOrWhiteSpace(banner)) return null;
        var parts = banner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[0] == "ffmpeg" && parts[1] == "version" ? parts[2] : null;
    }
}
