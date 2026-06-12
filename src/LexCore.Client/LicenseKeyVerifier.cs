using System.Security.Cryptography;
using SimpleBase;

namespace LexCore.Client;

/// <summary>
/// Verifies the ECDSA P-256 signature on a license key without calling the server.
/// The public key is stored as two XOR'd halves to slow naive binary string search.
/// Call SetPublicKey(spki) once at startup with your server's SPKI-encoded public key.
/// </summary>
public sealed class LicenseKeyVerifier : IDisposable
{
    private readonly ECDsa _key;

    /// <summary>
    /// Construct with the server's SPKI-encoded (SubjectPublicKeyInfo / DER) public key bytes.
    /// Embed the key in your binary as two XOR'd halves combined at runtime (P-256 SPKI = 91 bytes):
    ///   private static ReadOnlySpan&lt;byte&gt; HalfA => [/* all 91 bytes XOR'd with mask */];
    ///   private static ReadOnlySpan&lt;byte&gt; HalfB => [/* 91-byte XOR mask */];
    ///   var spki = HalfA.ToArray().Zip(HalfB.ToArray(), (a, b) => (byte)(a ^ b)).ToArray();
    /// </summary>
    public LicenseKeyVerifier(byte[] spkiPublicKey)
    {
        _key = ECDsa.Create();
        _key.ImportSubjectPublicKeyInfo(spkiPublicKey, out _);
    }

    public void Dispose() => _key.Dispose();

    public bool Verify(string key, out LicenseKeyInfo info)
    {
        info = default;
        try
        {
            var raw = Decode(key);
            if (raw is not { Length: 86 }) return false;
            var payload = raw[..22];
            var sig = raw[22..86];
            if (!_key.VerifyData(payload, sig, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return false;

            info = ParsePayload(payload);
            return true;
        }
        catch { return false; }
    }

    private static LicenseKeyInfo ParsePayload(byte[] payload)
    {
        var expiresDay = ReadUInt32BE(payload, 6);
        return new LicenseKeyInfo(
            Tier: payload[4],
            IsFloating: (payload[5] & 1) != 0,
            AllowVm: (payload[5] & 2) != 0,
            ExpiresAt: expiresDay == 0
                ? null
                : DateTimeOffset.FromUnixTimeSeconds(expiresDay * 86400L).UtcDateTime,
            MaxSeats: (ushort)((payload[10] << 8) | payload[11])
        );
    }

    private static byte[] Decode(string key)
        => Base32.Rfc4648.Decode(key.Replace("-", "").ToUpperInvariant());

    private static uint ReadUInt32BE(byte[] buf, int offset)
        => ((uint)buf[offset] << 24) | ((uint)buf[offset + 1] << 16) |
           ((uint)buf[offset + 2] << 8) | buf[offset + 3];
}

public readonly record struct LicenseKeyInfo(
    byte Tier,
    bool IsFloating,
    bool AllowVm,
    DateTime? ExpiresAt,
    ushort MaxSeats);
