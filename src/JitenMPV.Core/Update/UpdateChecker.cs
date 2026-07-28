using System.Net;
using System.Text.Json;
using JitenMPV.Core.Install;

namespace JitenMPV.Core.Update;

public sealed record UpdateInfo(string Version, string TagName)
{
    public string ReleaseNotesUrl => $"{UpdateChecker.ReleasesUrl}/tag/{TagName}";
}

/// <param name="Reachable">False when GitHub could not be asked, so a null <paramref name="Update"/>
/// means "unknown" rather than "up to date". Only a user-initiated check can tell the difference.</param>
public sealed record UpdateCheckResult(UpdateInfo? Update, bool Reachable);

/// Asks GitHub whether a newer release exists. Nothing is ever downloaded here; the user decides
/// that in the settings window.
public static class UpdateChecker
{
    public const string Repository = "Sirush/JitenMPV";

    public static string ReleasesUrl => $"https://github.com/{Repository}/releases";

    /// mpv can spawn the plugin a dozen times a day, and the answer changes at most weekly.
    private static readonly TimeSpan Throttle = TimeSpan.FromHours(24);

    private static readonly HttpClient Http = CreateClient();

    /// <returns>The newer release, or null when up to date, throttled, disabled or unreachable.</returns>
    public static async Task<UpdateInfo?> CheckAsync(bool enabled, CancellationToken ct)
    {
        if (!enabled) return null;

        var state = await UpdateState.LoadAsync(ct);

        if (state.LastCheckUtc is { } last && DateTimeOffset.UtcNow - last < Throttle)
            return Available(state);

        try
        {
            await RefreshAsync(state, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            // Offline, rate-limited or a body we could not read: keep whatever was known before.
            // The timestamp is still stamped, so a broken connection costs one request a day rather
            // than one per mpv launch.
            state.LastCheckUtc = DateTimeOffset.UtcNow;
        }

        await UpdateState.SaveAsync(state, ct);
        return Available(state);
    }

    /// The Check now button: ignores both the daily throttle and the enabled flag, because pressing
    /// it is the request.
    public static async Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct)
    {
        var state = await UpdateState.LoadAsync(ct);
        bool reachable;

        try
        {
            reachable = await RefreshAsync(state, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            state.LastCheckUtc = DateTimeOffset.UtcNow;
            reachable = false;
        }

        await UpdateState.SaveAsync(state, ct);
        return new UpdateCheckResult(Available(state), reachable);
    }

    /// <returns>False when the answer could not be trusted, so the caller can say so instead of
    /// reporting the stale known version as current.</returns>
    private static async Task<bool> RefreshAsync(UpdateState state, CancellationToken ct)
    {
        // This endpoint excludes drafts and prereleases by definition, so nothing here has to.
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");

        if (!string.IsNullOrEmpty(state.ETag))
            request.Headers.TryAddWithoutValidation("If-None-Match", state.ETag);

        using var response = await Http.SendAsync(request, ct);
        state.LastCheckUtc = DateTimeOffset.UtcNow;

        // 304 means the cached answer is still the right one, which is a successful check.
        if (response.StatusCode == HttpStatusCode.NotModified) return true;
        if (!response.IsSuccessStatusCode) return false;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
        if (!doc.RootElement.TryGetProperty("tag_name", out var tag)) return false;
        if (tag.GetString() is not { Length: > 0 } tagName) return false;

        state.ETag = response.Headers.ETag?.ToString();
        state.KnownLatestTag = tagName;
        state.KnownLatestVersion = tagName.TrimStart('v', 'V');
        return true;
    }

    private static UpdateInfo? Available(UpdateState state)
        => state is { KnownLatestVersion: { } version, KnownLatestTag: { } tag }
           && IsNewer(version, Installer.CurrentVersion)
            ? new UpdateInfo(version, tag)
            : null;

    public static bool IsNewer(string candidate, string current)
    {
        if (!TryParse(candidate, out var candidateVersion, out var candidatePrerelease)) return false;
        if (!TryParse(current, out var currentVersion, out var currentPrerelease)) return false;

        var comparison = candidateVersion.CompareTo(currentVersion);
        if (comparison != 0) return comparison > 0;

        // Equal numbers: 1.2.3 supersedes the 1.2.3-beta that led to it, and never the reverse.
        return currentPrerelease && !candidatePrerelease;
    }

    private static bool TryParse(string text, out Version version, out bool prerelease)
    {
        version = new Version(0, 0, 0);
        prerelease = false;

        var trimmed = text.Trim().TrimStart('v', 'V');
        var suffix = trimmed.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            prerelease = trimmed[suffix] == '-';
            trimmed = trimmed[..suffix];
        }

        if (!Version.TryParse(trimmed, out var parsed)) return false;

        // Version treats an absent component as -1, which would rank "1.2" below "1.2.0".
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("jiten-mpv");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
