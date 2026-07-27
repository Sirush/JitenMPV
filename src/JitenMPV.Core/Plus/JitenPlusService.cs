using JitenMPV.Core.Api;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plus;

public enum JitenPlusTier { None = 0, Trial = 1, Full = 2 }

public sealed record JitenPlusSnapshot(
    JitenPlusTier Tier,
    long UsedBytes,
    long MaxBytes,
    DateTimeOffset FetchedAt,
    bool FromCache,
    string? Error)
{
    public bool IsActive => Tier > JitenPlusTier.None;
    public bool CanUpload => IsActive && MaxBytes > UsedBytes;

    public static JitenPlusSnapshot Unknown { get; } =
        new(JitenPlusTier.None, 0, 0, default, true, null);
}

/// Single source of truth for whether media capture is available. Every consumer reads
/// <see cref="Current"/>; nothing else calls the status endpoint.
public sealed class JitenPlusService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    /// A cached snapshot stops gating the UI after this; the server always has the final say on upload.
    private static readonly TimeSpan CacheGrace = TimeSpan.FromDays(7);

    private readonly JitenApiClient _api;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Timer? _timer;
    private volatile JitenPlusSnapshot _current;

    public JitenPlusService(JitenApiClient api, ILogger logger, bool loadCache = true)
    {
        _api = api;
        _logger = logger;
        _current = loadCache ? Age(JitenPlusCache.Load()) : JitenPlusSnapshot.Unknown;
    }

    public JitenPlusSnapshot Current => _current;
    public event Action<JitenPlusSnapshot>? StatusChanged;

    /// Refreshes now and every 6 h after. The status endpoint is rate-limited, so nothing else polls.
    public void StartPeriodicRefresh(CancellationToken ct)
    {
        _timer?.Dispose();
        _timer = new Timer(_ =>
        {
            if (ct.IsCancellationRequested) return;
            _ = TaskHelper.RunSafe(() => RefreshAsync(ct), _logger, "Jiten+ periodic refresh");
        }, null, TimeSpan.Zero, RefreshInterval);
    }

    public async Task<JitenPlusSnapshot> RefreshAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var status = await _api.GetJitenPlusStatusAsync(ct);
            var snapshot = new JitenPlusSnapshot(
                ParseTier(status?.Tier),
                status?.Quota?.UsedBytes ?? 0,
                status?.Quota?.MaxBytes ?? 0,
                DateTimeOffset.UtcNow,
                FromCache: false,
                Error: null);

            Publish(snapshot, persist: true);
            _logger.LogInformation("Jiten+ tier {Tier}, {Used}/{Max} bytes used",
                snapshot.Tier, snapshot.UsedBytes, snapshot.MaxBytes);
            return snapshot;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Jiten+ status check failed: {Message}", ex.Message);

            // The last known tier keeps gating the UI while it is inside the grace window; the
            // server rejects any upload the client wrongly believes is allowed.
            var kept = _current with { FromCache = true, Error = ex.Message };
            Publish(Age(kept), persist: false);
            return _current;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// Called by the upload path on a 403 carrying jitenPlus: true. Downgrades to None immediately so
    /// the session stops attempting uploads without waiting for the next scheduled refresh.
    public void MarkRevoked(string reason)
    {
        _logger.LogWarning("Jiten+ revoked: {Reason}", reason);
        Publish(_current with
        {
            Tier = JitenPlusTier.None,
            FetchedAt = DateTimeOffset.UtcNow,
            FromCache = false,
            Error = reason
        }, persist: true);
    }

    /// Applied after a successful upload so the settings quota readout does not lag a full refresh cycle.
    public void ApplyQuotaDelta(long deltaBytes)
    {
        if (deltaBytes == 0) return;
        Publish(_current with { UsedBytes = Math.Max(0, _current.UsedBytes + deltaBytes) }, persist: true);
    }

    /// Adopts the authoritative figures the upload response carries.
    public void ApplyQuota(long usedBytes, long maxBytes)
    {
        if (maxBytes <= 0) return;
        Publish(_current with { UsedBytes = usedBytes, MaxBytes = maxBytes }, persist: true);
    }

    private void Publish(JitenPlusSnapshot snapshot, bool persist)
    {
        _current = snapshot;
        if (persist) JitenPlusCache.Save(snapshot);
        StatusChanged?.Invoke(snapshot);
    }

    /// A snapshot nobody has been able to confirm for a week stops unlocking anything.
    private static JitenPlusSnapshot Age(JitenPlusSnapshot snapshot)
        => snapshot.Tier != JitenPlusTier.None
           && DateTimeOffset.UtcNow - snapshot.FetchedAt > CacheGrace
            ? snapshot with { Tier = JitenPlusTier.None }
            : snapshot;

    private static JitenPlusTier ParseTier(string? tier) => tier?.ToLowerInvariant() switch
    {
        "full" => JitenPlusTier.Full,
        "trial" => JitenPlusTier.Trial,
        _ => JitenPlusTier.None
    };

    public void Dispose()
    {
        _timer?.Dispose();
        _refreshLock.Dispose();
    }
}
