using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

/// A scratch directory per capture, removed when the capture is disposed.
public sealed class MediaTempFiles : IDisposable
{
    private static readonly TimeSpan StaleAge = TimeSpan.FromHours(24);
    private static readonly string RootDir = Path.Combine(Path.GetTempPath(), "jiten-mpv");

    private readonly ILogger _logger;

    public MediaTempFiles(ILogger logger)
    {
        _logger = logger;
        Directory = Path.Combine(RootDir, Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public string PathFor(string fileName) => Path.Combine(Directory, fileName);

    /// Clears directories left behind by a crash, so temp does not grow without bound.
    public static void SweepStale(ILogger logger)
    {
        try
        {
            if (!System.IO.Directory.Exists(RootDir)) return;

            var cutoff = DateTime.UtcNow - StaleAge;
            foreach (var dir in System.IO.Directory.EnumerateDirectories(RootDir))
            {
                try
                {
                    if (System.IO.Directory.GetLastWriteTimeUtc(dir) < cutoff)
                        System.IO.Directory.Delete(dir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Could not sweep temp dir {Dir}", dir);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Temp sweep failed");
        }
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete capture temp dir {Dir}", Directory);
        }
    }
}
