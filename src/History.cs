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

    /// <summary>Mean absolute power over the given span, or null when empty.</summary>
    public double? Average(TimeSpan span)
    {
        var samples = Since(span);
        return samples.Count == 0 ? null : samples.Average(s => Math.Abs(s.Watts));
    }

    /// <summary>Downsample the last <paramref name="span"/> into <paramref name="bins"/> averaged buckets.</summary>
    public double[] Bins(TimeSpan span, int bins)
    {
        var samples = Since(span);
        var result = new double[bins];
        if (samples.Count == 0) return result;

        var counts = new int[bins];
        var t0 = DateTime.Now - span;
        foreach (var s in samples)
        {
            int i = (int)((s.Time - t0).Ticks * bins / span.Ticks);
            i = Math.Clamp(i, 0, bins - 1);
            result[i] += Math.Abs(s.Watts);
            counts[i]++;
        }
        for (int i = 0; i < bins; i++)
            if (counts[i] > 0) result[i] /= counts[i];
        return result;
    }
}
