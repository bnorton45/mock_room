using System.Security.Cryptography;

namespace LexCore.Client;

/// <summary>
/// ILicenseProvider implementation backed by the self-hosted LexCore server.
/// Security properties:
///   - TLS public key pinning via pinnedTlsKeyHash (SHA-256 of server SPKI)
///   - All server calls carry nonce + timestamp + HMAC signature (replay protection)
///   - Activation secret stored in OS keyring / DPAPI (token extraction resistance)
///   - Server time used for all expiry checks (clock manipulation resistance)
///   - VM detection passed to server (trial reset via VM resistance)
///   - Reinstalling the app changes nothing — trial state is server-side
/// </summary>
public sealed class LexCoreProvider : ILicenseProvider, IDisposable
{
    private readonly LexCoreClient _client;
    private readonly SecureTokenStore _store;
    private readonly ClockDriftGuard _driftGuard;
    private readonly string _productId;
    private LicenseStatus _status = LicenseStatus.Unknown;

    public LexCoreProvider(string serverBaseUrl, string productId, byte[] pinnedTlsKeyHash)
    {
        _productId = productId;
        _driftGuard = new ClockDriftGuard();
        _client = new LexCoreClient(serverBaseUrl, pinnedTlsKeyHash, _driftGuard);
        _store = new SecureTokenStore(productId);

        var saved = _store.Load();
        if (saved is not null)
            _client.SetActivationSecret(saved.ActivationSecret);
    }

    public LicenseStatus Status => _status;

    public async Task<LicenseResult> ActivateAsync(string licenseKey, CancellationToken ct = default)
    {
        if (!_driftGuard.IsClockValid())
            return LicenseResult.Fail(LicenseStatus.Invalid, "System clock appears manipulated.");

        var fp = MachineFingerprint.Compute(_productId);
        var isVm = VmDetector.IsVirtualMachine();
        var machineName = Environment.MachineName;

        var resp = await _client.ActivateAsync(
            licenseKey, fp.Hash, fp.ComponentsJson, machineName, isVm, ct);

        if (resp is null)
            return LicenseResult.Fail(LicenseStatus.Invalid, "Activation failed — server unreachable or key rejected.");

        _driftGuard.RecordServerTime(resp.ServerTime);

        if (!_driftGuard.IsClockValid(resp.ServerTime))
            return LicenseResult.Fail(LicenseStatus.Invalid, "System clock drift detected.");

        _status = LicenseStatus.Active;
        _store.Save(new ActivationState(
            resp.ActivationId,
            resp.ActivationSecret,
            resp.LicenseExpiresAt,
            resp.ServerTime,
            DateTime.UtcNow));
        _client.SetActivationSecret(resp.ActivationSecret);

        return LicenseResult.Ok(LicenseStatus.Active);
    }

    public async Task<LicenseResult> ValidateAsync(CancellationToken ct = default)
    {
        if (!_driftGuard.IsClockValid())
            return LicenseResult.Fail(LicenseStatus.Invalid, "System clock appears manipulated.");

        var saved = _store.Load();
        if (saved is null)
            return LicenseResult.Fail(LicenseStatus.Inactive, "No active license found on this machine.");

        // Use cached server time if recent enough
        if (!_driftGuard.CacheIsStale && !_driftGuard.IsClockValid())
            return LicenseResult.Fail(LicenseStatus.Invalid, "System clock drift detected.");

        var fp = MachineFingerprint.Compute(_productId);
        var resp = await _client.ValidateAsync(saved.ActivationId, fp.Hash, ct);

        if (resp is null)
        {
            // Offline grace: allow up to 24 h without server contact
            var gracePeriod = TimeSpan.FromHours(24);
            if (DateTime.UtcNow - saved.LastSyncAt < gracePeriod && saved.LicenseExpiresAt > DateTime.UtcNow)
            {
                _status = LicenseStatus.Active;
                return LicenseResult.Ok(LicenseStatus.Active);
            }
            _status = LicenseStatus.Invalid;
            return LicenseResult.Fail(LicenseStatus.Invalid, "Cannot reach license server.");
        }

        _driftGuard.RecordServerTime(resp.ServerTime);
        if (!_driftGuard.IsClockValid(resp.ServerTime))
            return LicenseResult.Fail(LicenseStatus.Invalid, "System clock drift detected.");

        _status = resp.LicenseStatus switch
        {
            "Active" => LicenseStatus.Active,
            "Suspended" => LicenseStatus.Suspended,
            _ => LicenseStatus.Invalid,
        };

        if (_status == LicenseStatus.Active && resp.LicenseExpiresAt.HasValue &&
            resp.LicenseExpiresAt.Value < resp.ServerTime)
        {
            _status = LicenseStatus.Expired;
        }

        _store.Save(saved with { LastServerTime = resp.ServerTime, LastSyncAt = DateTime.UtcNow });

        return _status == LicenseStatus.Active
            ? LicenseResult.Ok(_status)
            : LicenseResult.Fail(_status, $"License status: {resp.LicenseStatus}");
    }

    public async Task<LicenseResult> DeactivateAsync(CancellationToken ct = default)
    {
        var saved = _store.Load();
        if (saved is null)
            return LicenseResult.Fail(LicenseStatus.Inactive, "No active license to deactivate.");

        var fp = MachineFingerprint.Compute(_productId);
        var ok = await _client.DeactivateAsync(saved.ActivationId, fp.Hash, ct);
        _store.Clear();
        _status = LicenseStatus.Inactive;
        return ok
            ? LicenseResult.Ok(LicenseStatus.Inactive)
            : LicenseResult.Fail(LicenseStatus.Inactive, "Deactivation request failed — local state cleared.");
    }

    public void Dispose() => _client.Dispose();
}
