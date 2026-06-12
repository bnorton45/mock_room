using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LexCore.Client;

/// <summary>
/// HTTP client for the LexCore server.
/// Implements:
///   - TLS public key pinning (SHA-256 of server's SubjectPublicKeyInfo)
///   - Per-request nonce + timestamp + HMAC-SHA256 signature headers
///   - Server time extraction from every response
/// </summary>
public sealed class LexCoreClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ClockDriftGuard _driftGuard;
    private string? _activationSecret;

    public LexCoreClient(string serverBaseUrl, byte[] pinnedTlsKeyHash, ClockDriftGuard driftGuard)
    {
        _driftGuard = driftGuard;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                if (cert is null) return false;
                var pubKeyHash = SHA256.HashData(cert.PublicKey.EncodedKeyValue.RawData);
                return CryptographicOperations.FixedTimeEquals(pubKeyHash, pinnedTlsKeyHash);
            },
        };

        _http = new HttpClient(handler) { BaseAddress = new Uri(serverBaseUrl) };
    }

    public void SetActivationSecret(string secret) => _activationSecret = secret;

    public async Task<ActivateResponse?> ActivateAsync(
        string licenseKey, string fingerprintHash, string? fingerprintComponents,
        string machineName, bool isVm, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(
            new ActivateRequest(licenseKey, fingerprintHash, fingerprintComponents, machineName, isVm),
            LexCoreJsonContext.Default.ActivateRequest);

        var resp = await PostAsync("/api/v1/activations", body, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var result = await resp.Content.ReadFromJsonAsync(LexCoreJsonContext.Default.ActivateResponse, ct);
        if (result is not null)
        {
            _activationSecret = result.ActivationSecret;
            _driftGuard.RecordServerTime(result.ServerTime);
        }
        return result;
    }

    public async Task<ValidateResponse?> ValidateAsync(
        Guid activationId, string fingerprintHash, CancellationToken ct)
    {
        var url = $"/api/v1/activations/{activationId}?fingerprint={Uri.EscapeDataString(fingerprintHash)}";
        var resp = await GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var result = await resp.Content.ReadFromJsonAsync(LexCoreJsonContext.Default.ValidateResponse, ct);
        if (result is not null)
            _driftGuard.RecordServerTime(result.ServerTime);
        return result;
    }

    public async Task<bool> DeactivateAsync(Guid activationId, string fingerprintHash, CancellationToken ct)
    {
        var url = $"/api/v1/activations/{activationId}?fingerprint={Uri.EscapeDataString(fingerprintHash)}";
        var req = BuildRequest(HttpMethod.Delete, url, body: null);
        var resp = await _http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<TrialResponse?> StartTrialAsync(
        string fingerprintHash, Guid productId, bool isVm, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(
            new TrialStartRequest(fingerprintHash, productId, isVm),
            LexCoreJsonContext.Default.TrialStartRequest);
        var resp = await PostAsync("/api/v1/trial/start", body, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var result = await resp.Content.ReadFromJsonAsync(LexCoreJsonContext.Default.TrialResponse, ct);
        if (result is not null)
            _driftGuard.RecordServerTime(result.ServerTime);
        return result;
    }

    public async Task<TrialResponse?> GetTrialStatusAsync(
        string fingerprintHash, Guid productId, CancellationToken ct)
    {
        var url = $"/api/v1/trial/status?fingerprint={Uri.EscapeDataString(fingerprintHash)}&productId={productId}";
        var resp = await GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var result = await resp.Content.ReadFromJsonAsync(LexCoreJsonContext.Default.TrialResponse, ct);
        if (result is not null)
            _driftGuard.RecordServerTime(result.ServerTime);
        return result;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string jsonBody, CancellationToken ct)
    {
        var req = BuildRequest(HttpMethod.Post, path, jsonBody);
        return await _http.SendAsync(req, ct);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct)
    {
        var req = BuildRequest(HttpMethod.Get, path, body: null);
        return await _http.SendAsync(req, ct);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string? body)
    {
        var req = new HttpRequestMessage(method, path);
        var tsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");

        req.Headers.Add("X-Timestamp", tsMs);
        req.Headers.Add("X-Nonce", nonce);

        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        if (_activationSecret is not null)
        {
            var bodyHash = body is not null
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant()
                : Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();

            var sig = ComputeSig(_activationSecret, nonce, tsMs, bodyHash);
            req.Headers.Add("X-Request-Sig", sig);
        }

        return req;
    }

    private static string ComputeSig(string secret, string nonce, string timestamp, string bodyHex)
    {
        var message = $"{nonce}.{timestamp}.{bodyHex}";
        var keyBytes = Convert.FromHexString(secret);
        var msgBytes = Encoding.UTF8.GetBytes(message);
        var hash = HMACSHA256.HashData(keyBytes, msgBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose() => _http.Dispose();
}

public sealed record ActivateRequest(
    string LicenseKey,
    string FingerprintHash,
    string? FingerprintComponents,
    string MachineName,
    bool IsVirtualMachine);

public sealed record TrialStartRequest(
    string FingerprintHash,
    Guid ProductId,
    bool IsVirtualMachine);

public sealed record ActivateResponse(
    Guid ActivationId,
    string ActivationSecret,
    DateTime? LicenseExpiresAt,
    DateTime ServerTime);

public sealed record ValidateResponse(
    Guid ActivationId,
    string LicenseStatus,
    DateTime? LicenseExpiresAt,
    DateTime ServerTime);

public sealed record TrialResponse(
    Guid SessionId,
    string Status,
    DateTime StartedAt,
    DateTime ExpiresAt,
    DateTime ServerTime);
