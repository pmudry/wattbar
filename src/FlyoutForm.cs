using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WattBar;

/// <summary>Borderless popup anchored above the tray that plots recent battery power.</summary>
public sealed class FlyoutForm : Form
{
    private static readonly TimeSpan[] Windows = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];

    private readonly History _history;
    private int _windowIndex = 1;

    private const int BaseWidth = 380;
    private const int BaseHeight = 250;

    public FlyoutForm(History history)
    {
        _history = history;

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
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
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
        _ = Handle; // make sure DeviceDpi is known
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

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Hide();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            _windowIndex = (_windowIndex + 1) % Windows.Length;
            Invalidate();
        }
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

        using var fontSmall = new Font("Segoe UI", 9f);
        using var fontBig = new Font("Segoe UI", 22f, FontStyle.Bold);
        using var brushFg = new SolidBrush(fg);
        using var brushMuted = new SolidBrush(muted);

        // Header
        g.DrawString("Battery power", fontSmall, brushMuted, pad, pad);
        string windowLabel = $"last {(int)span.TotalMinutes} min  ▾";
        var wl = g.MeasureString(windowLabel, fontSmall);
        g.DrawString(windowLabel, fontSmall, brushMuted, Width - pad - wl.Width, pad);

        // Big number + status
        float y = pad + 20 * s;
        string big = last is null ? "--" : $"{Math.Abs(last.Value.Watts):0.0} W";
        g.DrawString(big, fontBig, brushFg, pad - 2 * s, y);
        var bigSize = g.MeasureString(big, fontBig);
        g.DrawString(StatusLine(last), fontSmall, brushMuted, pad + bigSize.Width + 4 * s, y + bigSize.Height - 22 * s);

        // Chart
        var chart = new RectangleF(pad, y + bigSize.Height + 6 * s, Width - 2 * pad, Height - (y + bigSize.Height + 6 * s) - pad - 42 * s);
        DrawChart(g, chart, samples, span, accent, grid, muted, fontSmall, s);

        // Footer stats
        if (samples.Count > 0)
        {
            double avg = samples.Average(x => Math.Abs(x.Watts));
            double peak = samples.Max(x => Math.Abs(x.Watts));
            double min = samples.Min(x => Math.Abs(x.Watts));
            g.DrawString($"avg {avg:0.0} W   ·   peak {peak:0.0} W   ·   min {min:0.0} W", fontSmall, brushMuted, pad, Height - pad - 16 * s);
        }
    }

    private void DrawChart(Graphics g, RectangleF r, List<Sample> samples, TimeSpan span, Color accent, Color grid, Color muted, Font font, float s)
    {
        double maxW = samples.Count > 0 ? samples.Max(x => Math.Abs(x.Watts)) : 10;
        double step = NiceStep(maxW);
        double yMax = Math.Max(step, Math.Ceiling(maxW / step) * step);

        // Reserve a gutter on the right for the axis labels so the plot never runs under them.
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
        var nowSz = g.MeasureString("now", font);
        g.DrawString("now", font, mutedBrush, r.Right - nowSz.Width, r.Bottom + 2 * s);

        if (samples.Count < 2) return;

        var now = DateTime.Now;
        var pts = new PointF[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            double tx = 1.0 - (now - samples[i].Time).Ticks / (double)span.Ticks;
            float px = (float)(r.Left + Math.Clamp(tx, 0, 1) * r.Width);
            float py = (float)(r.Bottom - Math.Abs(samples[i].Watts) / yMax * r.Height);
            pts[i] = new PointF(px, py);
        }

        using (var area = new GraphicsPath())
        {
            area.AddLine(pts[0].X, r.Bottom, pts[0].X, pts[0].Y);
            area.AddLines(pts);
            area.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, r.Bottom);
            area.CloseFigure();
            using var fill = new LinearGradientBrush(r, Color.FromArgb(110, accent), Color.FromArgb(10, accent), LinearGradientMode.Vertical);
            g.FillPath(fill, area);
        }

        using var line = new Pen(accent, 2 * s) { LineJoin = LineJoin.Round };
        g.DrawLines(line, pts);

        using var dot = new SolidBrush(accent);
        float rad = 3 * s;
        g.FillEllipse(dot, pts[^1].X - rad, pts[^1].Y - rad, 2 * rad, 2 * rad);
    }

    private string StatusLine(Sample? last)
    {
        if (last is null) return "waiting for data";
        var l = last.Value;
        var parts = new List<string>
        {
            l.State switch
            {
                PowerState.Discharging => "discharging",
                PowerState.Charging => "charging",
                PowerState.Idle => "on AC",
                _ => "unknown",
            }
        };
        if (l.Percent is double p) parts.Add($"{p:0} %");

        var left = EstimateTimeLeft(l);
        if (left is TimeSpan t)
            parts.Add(t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min left" : $"{t.Minutes} min left");
        return string.Join("  ·  ", parts);
    }

    private static readonly TimeSpan EstimateWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EstimateStep = TimeSpan.FromMinutes(10);
    private TimeSpan? _shownLeft;

    /// <summary>
    /// Time left from the 5-minute average drain, rounded to 10-minute steps. The shown value only
    /// moves once the raw estimate has drifted most of a step away, so it does not flicker at boundaries.
    /// </summary>
    private TimeSpan? EstimateTimeLeft(Sample l)
    {
        var avg = _history.Average(EstimateWindow);
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
