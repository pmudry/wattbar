namespace WattBar;

/// <summary>Fixed-size ring buffer of samples, oldest evicted first.</summary>
public sealed class History
{
    private readonly Sample[] _buf;
    private int _head;

    public History(int capacity) => _buf = new Sample[capacity];

    public int Count { get; private set; }

    public void Add(Sample s)
    {
        _buf[_head] = s;
        _head = (_head + 1) % _buf.Length;
        if (Count < _buf.Length) Count++;
    }

    public Sample? Last => Count == 0 ? null : _buf[(_head - 1 + _buf.Length) % _buf.Length];

    /// <summary>Samples newer than <paramref name="span"/>, oldest first.</summary>
    public List<Sample> Since(TimeSpan span)
    {
        var cutoff = DateTime.Now - span;
        var result = new List<Sample>();
        int start = (_head - Count + _buf.Length) % _buf.Length;
        for (int i = 0; i < Count; i++)
        {
            var s = _buf[(start + i) % _buf.Length];
            if (s.Time >= cutoff) result.Add(s);
        }
        return result;
    }

    /// <summary>Mean absolute gauge power over the given span, or null when empty.</summary>
    public double? Average(TimeSpan span)
    {
        var samples = Since(span);
        return samples.Count == 0 ? null : samples.Average(s => Math.Abs(s.Watts));
    }

    /// <summary>Mean package power over the span, or null when no sample carries one.</summary>
    public double? PackageAverage(TimeSpan span)
    {
        var values = Since(span).Where(s => s.PackageWatts is not null).Select(s => s.PackageWatts!.Value).ToList();
        return values.Count == 0 ? null : values.Average();
    }

    /// <summary>
    /// Mean drain computed from the change in remaining capacity across the span. This is the honest
    /// average: the gauge reports a stepped one-minute rolling value, so averaging its samples over-weights
    /// plateaus. Null unless the window covers at least <paramref name="minimum"/> of continuous discharge
    /// with a measurable capacity drop.
    /// </summary>
    public double? CapacityAverage(TimeSpan span, TimeSpan? minimum = null)
    {
        var samples = Since(span);
        Sample? first = null, last = null;
        foreach (var s in samples)
        {
            if (s.State != PowerState.Discharging || s.RemainingMwh is null) { first = null; continue; }
            first ??= s;
            last = s;
        }
        if (first is null || last is null) return null;

        var dt = last.Value.Time - first.Value.Time;
        if (dt < (minimum ?? TimeSpan.FromMinutes(4))) return null;

        int drop = first.Value.RemainingMwh!.Value - last.Value.RemainingMwh!.Value;
        if (drop <= 0) return null;
        return drop / dt.TotalHours / 1000.0;
    }

    /// <summary>Downsample the last <paramref name="span"/> into <paramref name="bins"/> averaged buckets of gauge power.</summary>
    public double[] Bins(TimeSpan span, int bins) => Bins(span, bins, s => Math.Abs(s.Watts));

    /// <summary>Downsample the last <paramref name="span"/> into <paramref name="bins"/> averaged buckets of the selected value.</summary>
    public double[] Bins(TimeSpan span, int bins, Func<Sample, double?> selector)
    {
        var samples = Since(span);
        var result = new double[bins];
        if (samples.Count == 0) return result;

        var counts = new int[bins];
        var t0 = DateTime.Now - span;
        foreach (var s in samples)
        {
            if (selector(s) is not double v) continue;
            int i = (int)((s.Time - t0).Ticks * bins / span.Ticks);
            i = Math.Clamp(i, 0, bins - 1);
            result[i] += v;
            counts[i]++;
        }
        for (int i = 0; i < bins; i++)
            if (counts[i] > 0) result[i] /= counts[i];
        return result;
    }
}
