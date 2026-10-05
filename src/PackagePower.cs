using System.Diagnostics;

namespace WattBar;

/// <summary>
/// CPU package power from the RAPL "Energy Meter" performance counters that Windows exposes through
/// the EMI interface. Instantaneous (1 s) unlike the battery gauge. Absent on machines whose CPU driver
/// has no EMI support. The counter can also fail or read zero for a while around standby and resume, so a
/// failure only pauses the reader; it tries to come back every <see cref="RetryAfter"/>.
/// </summary>
public sealed class PackagePower : IDisposable
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private PerformanceCounter? _pkg;
    private int _zeroReads;
    private DateTime _pausedAt = DateTime.MinValue;
    private bool _everWorked;

    /// <summary>True while the counter is being read successfully.</summary>
    public bool Available => _pkg is not null;

    public PackagePower() => TryOpen();

    /// <summary>Package power in watts, or null when unavailable right now.</summary>
    public double? Read()
    {
        if (_pkg is null)
        {
            if (DateTime.Now - _pausedAt < RetryAfter) return null;
            TryOpen();
            if (_pkg is null) return null;
        }
        try
        {
            double w = _pkg.NextValue() / 1000.0;
            // A counter that stays at zero is not delivering: pause and retry later. Before it has ever
            // worked, a long zero streak most likely means the machine does not expose RAPL at all.
            if (w <= 0)
            {
                if (++_zeroReads > 10) Pause();
                return null;
            }
            _zeroReads = 0;
            _everWorked = true;
            return w;
        }
        catch
        {
            Pause();
            return null;
        }
    }

    private void TryOpen()
    {
        // Instance enumeration is empty for this provider, so the only reliable probe is to read it.
        try
        {
            if (!PerformanceCounterCategory.Exists("Energy Meter")) { _pausedAt = DateTime.Now; return; }
            _pkg = new PerformanceCounter("Energy Meter", "Power", "rapl_package0_pkg", readOnly: true);
            _pkg.NextValue(); // first call primes the counter
            _zeroReads = 0;
        }
        catch
        {
            Pause();
        }
    }

    private void Pause()
    {
        _pkg?.Dispose();
        _pkg = null;
        _zeroReads = 0;
        // Machines without RAPL would otherwise probe every 30 s forever; once a day is plenty for them.
        _pausedAt = _everWorked ? DateTime.Now : DateTime.Now + TimeSpan.FromHours(24) - RetryAfter;
    }

    public void Dispose()
    {
        _pkg?.Dispose();
        _pkg = null;
    }
}
