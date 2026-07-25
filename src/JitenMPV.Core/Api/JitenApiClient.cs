using System.Net.Http.Json;
using System.Text.Json;
using JitenMPV.Core.Api.Models;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Api;

public sealed class JitenApiClient
{
    // DTOs carry explicit [JsonPropertyName] (camelCase), so no naming policy is needed here.
    private static readonly JsonSerializerOptions JsonOptions = new();

    /// Shared and never disposed: UpdateConnection swaps clients while requests may still be in
    /// flight, and disposing the client that owns the handler would abort them.
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    private volatile HttpClient _http;
    private readonly ILogger _logger;

    public JitenApiClient(string? apiKey, string baseUrl, int timeoutSeconds, ILogger logger)
    {
        _logger = logger;
        _http = BuildClient(apiKey, baseUrl, timeoutSeconds);
    }

    public void UpdateConnection(string? apiKey, string baseUrl, int timeoutSeconds)
        => _http = BuildClient(apiKey, baseUrl, timeoutSeconds);

    private static HttpClient BuildClient(string? apiKey, string baseUrl, int timeoutSeconds)
    {
        var http = new HttpClient(SharedHandler, disposeHandler: false)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        if (!string.IsNullOrEmpty(apiKey))
            http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return http;
    }

    public async Task<ReaderParseResponse> ParseAsync(string text, CancellationToken ct)
        => await ParseBatchAsync([text], ct);

    public async Task<ReaderParseResponse> ParseBatchAsync(string[] texts, CancellationToken ct)
    {
        var request = new ReaderParseRequest { Text = texts };
        var response = await _http.PostAsJsonAsync("/api/reader/parse", request, JsonOptions, ct);
        await EnsureSuccessAsync(response, "Parse", ct);

        var result = await response.Content.ReadFromJsonAsync<ReaderParseResponse>(JsonOptions, ct);
        return result ?? new ReaderParseResponse();
    }

    public async Task<SetVocabularyStateResponse> SetVocabularyStateAsync(
        int wordId, byte readingIndex, string stateAction, CancellationToken ct)
    {
        var request = new SetVocabularyStateRequest
        {
            WordId = wordId, ReadingIndex = readingIndex, State = stateAction
        };
        var response = await _http.PostAsJsonAsync("/api/srs/set-vocabulary-state", request, JsonOptions, ct);
        await EnsureSuccessAsync(response, "SetVocabularyState", ct);

        return await response.Content.ReadFromJsonAsync<SetVocabularyStateResponse>(JsonOptions, ct)
               ?? new SetVocabularyStateResponse();
    }

    public async Task<ReviewResponse> ReviewAsync(
        int wordId, byte readingIndex, int rating, CancellationToken ct)
    {
        var request = new ReviewRequest { WordId = wordId, ReadingIndex = readingIndex, Rating = rating };
        var response = await _http.PostAsJsonAsync("/api/srs/review", request, JsonOptions, ct);
        await EnsureSuccessAsync(response, "Review", ct);

        return await response.Content.ReadFromJsonAsync<ReviewResponse>(JsonOptions, ct)
               ?? new ReviewResponse();
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string label, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogError("{Label} API returned {Status}: {Body}", label, response.StatusCode, body);
        response.EnsureSuccessStatusCode();
    }
}
