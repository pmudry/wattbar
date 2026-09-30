namespace WattBar;

/// <summary>Owns the tray icon, the sampling timer and the flyout.</summary>
public sealed class TrayApp : ApplicationContext
{
    private const double EmaAlpha = 0.3;
    private static readonly TimeSpan SparkSpan = TimeSpan.FromMinutes(2);

    private readonly NotifyIcon _icon;
    private readonly History _history = new(capacity: 3600 * 2);
    private readonly System.Windows.Forms.Timer _timer;
    private readonly FlyoutForm _flyout;
    private readonly ToolStripMenuItem _startupItem;
    private double? _ema;
    private int _pinAttempts;

    public TrayApp(bool showFlyout = false)
    {
        _flyout = new FlyoutForm(_history);

        var menu = new ContextMenuStrip();
        var show = new ToolStripMenuItem("Show chart", null, (_, _) => ToggleFlyout()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup()) { Checked = Startup.IsEnabled() };
        menu.Items.Add(show);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Exit()));

        _icon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "WattBar",
            Icon = SystemIcons.Application,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleFlyout(); };

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
        if (showFlyout) _flyout.ShowAtTray();
    }

    private void Tick()
    {
        Sample sample;
        try
        {
            sample = BatteryReader.Read();
        }
        catch (Exception ex)
        {
            _icon.Text = "WattBar: " + Truncate(ex.Message, 100);
            return;
        }

        _history.Add(sample);
        double w = Math.Abs(sample.Watts);
        _ema = _ema is null ? w : EmaAlpha * w + (1 - EmaAlpha) * _ema.Value;

        int size = SystemInformation.SmallIconSize.Width;
        var spark = _history.Bins(SparkSpan, Math.Max(8, size / 2));
        var old = _icon.Icon;
        _icon.Icon = TrayIconRenderer.Render(size, _ema.Value, sample.State, spark, Theme.TaskbarIsLight());
        if (old is not null && old != SystemIcons.Application) old.Dispose();

        _icon.Text = Truncate(Tooltip(sample), 127);

        if (_flyout.Visible) _flyout.Invalidate();

        // Explorer creates our NotifyIconSettings entry shortly after the icon appears; retry a few times.
        if (_pinAttempts < 15 && !TrayPin.Promote()) _pinAttempts++;
        else _pinAttempts = 15;
    }

    private string Tooltip(Sample s)
    {
        string state = s.State switch
        {
            PowerState.Discharging => "discharging",
            PowerState.Charging => "charging",
            PowerState.Idle => "on AC",
            _ => "unknown",
        };
        string pct = s.Percent is double p ? $" · {p:0} %" : "";
        var avg = _history.Average(TimeSpan.FromMinutes(1));
        string avgText = avg is double a ? $"\navg 1 min: {a:0.0} W" : "";
        return $"{Math.Abs(s.Watts):0.0} W {state}{pct}{avgText}";
    }

    private void ToggleFlyout()
    {
        if (_flyout.Visible) _flyout.Hide();
        else _flyout.ShowAtTray();
    }

    private void ToggleStartup()
    {
        bool enable = !Startup.IsEnabled();
        Startup.Set(enable);
        _startupItem.Checked = enable;
    }

    private void Exit()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _flyout.Dispose();
        ExitThread();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
