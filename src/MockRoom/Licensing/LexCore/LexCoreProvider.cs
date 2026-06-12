using ClientProvider = global::LexCore.Client.LexCoreProvider;
using ClientStatus = global::LexCore.Client.LicenseStatus;

namespace MockRoom.Licensing.LexCore;

/// <summary>
/// Adapts LexCore.Client.LexCoreProvider to the MockRoom ILicenseProvider contract.
/// Configure serverBaseUrl, productId, and pinnedTlsKeyHash in App.axaml.cs or Program.cs.
///
/// pinnedTlsKeyHash: SHA-256 of your LexCore server's TLS public key (SubjectPublicKeyInfo).
/// Obtain with: openssl s_client -connect yourserver:443 | openssl x509 -pubkey -noout |
///              openssl pkey -pubin -outform DER | sha256sum
/// </summary>
public sealed class LexCoreProvider(string serverBaseUrl, string productId, byte[] pinnedTlsKeyHash)
    : ILicenseProvider, IDisposable
{
    private readonly ClientProvider _inner = new(serverBaseUrl, productId, pinnedTlsKeyHash);

    public LicenseStatus Status => Map(_inner.Status);

    public async Task<LicenseResult> ActivateAsync(string licenseKey, CancellationToken ct = default)
        => Map(await _inner.ActivateAsync(licenseKey, ct));

    public async Task<LicenseResult> ValidateAsync(CancellationToken ct = default)
        => Map(await _inner.ValidateAsync(ct));

    public async Task<LicenseResult> DeactivateAsync(CancellationToken ct = default)
        => Map(await _inner.DeactivateAsync(ct));

    public void Dispose() => _inner.Dispose();

    private static LicenseStatus Map(ClientStatus s) => s switch
    {
        ClientStatus.Active => LicenseStatus.Active,
        ClientStatus.Inactive => LicenseStatus.Inactive,
        ClientStatus.Expired => LicenseStatus.Expired,
        ClientStatus.Suspended => LicenseStatus.Suspended,
        ClientStatus.Invalid => LicenseStatus.Invalid,
        _ => LicenseStatus.Unknown,
    };

    private static LicenseResult Map(global::LexCore.Client.LicenseResult r)
        => r.Success ? LicenseResult.Ok(Map(r.Status)) : LicenseResult.Fail(Map(r.Status), r.Message ?? string.Empty);
}
