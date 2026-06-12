using System;
using System.Threading;
using System.Threading.Tasks;

namespace MockRoom.Licensing;

public sealed class LicenseManager
{
    private readonly ILicenseProvider _provider;

    public LicenseManager(ILicenseProvider provider) => _provider = provider;

    public LicenseStatus Status => _provider.Status;

    public Task<LicenseResult> ActivateAsync(string licenseKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseKey);
        return _provider.ActivateAsync(licenseKey, ct);
    }

    public Task<LicenseResult> ValidateAsync(CancellationToken ct = default)
        => _provider.ValidateAsync(ct);

    public Task<LicenseResult> DeactivateAsync(CancellationToken ct = default)
        => _provider.DeactivateAsync(ct);
}
