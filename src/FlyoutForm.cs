using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

using static WattBar.L10n;

namespace WattBar;

/// <summary>Borderless popup anchored above the tray that plots recent battery and package power.</summary>
public sealed class FlyoutForm : Form
{
    private static readonly TimeSpan[] Windows = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];
    private static readonly TimeSpan EstimateWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EstimateStep = TimeSpan.FromMinutes(10);

    private const int BaseWidth = 380;
    private const int BaseHeight = 300;

    private readonly History _history;
    private readonly Func<PowerContext?> _context;
    private readonly Action _showOffenders;
    private readonly Action<Control, Point> _showSettings;
    private int _windowIndex = 1; // 5 minutes by default
    private TimeSpan? _shownLeft;

    private const string PackageHelp =
        "CPU package power (Intel/AMD RAPL): cores, integrated GPU and on-package memory,\n" +
        "read every second. The battery figure minus this is roughly the display, Wi-Fi,\n" +
        "SSD and the rest of the machine. The battery gauge is a slow one-minute average;\n" +
        "this one reacts instantly.";
    private readonly ToolTip _tip = new() { InitialDelay = 300, ReshowDelay = 100 };
    private readonly List<(RectangleF rect, string? tip, Action? click)> _hot = [];
    private int _tipRegion = -1;

    public FlyoutForm(History history, Func<PowerContext?> context, Action showOffenders, Action<Control, Point> showSettings)
    {
        _history = history;
        _context = context;
        _showOffenders = showOffenders;
        _showSettings = showSettings;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "WattBar";
    }

    protected override bool ShowWithoutActivation => false;

    // WS_EX_TOOLWINDOW keeps it out of Alt-Tab.
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int pref = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref pref, sizeof(int));
    }

    public void ShowAtTray()
    {
        _ = Handle;
        float s = DeviceDpi / 96f;
        Size = new Size((int)(BaseWidth * s), (int)(BaseHeight * s));

        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var wa = screen.WorkingArea;
        int margin = (int)(12 * s);
        int x = wa.Right - Width - margin;
        int y = wa.Top > screen.Bounds.Top ? wa.Top + margin : wa.Bottom - Height - margin;
        Location = new Point(x, y);

        Show();
        Activate();
        Invalidate();
    }

    /// <summary>When the flyout was last hidden by losing focus; lets a tray click that caused it act as "close".</summary>
    public DateTime HiddenAt { get; private set; } = DateTime.MinValue;

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HideTip();
        HiddenAt = DateTime.Now;
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Hide();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int region = _hot.FindIndex(h => h.tip is not null && h.rect.Contains(e.Location));
        if (region == _tipRegion) return;
        if (_tipRegion >= 0) _tip.Hide(this);
        _tipRegion = region;
        if (region >= 0) _tip.Show(_hot[region].tip, this, e.X, e.Y + 20);
        Cursor = _hot.Any(h => h.click is not null && h.rect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        HideTip();
    }

    private void HideTip()
    {
        if (_tipRegion >= 0) { _tip.Hide(this); _tipRegion = -1; }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        _hot.FirstOrDefault(h => h.click is not null && h.rect.Contains(e.Location)).click?.Invoke();
    }

    private void CycleWindow()
    {
        _windowIndex = (_windowIndex + 1) % Windows.Length;
        Invalidate();
    }

    private ContextMenuStrip? _windowMenu;

    private void ShowWindowMenu(Point at)
    {
        // Never dispose a menu from its own events: WinForms still touches it afterwards (keyboard handling).
        // The previous instance is released here, long after it closed.
        _windowMenu?.Dispose();
        var menu = _windowMenu = new ContextMenuStrip();
        for (int i = 0; i < Windows.Length; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(T("last {0} min", (int)Windows[i].TotalMinutes)) { Checked = i == _windowIndex };
            item.Click += (_, _) => { _windowIndex = index; Invalidate(); };
            menu.Items.Add(item);
        }
        menu.Show(this, at);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool dark = Theme.AppsAreDark();
        var bg = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249);
        var fg = dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(28, 28, 28);
        var muted = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(110, 110, 110);
        var grid = dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(225, 225, 225);
        var border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);

        g.Clear(bg);
        using (var pen = new Pen(border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        float s = DeviceDpi / 96f;
        float pad = 16 * s;
        var span = Windows[_windowIndex];
        var samples = _history.Since(span);
        var last = _history.Last;
        var state = last?.State ?? PowerState.Unknown;
        var accent = Theme.Accent(state, dark);
        // The package series is context, the battery average is the point: keep it translucent.
        var packageLine = Color.FromArgb(110, Theme.Package(dark));
        var package = Theme.Package(dark);
        var ctx = _context();

        using var fontSmall = new Font("Segoe UI", 9f);
        using var fontBig = new Font("Segoe UI", 22f, FontStyle.Bold);
        using var brushFg = new SolidBrush(fg);
        using var brushMuted = new SolidBrush(muted);
        using var brushAccent = new SolidBrush(accent);
        using var brushPackage = new SolidBrush(package);

        _hot.Clear();

        // Header: title left; "who is using it", gear and the time window on the right
        g.DrawString(T("Battery power"), fontSmall, brushMuted, pad, pad);
        string windowLabel = T("last {0} min", (int)span.TotalMinutes) + "  \u25BE";
        var wl = g.MeasureString(windowLabel, fontSmall);
        float hx = Width - pad - wl.Width;
        g.DrawString(windowLabel, fontSmall, brushMuted, hx, pad);
        float labelX = hx, labelBottom = pad + wl.Height;
        _hot.Add((new RectangleF(hx, pad, wl.Width, wl.Height), T("Time window"), () => ShowWindowMenu(new Point((int)labelX, (int)labelBottom))));

        float gearW = DrawGlyphButton(g, "\uE713", hx - 10 * s, pad, wl.Height, muted, s);
        hx -= 10 * s + gearW;
        _hot.Add((new RectangleF(hx, pad, gearW, wl.Height), T("Settings: theme, language, start with Windows, about"), () => _showSettings(this, new Point((int)hx, (int)(pad + wl.Height)))));

        float whoW = DrawPillButton(g, T("Who is using it"), fontSmall, hx - 12 * s, pad, wl.Height, muted, border, s);
        hx -= 12 * s + whoW;
        _hot.Add((new RectangleF(hx, pad - 2 * s, whoW, wl.Height + 4 * s), T("Per-process energy estimates (opens a window)"), _showOffenders));

        // Big number + status
        float y = pad + 20 * s;
        double? pkgNow = _history.PackageNow();
        bool packageMode = last is { State: PowerState.Idle } && pkgNow is not null;
        string big = last is null ? "--" : packageMode ? $"{pkgNow!.Value:0.0} W" : $"{Math.Abs(last.Value.Watts):0.0} W";
        g.DrawString(big, fontBig, packageMode ? brushPackage : brushFg, pad - 2 * s, y);
        var bigSize = g.MeasureString(big, fontBig);
        g.DrawString(StatusLine(last), fontSmall, brushMuted, pad + bigSize.Width + 4 * s, y + bigSize.Height - 22 * s);

        // Context row: package power, scheme, brightness
        float rowY = y + bigSize.Height + 2 * s;
        var segments = new List<(string text, Brush brush)>();
        if (pkgNow is double pw && !packageMode)
        {
            int win = Settings.PackageWindowSeconds;
            string lbl = win > 1 ? T("package ({0} s avg) ", win) : T("package ");
            segments.Add((lbl, brushMuted));
            segments.Add(($"{pw:0.0} W", brushPackage));
            float w = g.MeasureString($"{lbl}{pw:0.0} W", fontSmall).Width;
            _hot.Add((new RectangleF(pad, rowY, w, fontSmall.GetHeight(g)), T(PackageHelp), null));
        }
        if (ctx is not null)
        {
            if (segments.Count > 0) segments.Add(("   \u00B7   ", brushMuted));
            segments.Add((ctx.Describe(), ctx.SchemeIsBalanced ? brushMuted : brushAccent));
            if (ctx.Brightness is int b)
            {
                segments.Add(("   \u00B7   ", brushMuted));
                segments.Add((T("brightness {0} %", b), brushMuted));
            }
        }
        DrawSegments(g, pad, rowY, fontSmall, segments);

        // Chart
        float chartTop = rowY + 26 * s;
        float chartBottom = Height - pad - 40 * s;
        var chart = new RectangleF(pad, chartTop, Width - 2 * pad, chartBottom - chartTop);
        string yMaxLabel = DrawChart(g, chart, samples, span, accent, packageLine, grid, muted, fontSmall, s);
        _hot.Add((chart, null, CycleWindow));

        // Legend centred on the x-axis label row, between "-N min" and "now"
        bool hasPackage = samples.Any(x => x.PackageWatts is not null);
        float legendW = LegendWidth(g, fontSmall, T("battery"), s) + (hasPackage ? 10 * s + LegendWidth(g, fontSmall, T("package"), s) : 0);
        float axisLeft = chart.Left + g.MeasureString($"-{(int)span.TotalMinutes} min", fontSmall).Width + 8 * s;
        float axisRight = chart.Right - g.MeasureString($"{yMaxLabel} W", fontSmall).Width - 6 * s - g.MeasureString(T("now"), fontSmall).Width - 8 * s;
        float lx = axisLeft + (axisRight - axisLeft - legendW) / 2;
        float ly = chart.Bottom + 2 * s;
        lx = DrawLegend(g, lx, ly, fontSmall, brushMuted, accent, T("battery"), s);
        if (hasPackage) DrawLegend(g, lx + 10 * s, ly, fontSmall, brushMuted, packageLine, T("package"), s);

        // Footer: stats. The asterisk marks a mean of gauge samples, used until the window holds
        // enough continuous discharge for the capacity-drop average.
        if (samples.Count > 0)
        {
            float footY = Height - pad - 16 * s;
            string stats;
            var pkgSamples = packageMode ? History.BucketPackage(samples, Settings.PackageWindowSeconds).Where(v => v is not null).Select(v => v!.Value).ToList() : null;
            if (pkgSamples is { Count: > 0 })
            {
                stats = T("package avg {0} W   \u00B7   peak {1} W   \u00B7   min {2} W", $"{pkgSamples.Average():0.0}", $"{pkgSamples.Max():0.0}", $"{pkgSamples.Min():0.0}");
            }
            else
            {
                double? capAvg = _history.CapacityAverage(span);
                double avg = capAvg ?? samples.Average(x => Math.Abs(x.Watts));
                double peak = samples.Max(x => Math.Abs(x.Watts));
                double min = samples.Min(x => Math.Abs(x.Watts));
                stats = T("avg {0} W{1}   \u00B7   peak {2} W   \u00B7   min {3} W", $"{avg:0.0}", capAvg is null ? "*" : "", $"{peak:0.0}", $"{min:0.0}");
            }
            g.DrawString(stats, fontSmall, brushMuted, pad, footY);
        }
    }

    /// <summary>Draws a Segoe MDL2 glyph right-aligned at <paramref name="right"/>; returns its width.</summary>
    private static float DrawGlyphButton(Graphics g, string glyph, float right, float y, float h, Color colour, float s)
    {
        using var brush = new SolidBrush(colour);
        Font font;
        try { font = new Font("Segoe MDL2 Assets", 11f); }
        catch { font = new Font("Segoe UI Symbol", 11f); glyph = "\u2699"; }
        using (font)
        {
            var sz = g.MeasureString(glyph, font);
            g.DrawString(glyph, font, brush, right - sz.Width, y + (h - sz.Height) / 2);
            return sz.Width;
        }
    }

    /// <summary>Draws a small outlined text button right-aligned at <paramref name="right"/>; returns its width.</summary>
    private static float DrawPillButton(Graphics g, string text, Font font, float right, float y, float h, Color colour, Color border, float s)
    {
        var sz = g.MeasureString(text, font);
        float w = sz.Width + 12 * s, hh = h + 4 * s, x = right - w, yy = y - 2 * s;
        using var path = new GraphicsPath();
        float r = hh / 2;
        path.AddArc(x, yy, hh, hh, 90, 180);
        path.AddArc(x + w - hh, yy, hh, hh, 270, 180);
        path.CloseFigure();
        using var pen = new Pen(border);
        using var brush = new SolidBrush(colour);
        g.DrawPath(pen, path);
        g.DrawString(text, font, brush, x + 6 * s, y);
        return w;
    }

    private static void DrawSegments(Graphics g, float x, float y, Font font, List<(string text, Brush brush)> segments)
    {
        using var fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
        fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        foreach (var (text, brush) in segments)
        {
            g.DrawString(text, font, brush, x, y, fmt);
            x += g.MeasureString(text, font, PointF.Empty, fmt).Width;
        }
    }

    private static float LegendWidth(Graphics g, Font font, string label, float s) =>
        8 * s + 4 * s + g.MeasureString(label, font).Width;

    private static float DrawLegend(Graphics g, float x, float y, Font font, Brush text, Color colour, string label, float s)
    {
        using var b = new SolidBrush(colour);
        float box = 8 * s;
        g.FillRectangle(b, x, y + 4 * s, box, box);
        g.DrawString(label, font, text, x + box + 4 * s, y);
        return x + box + 4 * s + g.MeasureString(label, font).Width;
    }

    /// <summary>Draws the chart; returns the top axis label (without unit) so the caller knows the gutter width.</summary>
    private static string DrawChart(Graphics g, RectangleF r, List<Sample> samples, TimeSpan span, Color accent, Color package, Color grid, Color muted, Font font, float s)
    {
        // Axis follows the battery series; package bursts are clipped at the top so they cannot squash it.
        // On AC the battery series sits at zero, so the package series takes over the scale.
        double batteryMax = samples.Count > 0 ? samples.Max(x => Math.Abs(x.Watts)) : 0;
        var smoothed = History.BucketPackage(samples, Settings.PackageWindowSeconds);
        double packageMax = smoothed.Length > 0 ? smoothed.Max(v => v ?? 0) : 0;
        double maxW = batteryMax >= 1 ? batteryMax : Math.Max(Math.Max(batteryMax, packageMax), samples.Count > 0 ? 0 : 10);
        double step = NiceStep(maxW);
        double yMax = Math.Max(step, Math.Ceiling(maxW / step) * step);

        float gutter = g.MeasureString($"{yMax:0} W", font).Width + 6 * s;
        var full = r;
        r = new RectangleF(r.Left, r.Top, r.Width - gutter, r.Height);

        using var gridPen = new Pen(grid) { DashStyle = DashStyle.Dot };
        using var mutedBrush = new SolidBrush(muted);
        for (double v = 0; v <= yMax + 1e-9; v += step)
        {
            float gy = (float)(r.Bottom - v / yMax * r.Height);
            g.DrawLine(gridPen, r.Left, gy, r.Right, gy);
            string lbl = $"{v:0} W";
            var sz = g.MeasureString(lbl, font);
            g.DrawString(lbl, font, mutedBrush, full.Right - sz.Width, gy - sz.Height / 2);
        }
        g.DrawString($"-{(int)span.TotalMinutes} min", font, mutedBrush, r.Left, r.Bottom + 2 * s);
        var nowSz = g.MeasureString(T("now"), font);
        g.DrawString(T("now"), font, mutedBrush, r.Right - nowSz.Width, r.Bottom + 2 * s);

        string top = $"{yMax:0}";
        if (samples.Count < 2) return top;

        var now = DateTime.Now;
        float X(Sample smp) => (float)(r.Left + Math.Clamp(1.0 - (now - smp.Time).Ticks / (double)span.Ticks, 0, 1) * r.Width);
        float Y(double w) => (float)(r.Bottom - Math.Min(w, yMax) / yMax * r.Height);

        // Battery gauge: filled area + line, broken wherever samples are missing (standby, hibernation)
        using var fill = new LinearGradientBrush(r, Color.FromArgb(110, accent), Color.FromArgb(10, accent), LinearGradientMode.Vertical);
        using var line = new Pen(accent, 2 * s) { LineJoin = LineJoin.Round };
        using var dot = new SolidBrush(accent);
        float rad = 3 * s;
        foreach (var seg in Segments(samples.Select(x => (new PointF(X(x), Y(Math.Abs(x.Watts))), x.Time))))
        {
            if (seg.Length >= 2)
            {
                using var area = new GraphicsPath();
                area.AddLine(seg[0].X, r.Bottom, seg[0].X, seg[0].Y);
                area.AddLines(seg);
                area.AddLine(seg[^1].X, seg[^1].Y, seg[^1].X, r.Bottom);
                area.CloseFigure();
                g.FillPath(fill, area);
                g.DrawLines(line, seg);
            }
            else
            {
                g.FillEllipse(dot, seg[0].X - rad / 2, seg[0].Y - rad / 2, rad, rad);
            }
        }
        var lastPt = new PointF(X(samples[^1]), Y(Math.Abs(samples[^1].Watts)));
        g.FillEllipse(dot, lastPt.X - rad, lastPt.Y - rad, 2 * rad, 2 * rad);

        // Package power: thin line, only where present, with the same gap rule
        using var pline = new Pen(package, 1.5f * s) { LineJoin = LineJoin.Round };
        using var pdot = new SolidBrush(package);
        var ppts = samples.Select((x, i) => (x, v: smoothed[i])).Where(t => t.v is not null).Select(t => (new PointF(X(t.x), Y(t.v!.Value)), t.x.Time));
        foreach (var seg in Segments(ppts))
        {
            if (seg.Length >= 2) g.DrawLines(pline, seg);
            else g.FillEllipse(pdot, seg[0].X - rad / 2, seg[0].Y - rad / 2, rad, rad);
        }
        return top;
    }

    /// <summary>Samples arrive once a second; a longer silence means the machine was asleep, so the line breaks there.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(5);

    private static IEnumerable<PointF[]> Segments(IEnumerable<(PointF p, DateTime t)> points)
    {
        var seg = new List<PointF>();
        DateTime? prev = null;
        foreach (var (p, t) in points)
        {
            if (prev is DateTime pt && t - pt > MaxGap && seg.Count > 0)
            {
                yield return seg.ToArray();
                seg.Clear();
            }
            seg.Add(p);
            prev = t;
        }
        if (seg.Count > 0) yield return seg.ToArray();
    }

    private string StatusLine(Sample? last)
    {
        if (last is null) return T("waiting for data");
        var l = last.Value;
        var parts = new List<string>
        {
            T(l.State switch
            {
                PowerState.Discharging => "discharging",
                PowerState.Charging => "charging",
                PowerState.Idle when l.PackageWatts is not null => "CPU package, on AC",
                PowerState.Idle => "on AC",
                _ => "unknown",
            })
        };
        if (l.Percent is double p) parts.Add($"{p:0} %");

        var left = EstimateTimeLeft(l);
        if (left is TimeSpan t)
            parts.Add(t.TotalHours >= 1 ? T("{0} h {1} min left", (int)t.TotalHours, t.Minutes.ToString("00")) : T("{0} min left", t.Minutes));
        return string.Join("  \u00B7  ", parts);
    }

    /// <summary>
    /// Time left from the capacity drop over the last 10 minutes (falling back to the 5-minute gauge mean
    /// until enough history exists), rounded to 10-minute steps with hysteresis so it does not flicker.
    /// </summary>
    private TimeSpan? EstimateTimeLeft(Sample l)
    {
        var avg = _history.CapacityAverage(EstimateWindow) ?? _history.Average(TimeSpan.FromMinutes(5));
        if (l.State != PowerState.Discharging || l.RemainingMwh is not int rem || avg is not > 0.1)
            return _shownLeft = null;

        var raw = TimeSpan.FromHours(rem / 1000.0 / avg.Value);
        if (_shownLeft is TimeSpan shown && (raw - shown).Duration() < EstimateStep * 0.8)
            return shown;

        long steps = (long)Math.Round(raw.Ticks / (double)EstimateStep.Ticks);
        return _shownLeft = TimeSpan.FromTicks(Math.Max(1, steps) * EstimateStep.Ticks);
    }

    private static double NiceStep(double max)
    {
        double raw = Math.Max(max, 1) / 4;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double nice = norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10;
        return nice * mag;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
