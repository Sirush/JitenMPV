using System.Globalization;
using System.Net;
using System.Text.Json;
using JitenMPV.Core.Net;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Jimaku;

public enum JimakuErrorKind
{
    MissingKey,
    Unauthorized,
    RateLimited,
    NotFound,
    Network,
    BadResponse
}

public sealed class JimakuException(JimakuErrorKind kind, string message, TimeSpan? retryAfter = null)
    : Exception(message)
{
    public JimakuErrorKind Kind { get; } = kind;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// The key goes in a bare Authorization header, and only to the API host: download URLs need no key
/// and could point anywhere.
public sealed class JimakuClient
{
    public const string DefaultBaseUrl = "https://jimaku.cc/api";

    private static readonly JsonSerializerOptions JsonOptions = new();

    /// Shared and never disposed, as in JitenApiClient: a key change rebuilds the client while a
    /// request may still be using the handler.
    private static readonly Lazy<HttpMessageHandler> SharedHandler = new(JitenHttp.CreateHandler);

    private const int TimeoutSeconds = 30;

    private const long MaxDownloadBytes = 64L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string? _apiKey;
    private readonly ILogger _logger;

    public JimakuClient(string? apiKey, ILogger logger, HttpMessageHandler? handler = null,
        string baseUrl = DefaultBaseUrl)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim().ReplaceLineEndings("");
        _logger = logger;
        _baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        _http = new HttpClient(handler ?? SharedHandler.Value, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
    }

    public bool HasKey => _apiKey is not null;

    public Task<List<JimakuEntry>> SearchAsync(string query, bool anime, CancellationToken ct)
        => GetJsonAsync<List<JimakuEntry>>(
            $"entries/search?anime={(anime ? "true" : "false")}&query={Uri.EscapeDataString(query)}", ct);

    /// Any authenticated request tells whether the key works; one entry is the smallest answer, and
    /// a missing entry still means the key was accepted.
    public async Task CheckKeyAsync(CancellationToken ct)
    {
        try
        {
            await GetJsonAsync<JimakuEntry>("entries/1", ct);
        }
        catch (JimakuException ex) when (ex.Kind == JimakuErrorKind.NotFound)
        {
        }
    }

    public Task<List<JimakuFile>> GetFilesAsync(int entryId, int? episode, CancellationToken ct)
    {
        var path = $"entries/{entryId.ToString(CultureInfo.InvariantCulture)}/files";
        if (episode is { } ep)
            path += $"?episode={ep.ToString(CultureInfo.InvariantCulture)}";
        return GetJsonAsync<List<JimakuFile>>(path, ct);
    }

    /// Writes through a temp file, so an interrupted download cannot leave a truncated subtitle the
    /// cache would later reuse.
    public async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new JimakuException(JimakuErrorKind.BadResponse, $"Unusable download URL: {url}");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (uri.Host == _baseUri.Host)
            AddKey(request);

        using var response = await SendAsync(request, ct);
        await ThrowForStatusAsync(response, ct);

        if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            throw new JimakuException(JimakuErrorKind.BadResponse, "Subtitle file is too large");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = $"{destination}.{Environment.ProcessId}.part";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(temp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                        throw new JimakuException(JimakuErrorKind.BadResponse, "Subtitle file is too large");
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    private async Task<T> GetJsonAsync<T>(string relative, CancellationToken ct) where T : new()
    {
        if (_apiKey is null)
            throw new JimakuException(JimakuErrorKind.MissingKey, "No Jimaku API key set");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, relative));
        AddKey(request);

        using var response = await SendAsync(request, ct);
        await ThrowForStatusAsync(response, ct);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct) ?? new T();
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Jimaku returned invalid JSON for {Path}", relative);
            throw new JimakuException(JimakuErrorKind.BadResponse, "Jimaku returned invalid JSON");
        }
    }

    private void AddKey(HttpRequestMessage request)
    {
        if (_apiKey is not null)
            request.Headers.TryAddWithoutValidation("Authorization", _apiKey);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new JimakuException(JimakuErrorKind.Network, $"Could not reach jimaku.cc: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new JimakuException(JimakuErrorKind.Network, "jimaku.cc did not answer in time");
        }
    }

    private static async Task ThrowForStatusAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new JimakuException(JimakuErrorKind.Unauthorized, "Jimaku refused the API key");
            case HttpStatusCode.TooManyRequests:
                throw new JimakuException(JimakuErrorKind.RateLimited, "Jimaku rate limit reached",
                    RetryAfterOf(response));
            case HttpStatusCode.NotFound:
                throw new JimakuException(JimakuErrorKind.NotFound, "Not found on Jimaku");
        }

        var body = "";
        try { body = await response.Content.ReadAsStringAsync(ct); }
        catch (HttpRequestException) { }
        throw new JimakuException(JimakuErrorKind.BadResponse,
            $"Jimaku answered {(int)response.StatusCode}{(body.Length is > 0 and < 200 ? ": " + body : "")}");
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } retry)
        {
            if (retry.Delta is { } delta) return delta;
            if (retry.Date is { } date) return Clamp(date - DateTimeOffset.UtcNow);
        }

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values)
            && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var reset))
            return Clamp(DateTimeOffset.FromUnixTimeMilliseconds((long)(reset * 1000)) - DateTimeOffset.UtcNow);

        return null;
    }

    private static TimeSpan Clamp(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
