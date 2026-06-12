using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace LexCore.Client;

/// <summary>
/// Detects virtual machine environments via three independent signals:
///   1. CPUID hypervisor bit (ECX[31] of leaf 1)
///   2. SMBIOS vendor/product/version strings
///   3. Known VM MAC OUI prefixes
/// </summary>
public static class VmDetector
{
    private static readonly string[] VmSmbiosStrings =
    [
        "vmware", "virtualbox", "vbox", "qemu", "kvm",
        "hyper-v", "hyperv", "xen", "parallels", "bochs",
        "bhyve", "virtual machine",
    ];

    private static readonly string[] VmMacPrefixes =
    [
        "080027", // VirtualBox
        "000C29", "005056", "000569", // VMware
        "525400", // QEMU/KVM
        "00155D", // Hyper-V
        "001C42", // Parallels
    ];

    public static bool IsVirtualMachine()
        => CheckCpuidHypervisorBit() || CheckSmbiosStrings() || CheckMacPrefixes();

    private static bool CheckCpuidHypervisorBit()
    {
        if (!X86Base.IsSupported) return false;
        try
        {
            // CPUID leaf 1, ECX bit 31 is the hypervisor present bit
            var (_, _, ecx, _) = X86Base.CpuId(1, 0);
            return (ecx & (1 << 31)) != 0;
        }
        catch { return false; }
    }

    private static bool CheckSmbiosStrings()
    {
        var paths = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? new[]
            {
                "/sys/class/dmi/id/sys_vendor",
                "/sys/class/dmi/id/product_name",
                "/sys/class/dmi/id/product_version",
                "/sys/class/dmi/id/board_vendor",
            }
            : [];

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var value = File.ReadAllText(path).ToLowerInvariant();
            if (VmSmbiosStrings.Any(s => value.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return CheckSmbiosWindows();

        return false;
    }

    // Windows-only WMI reflection (System.Management), OS-guarded by the caller and
    // wrapped in try/catch; never reached on the trimmed/AOT Linux target.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2058",
        Justification = "Windows-only WMI reflection; OS-guarded with try/catch fallback.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Windows-only WMI reflection; OS-guarded with try/catch fallback.")]
    private static bool CheckSmbiosWindows()
    {
        try
        {
            var asm = System.Reflection.Assembly.Load("System.Management");
            foreach (var wmiClass in new[] { "Win32_ComputerSystem", "Win32_BaseBoard" })
            {
                var searcher = asm.CreateInstance("System.Management.ManagementObjectSearcher",
                    false, System.Reflection.BindingFlags.Default, null,
                    [$"SELECT Manufacturer,Model,Name FROM {wmiClass}"], null, null);
                var getMethod = searcher?.GetType().GetMethod("Get", Type.EmptyTypes);
                var results = getMethod?.Invoke(searcher, null) as System.Collections.IEnumerable;
                if (results is null) continue;

                foreach (var obj in results)
                {
                    foreach (var prop in new[] { "Manufacturer", "Model", "Name" })
                    {
                        var val = obj.GetType().GetProperty("Item", [typeof(string)])
                            ?.GetValue(obj, [prop])?.ToString()?.ToLowerInvariant() ?? string.Empty;
                        if (VmSmbiosStrings.Any(s => val.Contains(s, StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private static bool CheckMacPrefixes()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.GetPhysicalAddress().ToString())
            .Where(mac => mac.Length >= 6)
            .Any(mac => VmMacPrefixes.Any(p =>
                mac.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
    }
}
