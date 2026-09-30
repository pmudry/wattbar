using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using static WattBar.L10n;

namespace WattBar;

/// <summary>
/// Cheap context that explains a reading: the classic power plan, the Windows 11 power mode (overlay) for
/// battery and for plugged in, panel brightness, AC. Also switches plan and mode; both calls work unelevated.
/// </summary>
public sealed record PowerContext(string Scheme, bool SchemeIsBalanced, Guid OverlayAc, Guid OverlayDc, int? Brightness, bool OnAc)
{
    public static readonly Guid BalancedScheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    public static readonly Guid OverlayBalanced = Guid.Empty;
    public static readonly Guid OverlayEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid OverlayPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    /// <summary>Power modes in the order Settings shows them; names are translation keys.</summary>
    public static readonly (Guid guid, string name)[] Overlays =
    [
        (OverlayEfficiency, "Best efficiency"),
        (OverlayBalanced, "Balanced"),
        (OverlayPerformance, "Best performance"),
    ];

    private static readonly Dictionary<Guid, string> SchemeNames = new()
    {
        [BalancedScheme] = "Balanced",
        [new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")] = "High performance",
        [new Guid("a1841308-3541-4fab-bc81-f71556f20b4a")] = "Power saver",
        [new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61")] = "Ultimate performance",
    };

    public Guid CurrentOverlay => OnAc ? OverlayAc : OverlayDc;

    public static string OverlayName(Guid g)
    {
        foreach (var (guid, name) in Overlays) if (guid == g) return name;
        return "Balanced";
    }

    public static PowerContext Read()
    {
        bool onAc = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        var (scheme, balanced) = ReadScheme();
        var (ac, dc) = ReadOverlays();
        // The live value for the current source beats the registry copy.
        if (PowerGetEffectiveOverlayScheme(out var eff) == 0)
        {
            if (onAc) ac = eff; else dc = eff;
        }
        return new PowerContext(scheme, balanced, ac, dc, ReadBrightness(), onAc);
    }

    /// <summary>"Mode: best performance (plugged in)", or a warning when the plan is not Balanced and modes are off.</summary>
    public string Describe() => SchemeIsBalanced
        ? T("Mode: {0} ({1})", T(OverlayName(CurrentOverlay)).ToLowerInvariant(), T(OnAc ? "plugged in" : "on battery"))
        : T("Plan is {0}, power modes are off", T(Scheme));

    /// <summary>Sets the power mode for the current source, as Settings does.</summary>
    public static bool SetOverlay(Guid overlay)
    {
        try { return PowerSetActiveOverlayScheme(overlay) == 0; }
        catch { return false; }
    }

    /// <summary>Sets the classic power plan.</summary>
    public static bool SetScheme(Guid scheme)
    {
        try { return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0; }
        catch { return false; }
    }

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

    private static (Guid ac, Guid dc) ReadOverlays()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
            Guid Parse(string name) => key?.GetValue(name) is string s && Guid.TryParse(s, out var g) ? g : Guid.Empty;
            return (Parse("ActiveOverlayAcPowerScheme"), Parse("ActiveOverlayDcPowerScheme"));
        }
        catch
        {
            return (Guid.Empty, Guid.Empty);
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
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroup, IntPtr powerSetting, byte[]? buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlay);
}
