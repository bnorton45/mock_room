namespace LexCore.Client;

public interface ILicenseProvider
{
    LicenseStatus Status { get; }
    Task<LicenseResult> ActivateAsync(string licenseKey, CancellationToken ct = default);
    Task<LicenseResult> ValidateAsync(CancellationToken ct = default);
    Task<LicenseResult> DeactivateAsync(CancellationToken ct = default);
}
