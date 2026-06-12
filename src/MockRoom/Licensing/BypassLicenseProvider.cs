using System.Threading;
using System.Threading.Tasks;

namespace MockRoom.Licensing;

/// <summary>
/// A no-op <see cref="ILicenseProvider"/> that always reports an active license.
/// Used during development to skip activation while keeping the licensing
/// framework fully wired. Swap back to the real provider in the composition root
/// (see <see cref="LicensingOptions"/>) before shipping.
/// </summary>
public sealed class BypassLicenseProvider : ILicenseProvider
{
    public LicenseStatus Status => LicenseStatus.Active;

    public Task<LicenseResult> ActivateAsync(string licenseKey, CancellationToken ct = default)
        => Task.FromResult(LicenseResult.Ok(LicenseStatus.Active));

    public Task<LicenseResult> ValidateAsync(CancellationToken ct = default)
        => Task.FromResult(LicenseResult.Ok(LicenseStatus.Active));

    public Task<LicenseResult> DeactivateAsync(CancellationToken ct = default)
        => Task.FromResult(LicenseResult.Ok(LicenseStatus.Inactive));
}
