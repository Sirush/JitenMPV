using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using JitenMPV.Core.Config;

namespace JitenMPV.Core.Media;

/// <param name="Fraction">Null while the step has no measurable length.</param>
public sealed record FfmpegInstallProgress(string Stage, double? Fraction);

public sealed record FfmpegInstallResult(bool Success, string Message, string? InstalledPath = null);

/// Fetches a prebuilt ffmpeg into the directory we own. Downloading it ourselves rather than
/// sending users to a browser is what keeps macOS quarantine and Windows Mark-of-the-Web out of
/// the picture, and LGPL builds keep this project clear of any conveyance obligation.
public static class FfmpegInstaller
{
    private const string Branch = "n8.1";
    private const string ReleaseVersion = "8.1";
    private const string ReleaseBaseUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";
    private const string ChecksumsAsset = "checksums.sha256";

    private static readonly HttpClient Http = CreateClient();

    /// BtbN publishes no macOS builds, and every third-party macOS source is either Intel-only or
    /// unusable from a non-browser client, so macOS is directed at Homebrew instead.
    public static bool IsSupported => AssetName() is not null;

    public static string TargetDirectory => AppPaths.ManagedFfmpegDir;

    public static async Task<FfmpegInstallResult> InstallAsync(
        IProgress<FfmpegInstallProgress>? progress, CancellationToken ct)
    {
        if (AssetName() is not { } asset)
            return new FfmpegInstallResult(false, "No prebuilt ffmpeg is available for this platform.");

        var workDir = Path.Combine(Path.GetTempPath(), "jiten-mpv-ffmpeg-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(workDir);
            var archivePath = Path.Combine(workDir, asset);

            progress?.Report(new FfmpegInstallProgress("Downloading ffmpeg", 0));
            await DownloadAsync(ReleaseBaseUrl + asset, archivePath, progress, ct);

            progress?.Report(new FfmpegInstallProgress("Checking the download", null));
            if (await VerifyChecksumAsync(archivePath, asset, ct) is { } checksumError)
                return new FfmpegInstallResult(false, checksumError);

            progress?.Report(new FfmpegInstallProgress("Installing", null));
            Directory.CreateDirectory(TargetDirectory);
            var destination = AppPaths.ManagedFfmpegExe;

            if (!await ExtractBinaryAsync(archivePath, workDir, destination, ct))
                return new FfmpegInstallResult(false, "The download did not contain an ffmpeg binary.");

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                File.SetUnixFileMode(destination,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var version = await FfmpegLocator.ProbeVersionAsync(destination, ct);
            if (version is null)
                return new FfmpegInstallResult(false, "ffmpeg was installed but would not run.");

            await WriteProvenanceAsync(asset, version, ct);
            FfmpegLocator.Invalidate();

            return new FfmpegInstallResult(true, $"ffmpeg {version} is ready.", destination);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new FfmpegInstallResult(false, "Cancelled.");
        }
        catch (IOException ex) when (IsFileInUse(ex))
        {
            return new FfmpegInstallResult(false, "ffmpeg is in use. Close mpv and try again.");
        }
        catch (HttpRequestException ex)
        {
            return new FfmpegInstallResult(false, $"Download failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new FfmpegInstallResult(false, $"Install failed: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static async Task DownloadAsync(
        string url, string destination, IProgress<FfmpegInstallProgress>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;

            if (total is > 0)
                progress?.Report(new FfmpegInstallProgress("Downloading ffmpeg", (double)copied / total.Value));
        }
    }

    /// <returns>Null when the archive matches the published hash, otherwise the reason it did not.</returns>
    private static async Task<string?> VerifyChecksumAsync(string archivePath, string asset, CancellationToken ct)
    {
        string manifest;
        try
        {
            manifest = await Http.GetStringAsync(ReleaseBaseUrl + ChecksumsAsset, ct);
        }
        catch (HttpRequestException ex)
        {
            return $"Could not fetch the checksum list: {ex.Message}";
        }

        var expected = FindHash(manifest, asset);
        if (expected is null)
            return "The checksum list did not cover this download.";

        await using var stream = File.OpenRead(archivePath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));

        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)
            ? null
            : "The download was corrupted (checksum mismatch).";
    }

    private static string? FindHash(string manifest, string asset)
    {
        foreach (var line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].TrimStart('*') == asset)
                return parts[0];
        }

        return null;
    }

    private static async Task<bool> ExtractBinaryAsync(
        string archivePath, string workDir, string destination, CancellationToken ct)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.Entries.FirstOrDefault(
                e => e.FullName.EndsWith("bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return false;

            entry.ExtractToFile(destination, overwrite: true);
            return true;
        }

        // .NET reads tar but not xz; shelling out avoids a compression dependency for a path that
        // only ever runs on Unix, where tar is always present.
        var extracted = Path.Combine(workDir, "ffmpeg");
        var ok = await RunTarAsync(archivePath, workDir, ct);
        if (!ok || !File.Exists(extracted)) return false;

        File.Move(extracted, destination, overwrite: true);
        return true;
    }

    private static async Task<bool> RunTarAsync(string archivePath, string workDir, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = "tar",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[]
                 {
                     "-xJf", archivePath, "-C", workDir,
                     "--strip-components=2", "--wildcards", "*/bin/ffmpeg"
                 })
            proc.StartInfo.ArgumentList.Add(arg);

        proc.Start();
        await proc.WaitForExitAsync(ct);
        return proc.ExitCode == 0;
    }

    /// Records where the binary came from. JitenMPV never conveys ffmpeg (the user's own machine
    /// fetches it from BtbN), so this is provenance for the curious rather than a licence
    /// obligation, and it is what makes a stale managed install diagnosable later.
    private static async Task WriteProvenanceAsync(string asset, string version, CancellationToken ct)
    {
        var text =
            $"""
             ffmpeg {version}
             Installed by JitenMPV on {DateTime.UtcNow:yyyy-MM-dd} (UTC).

             Downloaded from:
               {ReleaseBaseUrl}{asset}

             This is an LGPL build produced by the BtbN/FFmpeg-Builds project.
               Build scripts: https://github.com/BtbN/FFmpeg-Builds
               ffmpeg source: https://ffmpeg.org/download.html
               Licence text:  https://www.ffmpeg.org/legal.html

             JitenMPV runs ffmpeg as a separate process and does not link against it.
             Delete this folder to remove the copy JitenMPV installed.
             """;

        await File.WriteAllTextAsync(Path.Combine(TargetDirectory, "SOURCE.txt"), text, ct);
    }

    private static string? AssetName()
    {
        var arch = RuntimeInformation.OSArchitecture;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return arch switch
            {
                Architecture.X64   => Asset("win64", "zip"),
                Architecture.Arm64 => Asset("winarm64", "zip"),
                _                  => null
            };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return arch switch
            {
                Architecture.X64   => Asset("linux64", "tar.xz"),
                Architecture.Arm64 => Asset("linuxarm64", "tar.xz"),
                _                  => null
            };

        return null;
    }

    private static string Asset(string platform, string extension)
        => $"ffmpeg-{Branch}-latest-{platform}-lgpl-{ReleaseVersion}.{extension}";

    /// SHARING_VIOLATION / LOCK_VIOLATION on Windows, ETXTBSY on Unix.
    private static bool IsFileInUse(IOException ex)
        => ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
           || ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("Text file busy", StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Temp files; the OS reclaims them.
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("jiten-mpv");
        return client;
    }
}
