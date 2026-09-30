using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WattBar;

/// <summary>Cheap context that explains a reading: active power scheme, Windows 11 power-mode overlay, panel brightness, AC.</summary>
public sealed record PowerContext(string Scheme, bool SchemeIsBalanced, string? Overlay, int? Brightness, bool OnAc)
{
    private static readonly Guid BalancedScheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    private static readonly Dictionary<Guid, string> SchemeNames = new()
    {
        [BalancedScheme] = "Balanced",
        [new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")] = "High performance",
        [new Guid("a1841308-3541-4fab-bc81-f71556f20b4a")] = "Power saver",
        [new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61")] = "Ultimate performance",
    };

    private static readonly Dictionary<Guid, string> OverlayNames = new()
    {
        [Guid.Empty] = "balanced",
        [new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a")] = "best efficiency",
        [new Guid("ded574b5-45a0-4f42-8737-46345c09c238")] = "best performance",
    };

    public static PowerContext Read()
    {
        bool onAc = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        var (scheme, balanced) = ReadScheme();
        return new PowerContext(scheme, balanced, ReadOverlay(onAc), ReadBrightness(), onAc);
    }

    /// <summary>"Balanced · best efficiency", "High performance", ...</summary>
    public string Describe() =>
        SchemeIsBalanced && Overlay is string o && o != "balanced" ? $"{Scheme} \u00B7 {o}" : Scheme;

    private static (string name, bool balanced) ReadScheme()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr) != 0) return ("unknown scheme", false);
            try
            {
                var guid = Marshal.PtrToStructure<Guid>(ptr);
                if (SchemeNames.TryGetValue(guid, out var name)) return (name, guid == BalancedScheme);
                return (FriendlyName(guid) ?? "custom scheme", false);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return ("unknown scheme", false);
        }
    }

    private static string? FriendlyName(Guid scheme)
    {
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0) return null;
        var buf = new byte[size];
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buf, ref size) != 0) return null;
        return System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }

    private static string? ReadOverlay(bool onAc)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
            if (key?.GetValue(onAc ? "ActiveOverlayAcPowerScheme" : "ActiveOverlayDcPowerScheme") is not string s) return null;
            return Guid.TryParse(s, out var g) && OverlayNames.TryGetValue(g, out var name) ? name : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject o in searcher.Get())
                return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch
        {
            // no panel or no access
        }
        return null;
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroup, IntPtr powerSetting, byte[]? buffer, ref uint bufferSize);
}
