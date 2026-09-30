using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace WattBar;

/// <summary>"Who is using it": per-process energy shares from the elevated collector's snapshot.</summary>
public sealed class OffendersForm : Form
{
    private const int ColProcess = 0, ColState = 1, ColCpu = 2, ColWatts = 3, ColTotal = 4, ColScreen = 5, ColGpu = 6, ColDisk = 7, ColNet = 8, ColOther = 9;
    private const double SuspectCpuShare = 0.10;

    private readonly Func<double?> _packageWatts;
    private readonly Label _status = new() { Dock = DockStyle.Top, AutoSize = false, Padding = new Padding(12, 10, 12, 0) };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Clickable };
    private readonly Label _guide = new() { Dock = DockStyle.Bottom, AutoSize = false, Padding = new Padding(12, 8, 12, 0), ForeColor = SystemColors.GrayText };
    private readonly Label _why = new() { Dock = DockStyle.Bottom, AutoSize = false, Padding = new Padding(12, 8, 12, 0) };
    private readonly RadioButton _modeAsk = new() { Text = "Ask for administrator consent each time the collector starts (UAC prompt)", AutoSize = true };
    private readonly RadioButton _modeTask = new() { Text = "Install a scheduled task once (one prompt), then start it without prompts", AutoSize = true };
    private readonly RadioButton _modeOff = new() { Text = "Don\u2019t collect per-process data. Nothing else in WattBar needs administrator rights.", AutoSize = true };
    private readonly Button _start = new() { AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    private readonly Button _stop = new() { Text = "Stop collector", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    private readonly Button _removeTask = new() { Text = "Remove scheduled task", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    private DateTime _lastRead = DateTime.MinValue;
    private int _sortColumn = ColCpu;
    private bool _sortDescending = true;
    private E3Snapshot? _snap;
    private bool _taskInstalled;
    private bool _busy;

    public OffendersForm(Func<double?> packageWatts)
    {
        _packageWatts = packageWatts;

        Text = "WattBar \u2013 who is using the battery";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = true;

        _list.Columns.Add("Process", 250);
        _list.Columns.Add("State", 100);
        _list.Columns.Add("CPU share", 85, HorizontalAlignment.Right);
        _list.Columns.Add("\u2248 CPU W", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Total share", 85, HorizontalAlignment.Right);
        _list.Columns.Add("Screen", 70, HorizontalAlignment.Right);
        _list.Columns.Add("GPU", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Disk", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Network", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Other", 60, HorizontalAlignment.Right);
        _list.ColumnClick += (_, e) =>
        {
            if (e.Column == _sortColumn) _sortDescending = !_sortDescending;
            else { _sortColumn = e.Column; _sortDescending = e.Column > ColState; }
            Render();
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 4, 8, 4), FlowDirection = FlowDirection.LeftToRight };
        _start.Click += (_, _) => StartCollector();
        _stop.Click += (_, _) => StopCollector();
        _removeTask.Click += (_, _) => RemoveTask();
        buttons.Controls.Add(_start);
        buttons.Controls.Add(_stop);
        buttons.Controls.Add(_removeTask);

        var modes = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 0, 8, 4), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        modes.Controls.Add(_modeAsk);
        modes.Controls.Add(_modeTask);
        modes.Controls.Add(_modeOff);
        switch (E3Task.Mode)
        {
            case E3Mode.Task: _modeTask.Checked = true; break;
            case E3Mode.Off: _modeOff.Checked = true; break;
            default: _modeAsk.Checked = true; break;
        }
        foreach (var rb in new[] { _modeAsk, _modeTask, _modeOff })
            rb.CheckedChanged += (_, _) => { if (rb.Checked) { E3Task.Mode = CurrentMode; UpdateButtons(); } };

        _why.Text =
            "Why administrator rights?  Windows only lets administrators read the Energy Estimation Engine trace that these numbers come from. "
          + "WattBar therefore runs a small separate collector with elevated rights; it only reads that trace and writes a summary file. Choose how it may start:";

        _guide.Text =
            "How to read this.  Every minute Windows estimates how much energy each process caused (the numbers behind Task Manager\u2019s "
          + "\u201cPower usage\u201d column). Shares are that process\u2019s part of everything attributed to processes during that minute.\r\n"
          + "Finding an offender.  Sort by CPU share. A Background or Minimized process that stays near the top minute after minute is draining "
          + "the battery without you using it; such rows are magenta. Focus and Visible rows are what you are working in.\r\n"
          + "\u2248 CPU W spreads the measured CPU package power of the minute across processes by CPU share: an estimate, not a measurement. "
          + "Screen energy always goes to the window in front.";

        Controls.Add(_list);
        Controls.Add(_status);
        Controls.Add(buttons);
        Controls.Add(modes);
        Controls.Add(_why);
        Controls.Add(_guide);

        // Automatic DPI scaling does not fire for a form built in code, so scale by hand.
        _ = Handle;
        float f = DeviceDpi / 96f;
        Size = new Size((int)(1000 * f), (int)(720 * f));
        MinimumSize = new Size((int)(760 * f), (int)(480 * f));
        _status.Height = (int)(40 * f);
        _why.Height = (int)(48 * f);
        _guide.Height = (int)(124 * f);
        foreach (ColumnHeader c in _list.Columns) c.Width = (int)(c.Width * f);

        _taskInstalled = E3Task.IsInstalled();
        _timer.Tick += (_, _) => Refresh(force: false);
        _timer.Start();
        Refresh(force: true);
    }

    private E3Mode CurrentMode => _modeTask.Checked ? E3Mode.Task : _modeOff.Checked ? E3Mode.Off : E3Mode.Ask;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Keep the instance (and its sort state); the tray app re-shows it.
        e.Cancel = true;
        Hide();
        base.OnFormClosing(e);
    }

    private void StartCollector()
    {
        if (_busy || Environment.ProcessPath is not string exe) return;
        switch (CurrentMode)
        {
            case E3Mode.Off:
                _status.Text = "Per-process collection is switched off. Pick another option below to use it.";
                return;

            case E3Mode.Task:
                _busy = true;
                try
                {
                    if (!_taskInstalled)
                    {
                        _status.Text = "Registering the scheduled task, please confirm the prompt\u2026";
                        Application.DoEvents();
                        if (!E3Task.InstallElevated())
                        {
                            _status.Text = "The task was not installed (consent refused or registration failed). You can still use the UAC option.";
                            return;
                        }
                        _taskInstalled = E3Task.IsInstalled();
                    }
                    _status.Text = E3Task.Run()
                        ? "Collector started through the scheduled task, first batch in about a minute\u2026"
                        : "Could not start the scheduled task. Try removing and reinstalling it.";
                }
                finally
                {
                    _busy = false;
                    UpdateButtons();
                }
                return;

            default:
                try
                {
                    Process.Start(new ProcessStartInfo(exe, $"--collect-e3 {Environment.ProcessId}") { UseShellExecute = true, Verb = "runas" });
                    _status.Text = "Collector starting, first batch in about a minute\u2026";
                }
                catch (Win32Exception)
                {
                    _status.Text = "Consent was refused. Without it the collector cannot read the trace.";
                }
                return;
        }
    }

    private void StopCollector()
    {
        try
        {
            File.WriteAllText(E3Collector.StopPath, "");
            _status.Text = "Stopping collector\u2026";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not signal the collector: " + ex.Message;
        }
    }

    private void RemoveTask()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _status.Text = E3Task.UninstallElevated() ? "Scheduled task removed." : "The task was not removed (consent refused or it failed).";
            _taskInstalled = E3Task.IsInstalled();
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        bool live = _snap?.Status == "collecting" && DateTime.Now - (_snap?.Updated ?? DateTime.MinValue) <= TimeSpan.FromMinutes(3);
        var mode = CurrentMode;
        _start.Text = mode switch
        {
            E3Mode.Task when !_taskInstalled => "Install task and start collector (one prompt)",
            E3Mode.Task => "Start collector",
            _ => "Start collector (UAC prompt)",
        };
        _start.Enabled = mode != E3Mode.Off && !live && !_busy;
        _stop.Enabled = live;
        _removeTask.Visible = _taskInstalled;
        _removeTask.Enabled = !_busy;
    }

    private void Refresh(bool force)
    {
        try
        {
            var fi = new FileInfo(E3Collector.SnapshotPath);
            if (!fi.Exists)
            {
                _snap = null;
                _status.Text = CurrentMode == E3Mode.Off
                    ? "Per-process collection is switched off."
                    : "Collector not running. Start it to see which processes are using the battery.";
                _list.Items.Clear();
                UpdateButtons();
                return;
            }
            if (!force && fi.LastWriteTime == _lastRead) { UpdateStatusText(); return; }
            _lastRead = fi.LastWriteTime;
            _snap = JsonSerializer.Deserialize<E3Snapshot>(File.ReadAllText(fi.FullName), E3Collector.JsonOptions);
            Render();
        }
        catch (Exception ex)
        {
            _status.Text = "Cannot read snapshot: " + ex.Message;
        }
    }

    private void UpdateStatusText()
    {
        UpdateButtons();
        if (_snap is null) return;
        var age = DateTime.Now - _snap.Updated;

        int suspects = _snap.Apps.Count(IsSuspect(_snap));
        string pkg = _packageWatts() is double w ? $" \u00B7 CPU package {w:0.0} W over the minute" : "";
        string verdict = suspects == 0 ? " \u00B7 no heavy background process" : $" \u00B7 {suspects} heavy background process{(suspects > 1 ? "es" : "")} highlighted";

        _status.Text = _snap.Status switch
        {
            "collecting" when _snap.Apps.Count == 0 => "Collecting, first batch in about a minute\u2026",
            "collecting" when age > TimeSpan.FromMinutes(3) => $"Last batch {_snap.Updated:HH:mm:ss}, no update since: the collector may have stopped.",
            "collecting" => $"Minute ending {_snap.Updated:HH:mm:ss} \u00B7 {_snap.Apps.Count} processes{pkg}{verdict}",
            "starting" => "Collector starting\u2026",
            "stopped" when _snap.Apps.Count > 0 => $"Collector stopped; showing its last minute ({_snap.Updated:HH:mm:ss}){verdict}.",
            "stopped" => "Collector stopped. Start it again to resume.",
            _ => "Collector error: " + _snap.Message,
        };
    }

    private static Func<E3App, bool> IsSuspect(E3Snapshot snap)
    {
        double cpu = Math.Max(snap.Apps.Sum(a => a.Cpu), 1e-9);
        return a => a.Cpu / cpu >= SuspectCpuShare && a.State is "Background" or "Minimized";
    }

    private void Render()
    {
        UpdateStatusText();
        _list.BeginUpdate();
        _list.Items.Clear();
        if (_snap is { Apps.Count: > 0 })
        {
            double total = Math.Max(_snap.Apps.Sum(a => a.Total), 1e-9);
            double cpu = Math.Max(_snap.Apps.Sum(a => a.Cpu), 1e-9);
            double? pkgW = _packageWatts();
            var suspect = IsSuspect(_snap);

            Func<E3App, IComparable> key = _sortColumn switch
            {
                ColProcess => a => a.Name,
                ColState => a => a.State,
                ColCpu or ColWatts => a => a.Cpu,
                ColTotal => a => a.Total,
                ColScreen => a => a.Display,
                ColGpu => a => a.Gpu,
                ColDisk => a => a.Disk,
                ColNet => a => a.Network,
                _ => a => a.Other,
            };
            var apps = _sortDescending ? _snap.Apps.OrderByDescending(key) : _snap.Apps.OrderBy(key);

            foreach (var a in apps)
            {
                double cpuShare = a.Cpu / cpu;
                var item = new ListViewItem(a.Name);
                item.SubItems.Add(a.State);
                item.SubItems.Add(Pct(cpuShare));
                item.SubItems.Add(pkgW is double w && cpuShare >= 0.0005 ? $"{cpuShare * w:0.00}" : "");
                item.SubItems.Add(Pct(a.Total / total));
                item.SubItems.Add(Pct(a.Display / total));
                item.SubItems.Add(Pct(a.Gpu / total));
                item.SubItems.Add(Pct(a.Disk / total));
                item.SubItems.Add(Pct(a.Network / total));
                item.SubItems.Add(Pct((a.Npu + a.Other) / total));
                if (suspect(a)) item.ForeColor = Theme.AppsAreDark() ? Theme.MagentaDark : Theme.MagentaLight;
                _list.Items.Add(item);
            }
        }
        _list.EndUpdate();
    }

    private static string Pct(double share) => share < 0.0005 ? "" : $"{share * 100:0.0} %";
}
