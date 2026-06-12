namespace MockRoom.Licensing;

/// <summary>
/// Composition-root switch for the licensing backend.
///
/// While <see cref="BypassLicensing"/> is <c>true</c> the app uses
/// <see cref="BypassLicenseProvider"/> and never contacts a LexCore server.
/// Set it to <c>false</c> (and supply the LexCore server URL, product id, and
/// pinned TLS key hash) to restore real activation for the shipping build.
/// </summary>
public static class LicensingOptions
{
    // TODO(licensing): flip to false and configure LexCore before release.
    public const bool BypassLicensing = true;

    public static ILicenseProvider CreateProvider()
        => BypassLicensing
            ? new BypassLicenseProvider()
            : throw new System.InvalidOperationException(
                "LexCore licensing is not yet configured. Provide serverBaseUrl, productId, and " +
                "pinnedTlsKeyHash here and construct LexCore.LexCoreProvider, then set BypassLicensing = false.");
}
