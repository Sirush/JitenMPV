using System.Diagnostics;

namespace JitenMPV.Core.Net;

/// Runs each request through the system curl instead of SslStream: on macOS 26 both in-process
/// TLS backends (SecureTransport and Network.framework) can die with a native access violation
/// during the handshake, which no managed code can catch; a curl child process keeps that crash
/// out of the plugin.
public sealed class CurlHttpHandler : HttpMessageHandler
{
    internal const string CurlPath = "/usr/bin/curl";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var headerFile = Path.GetTempFileName();
        var bodyFile = Path.GetTempFileName();
        string? requestBodyFile = null;
        var bodyHandedOff = false;

        try
        {
            var psi = new ProcessStartInfo(CurlPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            psi.ArgumentList.Add("--silent");
            psi.ArgumentList.Add("--show-error");
            // Release downloads redirect to GitHub's CDN.
            psi.ArgumentList.Add("--location");
            psi.ArgumentList.Add("--request");
            psi.ArgumentList.Add(request.Method.Method);
            psi.ArgumentList.Add("--dump-header");
            psi.ArgumentList.Add(headerFile);
            psi.ArgumentList.Add("--output");
            psi.ArgumentList.Add(bodyFile);
            // curl volunteers Expect: 100-continue for large bodies, which HttpClient never does.
            psi.ArgumentList.Add("--header");
            psi.ArgumentList.Add("Expect:");

            foreach (var (name, values) in request.Headers)
            {
                psi.ArgumentList.Add("--header");
                psi.ArgumentList.Add($"{name}: {string.Join(", ", values)}");
            }

            if (request.Content is { } content)
            {
                // curl derives framing from the body it is given; a forwarded Content-Length that
                // disagreed with the serialized bytes would hang or truncate the request.
                foreach (var (name, values) in content.Headers.Where(h =>
                             !h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                             !h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)))
                {
                    psi.ArgumentList.Add("--header");
                    psi.ArgumentList.Add($"{name}: {string.Join(", ", values)}");
                }

                requestBodyFile = Path.GetTempFileName();
                await using (var f = File.Create(requestBodyFile))
                    await content.CopyToAsync(f, ct);

                psi.ArgumentList.Add("--data-binary");
                psi.ArgumentList.Add("@" + requestBodyFile);
            }

            psi.ArgumentList.Add(request.RequestUri!.AbsoluteUri);

            using var process = Process.Start(psi)
                ?? throw new HttpRequestException("curl could not be started.");
            await using var killOnCancel = ct.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            });

            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
                throw new HttpRequestException(
                    $"curl exited with {process.ExitCode}: {(await stderrTask).Trim()}");

            var headerLines = ParseFinalHeaderBlock(await File.ReadAllTextAsync(headerFile, ct));
            var response = ParseStatusLine(headerLines.FirstOrDefault());

            // The response stream owns the body file and removes it when the caller disposes it.
            response.Content = new StreamContent(new FileStream(
                bodyFile, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose));
            bodyHandedOff = true;

            CopyHeaders(headerLines, response);
            response.RequestMessage = request;
            return response;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            throw new HttpRequestException($"curl request failed: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(headerFile);
            if (!bodyHandedOff) TryDelete(bodyFile);
            if (requestBodyFile is not null) TryDelete(requestBodyFile);
        }
    }

    /// <returns>A response carrying only the status line; headers are copied separately.</returns>
    private static HttpResponseMessage ParseStatusLine(string? statusLine)
    {
        if (statusLine is null)
            throw new HttpRequestException("curl returned no response headers.");

        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var status))
            throw new HttpRequestException($"Unrecognized status line from curl: {statusLine}");

        return new HttpResponseMessage((System.Net.HttpStatusCode)status)
        {
            Version = parts[0].Replace("HTTP/", "") switch
            {
                "2" or "2.0" => new Version(2, 0),
                "3" or "3.0" => new Version(3, 0),
                "1.0" => new Version(1, 0),
                _ => new Version(1, 1)
            },
            ReasonPhrase = parts.Length == 3 ? parts[2] : null
        };
    }

    /// With --location the dump contains one block per hop; only the final one describes the
    /// response the caller gets.
    private static List<string> ParseFinalHeaderBlock(string headerText)
    {
        var blocks = headerText.Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return blocks.Length == 0
            ? []
            : [.. blocks[^1].Split('\n', StringSplitOptions.RemoveEmptyEntries)];
    }

    private static void CopyHeaders(List<string> headerLines, HttpResponseMessage response)
    {
        foreach (var line in headerLines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            // curl already decoded the transfer; advertising chunking again would be a lie.
            if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;

            if (!response.Headers.TryAddWithoutValidation(name, value))
                response.Content.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
