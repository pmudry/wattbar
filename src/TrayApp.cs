namespace WattBar;

/// <summary>Owns the tray icon, the sampling timer and the flyout.</summary>
public sealed class TrayApp : ApplicationContext
{
    private static readonly TimeSpan SparkSpan = TimeSpan.FromMinutes(2);
    private const int ContextEveryTicks = 5;

    private readonly NotifyIcon _icon;
    private readonly History _history = new(capacity: 3600 * 2);
    private readonly PackagePower _package = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly FlyoutForm _flyout;
    private readonly ToolStripMenuItem _startupItem;
    private PowerContext? _context;
    private OffendersForm? _offenders;
    private int _ticks;
    private int _pinAttempts;
    private readonly EventWaitHandle _showEvent = new(false, EventResetMode.AutoReset, Program.ShowEventName);
    private RegisteredWaitHandle? _showWait;
    private readonly EventWaitHandle _whoEvent = new(false, EventResetMode.AutoReset, Program.WhoEventName);
    private RegisteredWaitHandle? _whoWait;

    public TrayApp(bool showFlyout = false, bool showOffenders = false)
    {
        _flyout = new FlyoutForm(_history, () => _context);

        var menu = new ContextMenuStrip();
        var show = new ToolStripMenuItem("Show chart", null, (_, _) => ToggleFlyout()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup()) { Checked = Startup.IsEnabled() };
        menu.Items.Add(show);
        menu.Items.Add(new ToolStripMenuItem("Who is using it\u2026", null, (_, _) => ShowOffenders()));
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

        // Another launch with --show signals this event; hop back onto the UI thread to open the flyout.
        _ = _flyout.Handle;
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => _flyout.BeginInvoke(_flyout.ShowAtTray), null, -1, executeOnlyOnce: false);
        _whoWait = ThreadPool.RegisterWaitForSingleObject(_whoEvent, (_, _) => _flyout.BeginInvoke(ShowOffenders), null, -1, executeOnlyOnce: false);

        if (showFlyout) _flyout.ShowAtTray();
        if (showOffenders) ShowOffenders();
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

        sample = sample with { PackageWatts = _package.Read() };
        _history.Add(sample);

        if (_ticks++ % ContextEveryTicks == 0)
        {
            try { _context = PowerContext.Read(); }
            catch { _context = null; }
        }

        // The gauge is already a one-minute rolling average that steps every 30-60 s, so the digits
        // show it raw. The sparkline prefers package power, the only instantaneous signal.
        int size = SystemInformation.SmallIconSize.Width;
        int bins = Math.Max(8, size / 2);
        var spark = _package.Available
            ? _history.Bins(SparkSpan, bins, x => x.PackageWatts)
            : _history.Bins(SparkSpan, bins);
        var old = _icon.Icon;
        _icon.Icon = TrayIconRenderer.Render(size, sample.Watts, sample.State, spark, Theme.TaskbarIsLight());
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
        string pct = s.Percent is double p ? $" \u00B7 {p:0} %" : "";
        string pkg = s.PackageWatts is double w ? $"\npackage {w:0.0} W" : "";
        string ctx = _context is not null ? $"\n{_context.Describe()}" : "";
        return $"{Math.Abs(s.Watts):0.0} W {state}{pct}{pkg}{ctx}";
    }

    private void ToggleFlyout()
    {
        if (_flyout.Visible) _flyout.Hide();
        else _flyout.ShowAtTray();
    }

    private void ShowOffenders()
    {
        _offenders ??= new OffendersForm();
        _offenders.Show();
        _offenders.WindowState = FormWindowState.Normal;
        _offenders.Activate();
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
        _showWait?.Unregister(null);
        _showEvent.Dispose();
        _whoWait?.Unregister(null);
        _whoEvent.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _flyout.Dispose();
        _offenders?.Dispose();
        _package.Dispose();
        ExitThread();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
