using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JitenMPV.Core.Api.Models;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Api;

public sealed class JitenApiClient
{
    // DTOs carry explicit [JsonPropertyName] (camelCase), so no naming policy is needed here.
    private static readonly JsonSerializerOptions JsonOptions = new();

    private const int MaxAttempts = 3;
    private const int InitialBackoffMs = 500;

    /// Shared and never disposed: UpdateConnection swaps clients while requests may still be in
    /// flight, and disposing the client that owns the handler would abort them.
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    /// A 4 MB animation on a slow uplink outlives the shared client's timeout, which is a hard cap
    /// no CancellationToken can extend, so the media path needs a client of its own.
    private const int UploadTimeoutSeconds = 120;

    private volatile HttpClient _http;
    private volatile HttpClient _uploadHttp;
    private readonly ILogger _logger;

    /// Latches the key the server rejected so later calls fail fast instead of hammering the API.
    /// Cleared by a successful response or by a key change.
    private volatile string? _rejectedApiKey;
    private volatile string? _apiKey;

    public JitenApiClient(string? apiKey, string baseUrl, int timeoutSeconds, ILogger logger)
    {
        _logger = logger;
        _apiKey = apiKey;
        _http = BuildClient(apiKey, baseUrl, timeoutSeconds);
        _uploadHttp = BuildClient(apiKey, baseUrl, UploadTimeoutSeconds);
    }

    public bool IsApiKeyRejected => _rejectedApiKey is not null && _rejectedApiKey == _apiKey;

