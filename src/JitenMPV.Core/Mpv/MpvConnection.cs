using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Mpv;

public sealed class MpvConnection(ILogger logger) : IAsyncDisposable
{
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _connected;

    public bool IsConnected => _connected;

    public async Task ConnectAsync(string pipePath, CancellationToken ct)
    {
        var delay = 100;
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                _stream = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? await ConnectNamedPipeAsync(pipePath, ct)
                    : await ConnectUnixSocketAsync(pipePath, ct);
                _connected = true;
                logger.LogInformation("Connected to mpv pipe: {Path}", pipePath);
                break;
            }
            catch (Exception ex) when (ex is TimeoutException or SocketException)
            {
                if (attempt >= 30) break;
                logger.LogDebug("Connection attempt {Attempt} failed, retrying in {Delay}ms", attempt, delay);
                await Task.Delay(delay, ct);
                delay = Math.Min(delay * 2, 5000);
            }
        }

        if (!_connected)
            throw new IOException($"Failed to connect to mpv pipe after 30 attempts: {pipePath}");

        _reader = new StreamReader(_stream!, leaveOpen: true);
        _writer = new StreamWriter(_stream!, leaveOpen: true) { AutoFlush = true };
    }

    private static async Task<Stream> ConnectNamedPipeAsync(string pipePath, CancellationToken ct)
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

        var pipe = new NamedPipeClientStream(serverName, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1000, ct);
        return pipe;
    }

    private static async Task<Stream> ConnectUnixSocketAsync(string socketPath, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(1000);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), timeoutCts.Token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException($"Connection to {socketPath} timed out");
        }
        catch
        {
            socket.Dispose();
            throw;
        }
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
        _connected = false;
        try { if (_writer is not null) await _writer.DisposeAsync(); } catch { }
        try { if (_reader is not null) _reader.Dispose(); } catch { }
        try { if (_stream is not null) await _stream.DisposeAsync(); } catch { }
    }
}
