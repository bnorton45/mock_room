namespace LexCore.Client;

/// <summary>
/// Detects clock manipulation by comparing local time against the server-provided
/// time in every response. If drift exceeds 5 minutes, validation fails.
/// </summary>
public sealed class ClockDriftGuard
{
    private static readonly TimeSpan MaxDrift = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(30);

    private DateTime _lastServerTime;
    private DateTime _lastSyncAt;

    public void RecordServerTime(DateTime serverTime)
    {
        _lastServerTime = serverTime;
        _lastSyncAt = DateTime.UtcNow;
    }

    public bool IsClockValid(DateTime? serverTime = null)
    {
        var reference = serverTime ?? GetCachedServerTime();
        if (reference == default) return true; // no reference yet — allow first call

        var drift = (DateTime.UtcNow - reference).Duration();
        return drift <= MaxDrift;
    }

    public bool CacheIsStale => _lastSyncAt == default ||
        (DateTime.UtcNow - _lastSyncAt) > CacheExpiry;

    private DateTime GetCachedServerTime()
    {
        if (_lastSyncAt == default) return default;
        // Project the cached server time forward by elapsed local time
        var elapsed = DateTime.UtcNow - _lastSyncAt;
        return _lastServerTime + elapsed;
    }
}
