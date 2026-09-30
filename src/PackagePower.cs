using System.Diagnostics;

namespace WattBar;

/// <summary>
/// CPU package power from the RAPL "Energy Meter" performance counters that Windows exposes through
/// the EMI interface. Instantaneous (1 s) unlike the battery gauge. Absent on machines whose CPU driver
/// has no EMI support, in which case <see cref="Available"/> stays false.
/// </summary>
public sealed class PackagePower : IDisposable
{
    private PerformanceCounter? _pkg;
    private int _zeroReads;

    public bool Available => _pkg is not null;

    public PackagePower()
    {
        // Instance enumeration is empty for this provider, so the only reliable probe is to read it.
        try
        {
            if (!PerformanceCounterCategory.Exists("Energy Meter")) return;
            _pkg = new PerformanceCounter("Energy Meter", "Power", "rapl_package0_pkg", readOnly: true);
            _pkg.NextValue(); // first call primes the counter
        }
        catch
        {
            Disable();
        }
    }

    /// <summary>Package power in watts, or null when unavailable.</summary>
    public double? Read()
    {
        if (_pkg is null) return null;
        try
        {
            double w = _pkg.NextValue() / 1000.0;
            // A counter that exists but never moves off zero is as good as absent.
            if (w <= 0 && ++_zeroReads > 10) { Disable(); return null; }
            if (w > 0) _zeroReads = 0;
            return w;
        }
        catch
        {
            Disable();
            return null;
        }
    }

    private void Disable()
    {
        _pkg?.Dispose();
        _pkg = null;
    }

    public void Dispose() => Disable();
}
