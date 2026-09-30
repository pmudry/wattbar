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
        _flyout = new FlyoutForm(_history, () => _context, ShowOffenders, (owner, at) => BuildMenu(forFlyout: true).Show(owner, at));

        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup()) { Checked = Startup.IsEnabled() };

        _icon = new NotifyIcon
        {
            ContextMenuStrip = BuildMenu(forFlyout: false),
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
        // On AC with an idle battery the gauge reads zero; the CPU package is the only measured figure, so show it.
        bool light = Theme.TaskbarIsLight();
        double? pkgNow = _history.PackageNow();
        bool packageMode = sample.State == PowerState.Idle && pkgNow is double;
        var old = _icon.Icon;
        _icon.Icon = packageMode
            ? TrayIconRenderer.Render(size, pkgNow!.Value, PowerState.Discharging, spark, light, digitColor: Theme.Package(!light))
            : TrayIconRenderer.Render(size, sample.Watts, sample.State, spark, light);
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
        double? pkgNow = _history.PackageNow();
        string pkg = pkgNow is double w ? $"\nCPU package {w:0.0} W" : "";
        string ctx = _context is not null ? $"\n{_context.Describe()}" : "";
        if (s.State == PowerState.Idle && pkgNow is double pw)
            return $"CPU package {pw:0.0} W (on AC, battery idle){pct}{ctx}";
        return $"{Math.Abs(s.Watts):0.0} W {state}{pct}{pkg}{ctx}";
    }

    private void ToggleFlyout()
    {
        if (_flyout.Visible) { _flyout.Hide(); return; }
        // The mouse-down on the tray icon already deactivated and hid the flyout; that click means "close".
        if (DateTime.Now - _flyout.HiddenAt < TimeSpan.FromMilliseconds(500)) return;
        _flyout.ShowAtTray();
    }

    /// <summary>Tray menu, or the settings menu behind the flyout's gear (same items minus "Show chart").</summary>
    private ContextMenuStrip BuildMenu(bool forFlyout)
    {
        var menu = new ContextMenuStrip();
        if (!forFlyout)
        {
            menu.Items.Add(new ToolStripMenuItem("Show chart", null, (_, _) => ToggleFlyout()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) });
            menu.Items.Add(new ToolStripMenuItem("Who is using it\u2026", null, (_, _) => ShowOffenders()));
        }

        var theme = new ToolStripMenuItem("Theme");
        foreach (var mode in Enum.GetValues<Theme.Mode>())
        {
            var item = new ToolStripMenuItem(mode == Theme.Mode.System ? "Follow Windows" : mode.ToString()) { Checked = Theme.Selected == mode };
            item.Click += (_, _) => SetTheme(mode);
            theme.DropDownItems.Add(item);
        }
        menu.Items.Add(theme);

        var readout = new ToolStripMenuItem("Package readout");
        foreach (int secs in Settings.PackageWindows)
        {
            var item = new ToolStripMenuItem(secs == 1 ? "Every second, raw" : $"{secs} s moving average") { Checked = Settings.PackageWindowSeconds == secs };
            item.Click += (_, _) => { Settings.PackageWindowSeconds = secs; _flyout.Invalidate(); RebuildMenu(); };
            readout.DropDownItems.Add(item);
        }
        menu.Items.Add(readout);

        _startupItem.Checked = Startup.IsEnabled();
        menu.Items.Add(forFlyout ? new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup()) { Checked = Startup.IsEnabled() } : _startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("About WattBar\u2026", null, (_, _) => ShowAbout()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Exit()));
        return menu;
    }

    private void SetTheme(Theme.Mode mode)
    {
        Theme.Selected = mode;
        try
        {
#pragma warning disable WFO5001
            Application.SetColorMode(Theme.ColorMode);
#pragma warning restore WFO5001
        }
        catch { /* applies to new windows only */ }
        // Custom-painted windows repaint; framework-drawn ones are rebuilt on next use.
        _flyout.Invalidate();
        _offenders?.Dispose();
        _offenders = null;
        RebuildMenu();
    }

    private void RebuildMenu()
    {
        _icon.ContextMenuStrip?.Dispose();
        _icon.ContextMenuStrip = BuildMenu(forFlyout: false);
    }

    private void ShowAbout()
    {
        using var about = new AboutForm();
        about.ShowDialog();
    }

    private void ShowOffenders()
    {
        _offenders ??= new OffendersForm(() => _history.PackageAverage(TimeSpan.FromMinutes(1)));
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
