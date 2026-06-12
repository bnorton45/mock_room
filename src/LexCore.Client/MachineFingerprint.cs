using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LexCore.Client;

/// <summary>
/// Builds a stable, opaque hardware fingerprint from five components.
/// The combined hash is HMAC-SHA256(productId, concat(sorted components)) so
/// the same machine produces different fingerprints for different products,
/// preventing cross-product correlation.
///
/// Individual component hashes are also returned for server-side 4-of-5 fuzzy matching.
/// </summary>
public static class MachineFingerprint
{
    public static FingerprintResult Compute(string productId)
    {
        var components = new[]
        {
            HashComponent(GetCpuId()),
            HashComponent(GetMotherboardSerial()),
            HashComponent(GetPrimaryMacAddress()),
            HashComponent(GetDriveSerial()),
            HashComponent(GetOsId()),
        };

        var sorted = components.OrderBy(c => c).ToArray();
        var combined = string.Join("|", sorted);
        var keyBytes = Encoding.UTF8.GetBytes(productId);
        var msgBytes = Encoding.UTF8.GetBytes(combined);
        var digest = HMACSHA256.HashData(keyBytes, msgBytes);

        return new FingerprintResult(
            Hash: Convert.ToHexString(digest).ToLowerInvariant(),
            ComponentsJson: System.Text.Json.JsonSerializer.Serialize(components, LexCoreJsonContext.Default.StringArray));
    }

    private static string GetCpuId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWmiValue("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ReadFileFirstLine("/proc/cpuinfo", "model name");
        return "unknown-cpu";
    }

    private static string GetMotherboardSerial()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWmiValue("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return TryReadFile("/sys/class/dmi/id/board_serial");
        return "unknown-mobo";
    }

    private static string GetPrimaryMacAddress()
    {
        // Skip loopback and known VM OUI prefixes
        var vmOui = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "080027", // VirtualBox
            "000C29", "005056", // VMware
            "525400", // QEMU/KVM
            "00155D", // Hyper-V
        };

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && n.OperationalStatus == OperationalStatus.Up)
            .OrderBy(n => n.Name)
            .Select(n => n.GetPhysicalAddress().ToString())
            .Where(mac => mac.Length >= 6 && !vmOui.Contains(mac[..6]))
            .FirstOrDefault() ?? "unknown-mac";
    }

    private static string GetDriveSerial()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetWmiValue("SELECT SerialNumber FROM Win32_DiskDrive", "SerialNumber");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            foreach (var dev in new[] { "sda", "nvme0n1", "vda", "hda" })
            {
                var path = $"/sys/block/{dev}/device/serial";
                var val = TryReadFile(path);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        return "unknown-drive";
    }

    private static string GetOsId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid")?.ToString() ?? "unknown-win-id";
            }
            catch { return "unknown-win-id"; }
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return TryReadFile("/etc/machine-id");
        return "unknown-os-id";
    }

    private static string HashComponent(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    // WMI access via System.Management — Windows only, loaded reflectively so the
    // assembly carries no static dependency on it. The path is OS-guarded by every
    // caller and wrapped in try/catch with a graceful fallback, and is never reached
    // on the trimmed/AOT Linux target, so the unanalyzable reflection is safe here.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2058",
        Justification = "Windows-only WMI reflection; OS-guarded with try/catch fallback.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Windows-only WMI reflection; OS-guarded with try/catch fallback.")]
    private static string GetWmiValue(string query, string property)
    {
        try
        {
            var asm = System.Reflection.Assembly.Load("System.Management");
            var searcher = asm.CreateInstance("System.Management.ManagementObjectSearcher",
                false, System.Reflection.BindingFlags.Default, null,
                [query], null, null);
            if (searcher is null) return "unknown-wmi";
            var getMethod = searcher.GetType().GetMethod("Get", Type.EmptyTypes);
            var results = getMethod?.Invoke(searcher, null);
            if (results is null) return "unknown-wmi";
            var enumerator = ((System.Collections.IEnumerable)results).GetEnumerator();
            if (!enumerator.MoveNext()) return "unknown-wmi";
            var obj = enumerator.Current;
            return obj?.GetType().GetProperty("Item", [typeof(string)])
                ?.GetValue(obj, [property])?.ToString() ?? "unknown-wmi";
        }
        catch { return "unknown-wmi"; }
    }

    private static string TryReadFile(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch { return "unknown-file"; }
    }

    private static string ReadFileFirstLine(string path, string prefix)
    {
        try
        {
            return File.ReadLines(path)
                .Where(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Split(':').LastOrDefault()?.Trim() ?? string.Empty)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "unknown";
        }
        catch { return "unknown"; }
    }
}

public readonly record struct FingerprintResult(string Hash, string ComponentsJson);