    public void UpdateConnection(string? apiKey, string baseUrl, int timeoutSeconds)
    {
        _apiKey = apiKey;
        if (_rejectedApiKey != apiKey)
            _rejectedApiKey = null;
        _http = BuildClient(apiKey, baseUrl, timeoutSeconds);
        _uploadHttp = BuildClient(apiKey, baseUrl, UploadTimeoutSeconds);
    }

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
        return await PostAsync<ReaderParseRequest, ReaderParseResponse>(
            "/api/reader/parse", request, "Parse", ct) ?? new ReaderParseResponse();
    }

    public async Task<SetVocabularyStateResponse> SetVocabularyStateAsync(
        int wordId, byte readingIndex, string stateAction, CancellationToken ct)
    {
        var request = new SetVocabularyStateRequest
        {
            WordId = wordId, ReadingIndex = readingIndex, State = stateAction
        };
        return await PostAsync<SetVocabularyStateRequest, SetVocabularyStateResponse>(
            "/api/srs/set-vocabulary-state", request, "SetVocabularyState", ct)
            ?? new SetVocabularyStateResponse();
    }

    public async Task<ReviewResponse> ReviewAsync(
        int wordId, byte readingIndex, int rating, CancellationToken ct)
    {
        var request = new ReviewRequest { WordId = wordId, ReadingIndex = readingIndex, Rating = rating };
        return await PostAsync<ReviewRequest, ReviewResponse>(
            "/api/srs/review", request, "Review", ct) ?? new ReviewResponse();
    }

    public async Task<BatchReviewResponse> BatchReviewAsync(
        IReadOnlyList<BatchReviewItem> reviews, CancellationToken ct)
    {
        if (reviews.Count == 0) return new BatchReviewResponse { Success = true };

        var request = new BatchReviewRequest { Reviews = reviews };
        return await PostAsync<BatchReviewRequest, BatchReviewResponse>(
            "/api/srs/batch-review", request, "BatchReview", ct) ?? new BatchReviewResponse();
    }

    public async Task<List<StudyDeckListItem>> GetStudyDecksAsync(CancellationToken ct)
        => await PostAsync<object, List<StudyDeckListItem>>(
            "/api/srs/reader-study-decks", new object(), "StudyDecks", ct) ?? [];

    public async Task AddToStudyDeckAsync(
        int deckId, int wordId, byte readingIndex, string? sentence, string? source, CancellationToken ct)
    {
        var request = new AddToStudyDeckRequest
        {
            WordId = wordId, ReadingIndex = readingIndex, Sentence = sentence, Source = source
        };
        using var response = await SendWithRetryAsync(
            (http, token) => http.PostAsJsonAsync(
                $"/api/srs/study-decks/{deckId}/words", request, JsonOptions, token),
            "AddToStudyDeck", ct);
        await EnsureSuccessAsync(response, "AddToStudyDeck", ct);
    }

    /// Not exercised by the mining flow, which attaches the sentence via AddToStudyDeckAsync.
    public async Task AddExampleSentenceAsync(
        int wordId, byte readingIndex, string text, string? source, CancellationToken ct)
    {
        var request = new AddExampleSentenceRequest { Text = text, Source = source };
        using var response = await SendWithRetryAsync(
            (http, token) => http.PostAsJsonAsync(
                $"/api/user/example-sentences/{wordId}/{readingIndex}", request, JsonOptions, token),
            "AddExampleSentence", ct);
        await EnsureSuccessAsync(response, "AddExampleSentence", ct);
    }

    public async Task<List<VocabularyLookup>> LookupVocabularyAsync(
        IReadOnlyList<(int WordId, byte ReadingIndex)> words, CancellationToken ct)
    {
        if (words.Count == 0) return [];

        var request = new LookupVocabularyRequest
        {
            Words = [..words.Select(w => new[] { w.WordId, w.ReadingIndex })]
        };
        var result = await PostAsync<LookupVocabularyRequest, LookupVocabularyResponse>(
            "/api/reader/lookup-vocabulary", request, "LookupVocabulary", ct);

        // The response carries no ids, so entry i must be matched to request word i by position.
        return [..words.Select((w, i) => new VocabularyLookup(
            w.WordId,
            w.ReadingIndex,
            MapStates(ElementAtOrNull(result?.Result, i)),
            ElementAtOrNull(result?.Decks, i) ?? []))];
    }

    /// Validates a key without consulting the latch, so a corrected key can always be retried.
    public async Task<bool> PingAsync(string apiKey, string baseUrl, int timeoutSeconds, CancellationToken ct)
    {
        using var http = BuildClient(apiKey, baseUrl, timeoutSeconds);
        try
        {
            using var response = await http.PostAsJsonAsync("/api/reader/ping", new object(), JsonOptions, ct);
            if (response.IsSuccessStatusCode && apiKey == _rejectedApiKey)
                _rejectedApiKey = null;
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ping failed");
            return false;
        }
    }

    private static List<int>? ElementAtOrNull(List<List<int>>? source, int index)
        => source is not null && index < source.Count ? source[index] : null;

    private static IReadOnlyList<KnownState> MapStates(List<int>? states)
    {
        if (states is null || states.Count == 0) return [KnownState.New];

        var mapped = states
            .Where(s => Enum.IsDefined(typeof(KnownState), s))
            .Select(s => (KnownState)s)
            .ToList();

        return mapped.Count > 0 ? mapped : [KnownState.New];
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string path, TRequest request, string label, CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(
            (http, token) => http.PostAsJsonAsync(path, request, JsonOptions, token), label, ct);
        await EnsureSuccessAsync(response, label, ct);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
    }

    private async Task<TResponse?> GetAsync<TResponse>(string path, string label, CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(
            (http, token) => http.GetAsync(path, token), label, ct);
        await EnsureSuccessAsync(response, label, ct);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
    }

    public Task<JitenPlusStatusResponse?> GetJitenPlusStatusAsync(CancellationToken ct)
        => GetAsync<JitenPlusStatusResponse>("/api/jiten-plus/status", "JitenPlusStatus", ct);

    public async Task<CardMediaEntry?> GetCardMediaAsync(int wordId, byte readingIndex, CancellationToken ct)
    {
        var request = new CardMediaBatchRequest
        {
            Items = [new CardMediaKey { WordId = wordId, ReadingIndex = readingIndex }]
        };
        var response = await PostAsync<CardMediaBatchRequest, CardMediaBatchResponse>(
            "/api/srs/card-media/batch", request, "CardMediaBatch", ct);
        return response?.Items.FirstOrDefault();
    }

    /// <param name="fileName">Only informs the server's logging; the kind is sniffed from the bytes.</param>
    public async Task<CardMediaUploadResult> UploadCardMediaAsync(
        int wordId, byte readingIndex, byte[] bytes, string fileName, string contentType,
        CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            // The retry helper re-invokes this callback per attempt and multipart content cannot be
            // replayed, so the whole body is rebuilt inside the lambda.
            response = await SendWithRetryAsync((http, token) =>
            {
                var content = new MultipartFormDataContent();
                var part = new ByteArrayContent(bytes);
                part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                content.Add(part, "file", fileName);
                return http.PostAsync($"/api/srs/card-media/{wordId}/{readingIndex}", content, token);
            }, "UploadCardMedia", ct, useUploadClient: true);
        }
        catch (JitenPlusRequiredException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not JitenApiKeyRejectedException)
        {
            _logger.LogError(ex, "Card media upload failed");
            return CardMediaUploadResult.Failed(ex.Message);
        }

        using (response)
        {
            var body = await ReadBodySafeAsync(response, ct);

            if (response.IsSuccessStatusCode)
            {
                var parsed = TryDeserialize<CardMediaUploadResponse>(body);
                return CardMediaUploadResult.Success(
                    parsed?.Media?.FileSizeBytes ?? bytes.Length,
                    parsed?.Quota?.UsedBytes ?? 0,
                    parsed?.Quota?.MaxBytes ?? 0);
            }

            var (error, usedBytes, maxBytes) = ReadErrorPayload(body);
            _logger.LogError("Card media upload returned {Status}: {Body}", response.StatusCode, body);

            // The quota rejection carries used/max so the OSD can name the ceiling that was hit.
            return response.StatusCode == HttpStatusCode.BadRequest && maxBytes > 0
                ? CardMediaUploadResult.QuotaExceeded(usedBytes, maxBytes, error)
                : CardMediaUploadResult.Rejected(error ?? response.StatusCode.ToString());
        }
    }

    private static T? TryDeserialize<T>(string body) where T : class
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonSerializer.Deserialize<T>(body, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static (string? Error, long UsedBytes, long MaxBytes) ReadErrorPayload(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, 0, 0);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, 0, 0);

            var error = doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null;
            var used = doc.RootElement.TryGetProperty("usedBytes", out var u) && u.TryGetInt64(out var uv) ? uv : 0;
            var max = doc.RootElement.TryGetProperty("maxBytes", out var m) && m.TryGetInt64(out var mv) ? mv : 0;
            return (error, used, max);
        }
        catch (JsonException)
        {
            return (null, 0, 0);
        }
    }

    /// Retries transient failures (network, 429, 5xx) with jittered exponential backoff. The send
    /// callback must build a fresh request each attempt, since request content cannot be replayed.
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> send,
        string label, CancellationToken ct, bool useUploadClient = false)
    {
        if (IsApiKeyRejected)
            throw new JitenApiKeyRejectedException();

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await send(useUploadClient ? _uploadHttp : _http, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                if (attempt >= MaxAttempts - 1)
                {
                    _logger.LogError(ex, "{Label} failed after {Attempts} attempts", label, MaxAttempts);
                    throw;
                }
                await DelayBackoffAsync(attempt, ct);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                var status = response.StatusCode;
                var body = await ReadBodySafeAsync(response, ct);
                response.Dispose();

                // A Jiten+ gate refuses the feature, not the credential: latching here would take
                // parsing, mining and reviews down with the upload that hit the gate.
                if (status == HttpStatusCode.Forbidden && TryReadJitenPlusGate(body, out var gateMessage))
                {
                    _logger.LogWarning("{Label} refused: Jiten+ required", label);
                    throw new JitenPlusRequiredException(gateMessage);
                }

                _logger.LogError("{Label} rejected the API key ({Status})", label, status);
                _rejectedApiKey = _apiKey;
                throw new JitenApiKeyRejectedException();
            }

            if (IsRetryable(response.StatusCode) && attempt < MaxAttempts - 1)
            {
                response.Dispose();
                await DelayBackoffAsync(attempt, ct);
                continue;
            }

            if (response.IsSuccessStatusCode)
                _rejectedApiKey = null;

            return response;
        }
    }

    private static async Task<string> ReadBodySafeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ""; }
    }

    /// Fail-closed: an unparseable body is not a Jiten+ gate, so the key still latches as rejected.
    private static bool TryReadJitenPlusGate(string body, out string message)
    {
        message = "";
        if (string.IsNullOrWhiteSpace(body)) return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("jitenPlus", out var flag)
                || flag.ValueKind != JsonValueKind.True)
                return false;

            message = doc.RootElement.TryGetProperty("message", out var msg)
                      && msg.ValueKind == JsonValueKind.String
                ? msg.GetString() ?? "Jiten+ required"
                : "Jiten+ required";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsRetryable(HttpStatusCode status)
        => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static Task DelayBackoffAsync(int attempt, CancellationToken ct)
    {
        var backoff = InitialBackoffMs * (1 << attempt);
        var jitter = Random.Shared.NextDouble() * backoff * 0.5;
        return Task.Delay(TimeSpan.FromMilliseconds(backoff + jitter), ct);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string label, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogError("{Label} API returned {Status}: {Body}", label, response.StatusCode, body);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class JitenApiKeyRejectedException()
    : Exception("The jiten.moe API key was rejected. Update it in the settings window.");

/// The account lacks the Jiten+ tier the endpoint requires. Distinct from a rejected key: the
/// credential is fine and every non-gated call must keep working.
public sealed class JitenPlusRequiredException(string message) : Exception(message);
