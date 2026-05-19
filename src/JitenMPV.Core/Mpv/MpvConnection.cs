using System.IO.Pipes;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Mpv;

public sealed class MpvConnection(ILogger logger) : IAsyncDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(string pipePath, CancellationToken ct)
    {
        var pipeName = pipePath;
        var serverName = ".";

        if (pipePath.StartsWith(@"\\.\pipe\"))
            pipeName = pipePath[@"\\.\pipe\".Length..];
        else if (pipePath.StartsWith(@"\\"))
        {
            var parts = pipePath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                serverName = parts[0];
                pipeName = parts[^1];
            }
        }

        _pipe = new NamedPipeClientStream(serverName, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var delay = 100;
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                await _pipe.ConnectAsync(1000, ct);
                logger.LogInformation("Connected to mpv pipe: {Path}", pipePath);
                break;
            }
            catch (TimeoutException) when (attempt < 30)
            {
                logger.LogDebug("Connection attempt {Attempt} timed out, retrying in {Delay}ms", attempt, delay);
                await Task.Delay(delay, ct);
                delay = Math.Min(delay * 2, 5000);
            }
            catch (TimeoutException)
            {
                throw new IOException($"Failed to connect to mpv pipe after {attempt} attempts: {pipePath}");
            }
        }

        if (!_pipe.IsConnected)
            throw new IOException($"Failed to connect to mpv pipe: {pipePath}");

        _reader = new StreamReader(_pipe, leaveOpen: true);
        _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
    }

    public async Task SendLineAsync(string line, CancellationToken ct)
    {
        if (_writer is null)
            throw new InvalidOperationException("Not connected");

        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (_reader is null)
            throw new InvalidOperationException("Not connected");

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _reader.ReadLineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (line is null)
            {
                logger.LogInformation("mpv pipe closed");
                yield break;
            }

            if (line.Length > 0)
                yield return line;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_writer is not null) await _writer.DisposeAsync(); } catch { }
        try { if (_reader is not null) _reader.Dispose(); } catch { }
        try { if (_pipe is not null) await _pipe.DisposeAsync(); } catch { }
    }
}
