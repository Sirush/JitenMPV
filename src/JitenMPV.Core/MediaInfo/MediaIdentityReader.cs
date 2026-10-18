using JitenMPV.Core.Mpv;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.MediaInfo;

public static class MediaIdentityReader
{
    public static async Task<MpvMediaProperties?> ReadPropertiesAsync(
        MpvIpcClient ipc, ILogger logger, CancellationToken ct)
    {
        try
        {
            var path = await ipc.GetPropertyAsync<string>("path", ct);
            // The series folder needs an absolute path; mpv keeps the one it was given.
            if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path) && !ReleaseNameParser.IsUrl(path))
            {
                var workDir = await ipc.GetPropertyAsync<string>("working-directory", ct);
                if (!string.IsNullOrEmpty(workDir))
                    path = Path.Combine(workDir, path);
            }

            return new MpvMediaProperties(
                path,
                await ipc.GetPropertyAsync<string>("filename", ct),
                await ipc.GetPropertyAsync<string>("media-title", ct),
                await ipc.GetPropertyAsync<string>("title", ct),
                await ipc.GetPropertyAsync<string>("metadata/by-key/title", ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read mpv media properties");
            return null;
        }
    }

    public static async Task<MediaIdentity?> ResolveAsync(MpvIpcClient ipc, ILogger logger, CancellationToken ct)
        => await ReadPropertiesAsync(ipc, logger, ct) is { } props ? MediaIdentityResolver.Resolve(props) : null;
}
