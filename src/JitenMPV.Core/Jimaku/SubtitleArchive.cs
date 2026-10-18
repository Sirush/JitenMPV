using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Jimaku;

/// Unpacks the text subtitles of a downloaded archive. Zip is handled in-process; 7z and rar need
/// 7-Zip on the PATH.
public static class SubtitleArchive
{
    private static readonly Lazy<string?> SevenZip = new(() => FindTool("7z", "7zz", "7za"));

    public static bool CanExtract(string extension) => extension == "zip" || SevenZip.Value is not null;

    public static async Task<List<string>> ExtractAsync(
        string archive, string directory, ILogger logger, CancellationToken ct)
    {
        var existing = ListSubtitles(directory);
        if (existing.Count > 0) return existing;

        Directory.CreateDirectory(directory);
        if (JimakuFileRanker.ExtensionOf(archive) == "zip")
            ExtractZip(archive, directory);
        else
            await ExtractSevenZipAsync(archive, directory, logger, ct);

        return ListSubtitles(directory);
    }

    /// Archives packed on macOS carry "._" resource-fork twins of every subtitle.
    public static List<string> ListSubtitles(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => JimakuFileRanker.IsText(path)
                           && !Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal)
                           && !path.Contains("__MACOSX", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
    }

    private static void ExtractZip(string archive, string directory)
    {
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0 || !JimakuFileRanker.IsText(entry.Name)) continue;

            var target = Path.GetFullPath(Path.Combine(directory, entry.FullName.Replace('\\', '/')));
            if (!target.StartsWith(root, StringComparison.Ordinal)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static async Task ExtractSevenZipAsync(
        string archive, string directory, ILogger logger, CancellationToken ct)
    {
        var tool = SevenZip.Value
                   ?? throw new JimakuException(JimakuErrorKind.BadResponse, "Install 7-Zip to open this archive");

        var psi = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "x", "-y", $"-o{directory}", archive })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
                            ?? throw new JimakuException(JimakuErrorKind.BadResponse, $"Could not start {tool}");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0)
            logger.LogDebug("{Tool} exited {Code}: {Error}", tool, process.ExitCode, stderr.Result.Trim());
    }

    private static string? FindTool(params string[] names)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var name in names)
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? name + ".exe" : name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
