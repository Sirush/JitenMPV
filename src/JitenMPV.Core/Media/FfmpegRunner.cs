using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Media;

public sealed record FfmpegResult(int ExitCode, string Stderr)
{
    public bool Succeeded => ExitCode == 0;

    /// The tail of stderr, which is where ffmpeg puts the reason it gave up.
    public string ErrorTail => string.Join(
        '\n', Stderr.Split('\n').Where(l => l.Trim().Length > 0).TakeLast(5));
}

public sealed class FfmpegRunner(string ffmpegPath, ILogger logger)
{
    private static readonly string[] CommonArgs = ["-hide_banner", "-nostdin", "-y"];

    public async Task<FfmpegResult> RunAsync(
        IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var (result, _) = await ExecuteAsync(args, timeout, captureStdout: false, ct);
        return result;
    }

    public async Task<(FfmpegResult Result, byte[] Bytes)> RunCaptureStdoutAsync(
        IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
        => await ExecuteAsync(args, timeout, captureStdout: true, ct);

    private async Task<(FfmpegResult, byte[])> ExecuteAsync(
        IEnumerable<string> args, TimeSpan timeout, bool captureStdout, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in CommonArgs)
            proc.StartInfo.ArgumentList.Add(arg);
        foreach (var arg in args)
            proc.StartInfo.ArgumentList.Add(arg);

        logger.LogDebug("ffmpeg {Args}", string.Join(' ', proc.StartInfo.ArgumentList));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var token = timeoutCts.Token;

        proc.Start();

        // Both pipes must drain concurrently and before the exit wait: ffmpeg fills stderr and
        // blocks on the write, so waiting for exit first deadlocks.
        using var stdoutBuffer = new MemoryStream();
        var stdoutTask = captureStdout
            ? proc.StandardOutput.BaseStream.CopyToAsync(stdoutBuffer, token)
            : proc.StandardOutput.BaseStream.CopyToAsync(Stream.Null, token);
        var stderrTask = proc.StandardError.ReadToEndAsync(token);

        try
        {
            await proc.WaitForExitAsync(token);
            await stdoutTask;
            var stderr = await stderrTask;
            return (new FfmpegResult(proc.ExitCode, stderr), stdoutBuffer.ToArray());
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning("ffmpeg timed out after {Seconds:F0}s", timeout.TotalSeconds);
            return (new FfmpegResult(-1, "timed out"), []);
        }
    }

    private void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not kill ffmpeg");
        }
    }
}
