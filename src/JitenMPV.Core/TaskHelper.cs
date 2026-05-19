using Microsoft.Extensions.Logging;

namespace JitenMPV.Core;

internal static class TaskHelper
{
    public static async Task RunSafe(Func<Task> action, ILogger logger, string context = "Background task")
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogError(ex, "{Context} failed", context); }
    }

    public static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
