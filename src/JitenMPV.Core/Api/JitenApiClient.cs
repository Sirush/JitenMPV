using System.Net.Http.Json;
using System.Text.Json;
using JitenMPV.Core.Api.Models;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Api;

public sealed class JitenApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public JitenApiClient(HttpClient http, string apiKey, ILogger logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    public async Task<ReaderParseResponse> ParseAsync(string text, CancellationToken ct)
    {
        var request = new ReaderParseRequest { Text = [text] };
        var response = await _http.PostAsJsonAsync("/api/reader/parse", request, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Parse API returned {Status}: {Body}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<ReaderParseResponse>(JsonOptions, ct);
        return result ?? new ReaderParseResponse();
    }
}
