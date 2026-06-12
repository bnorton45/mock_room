using System;
using System.Threading.Tasks;
using MockRoom.Licensing;
using Xunit;

namespace MockRoom.Tests;

public class LicensingTests
{
    [Fact]
    public async Task ActivateAsync_ThrowsOnEmptyKey()
    {
        var manager = new LicenseManager(new StubProvider());
        await Assert.ThrowsAsync<ArgumentException>(() => manager.ActivateAsync(""));
    }

    [Fact]
    public async Task ActivateAsync_ReturnsProviderResult()
    {
        var stub = new StubProvider { ActivateResult = LicenseResult.Ok(LicenseStatus.Active) };
        var result = await new LicenseManager(stub).ActivateAsync("test-key");
        Assert.True(result.Success);
        Assert.Equal(LicenseStatus.Active, result.Status);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsExpiredWhenExpired()
    {
        var stub = new StubProvider { ValidateResult = LicenseResult.Fail(LicenseStatus.Expired, "expired") };
        var result = await new LicenseManager(stub).ValidateAsync();
        Assert.False(result.Success);
        Assert.Equal(LicenseStatus.Expired, result.Status);
    }

    [Fact]
    public async Task DeactivateAsync_ReturnsInactiveOnSuccess()
    {
        var stub = new StubProvider { DeactivateResult = LicenseResult.Ok(LicenseStatus.Inactive) };
        var result = await new LicenseManager(stub).DeactivateAsync();
        Assert.True(result.Success);
        Assert.Equal(LicenseStatus.Inactive, result.Status);
    }

    [Fact]
    public void Status_ReflectsProviderStatus()
    {
        var stub = new StubProvider { CurrentStatus = LicenseStatus.Active };
        Assert.Equal(LicenseStatus.Active, new LicenseManager(stub).Status);
    }

    private sealed class StubProvider : ILicenseProvider
    {
        public LicenseStatus CurrentStatus { get; set; } = LicenseStatus.Unknown;
        public LicenseResult ActivateResult { get; set; } = LicenseResult.Ok(LicenseStatus.Active);
        public LicenseResult ValidateResult { get; set; } = LicenseResult.Ok(LicenseStatus.Active);
        public LicenseResult DeactivateResult { get; set; } = LicenseResult.Ok(LicenseStatus.Inactive);

        public LicenseStatus Status => CurrentStatus;
        public Task<LicenseResult> ActivateAsync(string licenseKey, CancellationToken ct = default)
            => Task.FromResult(ActivateResult);
        public Task<LicenseResult> ValidateAsync(CancellationToken ct = default)
            => Task.FromResult(ValidateResult);
        public Task<LicenseResult> DeactivateAsync(CancellationToken ct = default)
            => Task.FromResult(DeactivateResult);
    }
}
