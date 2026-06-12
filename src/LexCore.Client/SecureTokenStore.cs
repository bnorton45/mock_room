using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace LexCore.Client;

/// <summary>
/// Stores sensitive activation data using OS-native secret storage:
///   Windows : DPAPI (ProtectedData, CurrentUser scope)
///   Linux   : AES-256-GCM encrypted file, key derived from machine-id + username
///   macOS   : AES-256-GCM encrypted file (Keychain via SecKeychainAdd would require
///              Security.framework P/Invoke — left as a follow-up; same file fallback used)
/// </summary>
public sealed class SecureTokenStore
{
    private readonly string _storePath;
    private static readonly byte[] _entropy = "lexcore-v1"u8.ToArray();

    public SecureTokenStore(string productId)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "lex-core");
        Directory.CreateDirectory(dir);
        _storePath = Path.Combine(dir, $"{SanitizeId(productId)}.dat");
    }

    public void Save(ActivationState state)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(state, LexCoreJsonContext.Default.ActivationState);
        var encrypted = Encrypt(json);
        File.WriteAllBytes(_storePath, encrypted);
    }

    public ActivationState? Load()
    {
        if (!File.Exists(_storePath)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(_storePath);
            var json = Decrypt(encrypted);
            return JsonSerializer.Deserialize(json, LexCoreJsonContext.Default.ActivationState);
        }
        catch { return null; }
    }

    public void Clear()
    {
        if (File.Exists(_storePath))
            File.Delete(_storePath);
    }

    private byte[] Encrypt(byte[] plaintext)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Protect(plaintext, _entropy, DataProtectionScope.CurrentUser);

        // Non-Windows: AES-256-GCM with machine-derived key
        var key = DeriveKey();
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        // Layout: [12 nonce][16 tag][ciphertext]
        return [.. nonce, .. tag, .. ciphertext];
    }

    private byte[] Decrypt(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Unprotect(data, _entropy, DataProtectionScope.CurrentUser);

        if (data.Length < 28) throw new CryptographicException("Invalid data.");
        var nonce = data[..12];
        var tag = data[12..28];
        var ciphertext = data[28..];
        var plaintext = new byte[ciphertext.Length];
        var key = DeriveKey();
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static byte[] DeriveKey()
    {
        // Machine-specific key derivation: machine-id + username as IKM
        var machineId = TryRead("/etc/machine-id") ?? Environment.MachineName;
        var user = Environment.UserName;
        var ikm = Encoding.UTF8.GetBytes($"{machineId}:{user}:lexcore");
        var salt = Encoding.UTF8.GetBytes("lexcore-key-derivation-v1");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt);
    }

    private static string? TryRead(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch { return null; }
    }

    private static string SanitizeId(string id) =>
        new(id.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
}

public sealed record ActivationState(
    Guid ActivationId,
    string ActivationSecret,
    DateTime? LicenseExpiresAt,
    DateTime LastServerTime,
    DateTime LastSyncAt);
