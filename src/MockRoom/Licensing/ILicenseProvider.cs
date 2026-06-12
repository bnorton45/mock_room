using System.Threading;
using System.Threading.Tasks;

namespace MockRoom.Licensing;

public interface ILicenseProvider
{
    LicenseStatus Status { get; }
    Task<LicenseResult> ActivateAsync(string licenseKey, CancellationToken ct = default);
    Task<LicenseResult> ValidateAsync(CancellationToken ct = default);
    Task<LicenseResult> DeactivateAsync(CancellationToken ct = default);
}
