using System.Management;

namespace WattBar;

public enum PowerState { Unknown, Discharging, Charging, Idle }

/// <summary>
/// One reading. <see cref="Watts"/> is the battery gauge, positive while draining, negative while charging.
/// <see cref="PackageWatts"/> is the CPU package (RAPL) power when the machine exposes it.
/// </summary>
public readonly record struct Sample(DateTime Time, double Watts, PowerState State, double? Percent, int? RemainingMwh, double? PackageWatts = null);

/// <summary>
/// Battery gauge through the WMI classes in root\wmi (BatteryStatus, BatteryFullChargedCapacity). Same numbers as
/// the WinRT battery report, without pulling the 24 MB Windows SDK projection into the exe.
/// </summary>
public static class BatteryReader
{
    private static readonly ManagementObjectSearcher Status = new(@"root\wmi", "SELECT Charging, Discharging, ChargeRate, DischargeRate, RemainingCapacity FROM BatteryStatus");
    private static readonly ManagementObjectSearcher Full = new(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
    private static int? _fullMwh;
    private static DateTime _fullRead = DateTime.MinValue;

    public static Sample Read()
    {
        // Full-charge capacity changes only with battery wear; refresh it every few minutes.
        if (_fullMwh is null || DateTime.Now - _fullRead > TimeSpan.FromMinutes(5))
        {
            foreach (ManagementObject o in Full.Get()) { _fullMwh = ToInt(o["FullChargedCapacity"]); break; }
            _fullRead = DateTime.Now;
        }

        foreach (ManagementObject o in Status.Get())
        {
            bool charging = o["Charging"] is true;
            bool discharging = o["Discharging"] is true;
            int rem = ToInt(o["RemainingCapacity"]) ?? 0;
            double watts = discharging ? (ToInt(o["DischargeRate"]) ?? 0) / 1000.0
                         : charging ? -(ToInt(o["ChargeRate"]) ?? 0) / 1000.0
                         : 0;
            var state = discharging ? PowerState.Discharging : charging ? PowerState.Charging : PowerState.Idle;
            double? percent = _fullMwh is int full && full > 0 ? Math.Min(100.0, 100.0 * rem / full) : null;
            return new Sample(DateTime.Now, watts, state, percent, rem);
        }
        return new Sample(DateTime.Now, 0, PowerState.Unknown, null, null);
    }

    private static int? ToInt(object? v) => v is null ? null : Convert.ToInt32(v);
}
