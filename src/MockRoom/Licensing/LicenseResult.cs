namespace MockRoom.Licensing;

public sealed record LicenseResult(bool Success, LicenseStatus Status, string? Message = null)
{
    public static LicenseResult Ok(LicenseStatus status) => new(true, status);
    public static LicenseResult Fail(LicenseStatus status, string message) => new(false, status, message);
}
