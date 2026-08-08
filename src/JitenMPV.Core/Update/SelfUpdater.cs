using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using JitenMPV.Core.Config;
using JitenMPV.Core.Install;

namespace JitenMPV.Core.Update;

/// <param name="Fraction">Null while the step has no measurable length.</param>
public sealed record UpdateProgress(string Stage, double? Fraction);

public sealed record UpdateResult(bool Success, string Message);

/// Replaces the installed executable with a newer release. There is no restart choreography to do:
/// the new binary takes effect the next time mpv spawns the plugin.
public static class SelfUpdater
{
    private static readonly HttpClient Http = CreateClient();

    public static bool IsSupported => AssetName() is not null;

    /// Removes what a previous swap left behind. On Windows the outgoing binary is still running
    /// when it is renamed, so it can only be deleted on a later launch.
    public static void CleanupPreviousVersion()
    {
        TryDelete(Installer.InstalledExecutablePath + ".old");
        TryDelete(Installer.InstalledExecutablePath + ".new");
    }

    public static async Task<UpdateResult> UpdateAsync(
        UpdateInfo update, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        if (AssetName() is not { } asset)
            return new UpdateResult(false, "No release is published for this platform.");

        var target = Installer.InstalledExecutablePath;
        if (!File.Exists(target))
            return new UpdateResult(false,
                "JitenMPV is not installed for mpv yet, so there is nothing to replace.");

        var workDir = Path.Combine(Path.GetTempPath(), "jiten-mpv-update-" + Guid.NewGuid().ToString("N"));
        var baseUrl = $"{UpdateChecker.ReleasesUrl}/download/{update.TagName}/";

        try
        {
            Directory.CreateDirectory(workDir);
            var archivePath = Path.Combine(workDir, asset);

            progress?.Report(new UpdateProgress($"Downloading {update.Version}", 0));
            await DownloadAsync(baseUrl + asset, archivePath, update.Version, progress, ct);

            progress?.Report(new UpdateProgress("Checking the download", null));
            if (await VerifyChecksumAsync(archivePath, baseUrl + asset + ".sha256", ct) is { } checksumError)
                return new UpdateResult(false, checksumError);

            progress?.Report(new UpdateProgress("Installing", null));
            if (await ExtractExecutableAsync(archivePath, workDir, ct) is not { } extracted)
                return new UpdateResult(false, "The download did not contain a JitenMPV executable.");

            if (RemoveSupersededNatives() is { } blocked)
                return new UpdateResult(false, blocked);

            Swap(extracted, target);

            var message = $"Updated to {update.Version}. It takes effect the next time mpv starts.";
            if (!RefreshLuaScript(target))
                message += " The mpv script could not be refreshed; run the install once from settings.";

            return new UpdateResult(true, message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new UpdateResult(false, "Cancelled.");
        }
        catch (IOException ex) when (IsFileInUse(ex))
        {
            return new UpdateResult(false, "JitenMPV is in use. Close mpv and try again.");
        }
        catch (HttpRequestException ex)
        {
            return new UpdateResult(false, $"Download failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, $"Update failed: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static async Task DownloadAsync(
        string url, string destination, string version,
        IProgress<UpdateProgress>? progress, CancellationToken ct)
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
                progress?.Report(new UpdateProgress($"Downloading {version}", (double)copied / total.Value));
        }
    }

    /// <returns>Null when the archive matches the published hash, otherwise the reason it did not.</returns>
    private static async Task<string?> VerifyChecksumAsync(string archivePath, string url, CancellationToken ct)
    {
        string manifest;
        try
        {
            manifest = await Http.GetStringAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            return $"Could not fetch the checksum: {ex.Message}";
        }

        // sha256sum output: the hash, then the file it covers.
        var expected = manifest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expected is null) return "The release published no usable checksum.";

        await using var stream = File.OpenRead(archivePath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));

        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)
            ? null
            : "The download was corrupted (checksum mismatch).";
    }

    private static async Task<string?> ExtractExecutableAsync(
        string archivePath, string workDir, CancellationToken ct)
    {
        var unpacked = Path.Combine(workDir, "unpacked");
        Directory.CreateDirectory(unpacked);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, unpacked, overwriteFiles: true);
        }
        else
        {
            // Unlike the ffmpeg tarballs these are gzip, which .NET reads without shelling out.
            await using var file = File.OpenRead(archivePath);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzip, unpacked, overwriteFiles: true, ct);
        }

        return Directory
            .EnumerateFiles(unpacked, Installer.ExecutableName, SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// Installations made before single-file publishing left Skia, HarfBuzz and ANGLE beside the
    /// executable, and a self-extracting build loads those from disk in preference to its own. The
    /// mismatch only surfaces at first render, long after the update reported success.
    /// <returns>Null when the directory is clean, otherwise why it could not be.</returns>
    private static string? RemoveSupersededNatives()
    {
        string[] patterns = ["libSkiaSharp.*", "libHarfBuzzSharp.*", "av_libglesv2.*", "*.pdb"];

        foreach (var file in patterns.SelectMany(p => Directory.EnumerateFiles(AppPaths.AppDir, p)).ToList())
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"{Path.GetFileName(file)} is in use. Close mpv and try again.";
            }
        }

        return null;
    }

    private static void Swap(string source, string target)
    {
        var staged = target + ".new";
        var previous = target + ".old";

        File.Copy(source, staged, overwrite: true);
        SetExecutable(staged);
        ResignAdHoc(staged);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // A running executable cannot be overwritten on Windows but can be renamed out of the
            // way. Both moves are on one volume, so neither can leave a partial file behind.
            TryDelete(previous);
            File.Move(target, previous);

            try
            {
                File.Move(staged, target);
            }
            catch
            {
                File.Move(previous, target);
                throw;
            }

            return;
        }

        // Never write through the path of a running binary on Unix: Linux answers ETXTBSY, and
        // macOS caches code-signing state per inode, so an in-place overwrite can get later
        // launches killed. Renaming a new inode over the path leaves the running process on its own.
        File.Move(staged, target, overwrite: true);
    }

    /// The incoming binary carries the authoritative copy of the mpv script, so it writes its own
    /// rather than leaving the previous version's behind.
    private static bool RefreshLuaScript(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "install", "--lua-only", "--quiet" }
            });

            return process is not null
                   && process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds)
                   && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    /// Gatekeeper on macOS 26 kills binaries whose ad-hoc signature was made on another machine,
    /// so the release's CI signature must be replaced with one made here.
    private static void ResignAdHoc(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/codesign")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "--force", "--sign", "-", path }
            });
            process?.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // The CI signature stays in place; Macs that accept foreign ad-hoc signatures still run it.
        }
    }

    private static void SetExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// Must match the asset names produced by .github/workflows/release.yml.
    private static string? AssetName()
    {
        var arch = RuntimeInformation.OSArchitecture;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return arch == Architecture.X64 ? "jiten-mpv-win-x64.zip" : null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return arch == Architecture.X64 ? "jiten-mpv-linux-x64.tar.gz" : null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return arch switch
            {
                Architecture.X64 => "jiten-mpv-osx-x64.tar.gz",
                Architecture.Arm64 => "jiten-mpv-osx-arm64.tar.gz",
                _ => null
            };

        return null;
    }

    /// SHARING_VIOLATION / LOCK_VIOLATION on Windows, ETXTBSY on Unix.
    private static bool IsFileInUse(IOException ex)
        => ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
           || ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("Text file busy", StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still running, or not ours to remove; the next launch tries again.
        }
    }

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
