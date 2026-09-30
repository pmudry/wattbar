using Windows.Devices.Power;
using Windows.System.Power;

namespace WattBar;

public enum PowerState { Unknown, Discharging, Charging, Idle }

/// <summary>One battery reading. <see cref="Watts"/> is positive while draining, negative while charging.</summary>
public readonly record struct Sample(DateTime Time, double Watts, PowerState State, double? Percent, int? RemainingMwh);

public static class BatteryReader
{
    public static Sample Read()
    {
        BatteryReport r = Battery.AggregateBattery.GetReport();

        // WinRT reports the charge rate: negative when discharging. We flip it so drain is positive.
        double watts = -(r.ChargeRateInMilliwatts ?? 0) / 1000.0;

        PowerState state = r.Status switch
        {
            BatteryStatus.Discharging => PowerState.Discharging,
            BatteryStatus.Charging => PowerState.Charging,
            BatteryStatus.Idle => PowerState.Idle,
            _ => PowerState.Unknown,
        };

        double? percent = null;
        if (r.RemainingCapacityInMilliwattHours is int rem && r.FullChargeCapacityInMilliwattHours is int full && full > 0)
            percent = 100.0 * rem / full;

        return new Sample(DateTime.Now, watts, state, percent, r.RemainingCapacityInMilliwattHours);
    }
}
