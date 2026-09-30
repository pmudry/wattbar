using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace WattBar;

/// <summary>"Who is using it": per-process energy shares from the elevated collector's snapshot.</summary>
public sealed class OffendersForm : Form
{
    private readonly Label _status = new() { Dock = DockStyle.Top, AutoSize = false, Height = 44, Padding = new Padding(12, 10, 12, 0) };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, HeaderStyle = ColumnHeaderStyle.Clickable };
    private readonly Label _note = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 52, Padding = new Padding(12, 6, 12, 0), ForeColor = SystemColors.GrayText };
    private readonly Button _start = new() { Text = "Start collector (admin)", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    private readonly Button _stop = new() { Text = "Stop collector", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    private DateTime _lastRead = DateTime.MinValue;
    private int _sortColumn = 1;
    private bool _sortDescending = true;
    private E3Snapshot? _snap;

    public OffendersForm()
    {
        Text = "WattBar \u2013 who is using the battery";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(840, 480);
        MinimumSize = new Size(520, 320);
        ShowInTaskbar = true;

        _list.Columns.Add("Process", 260);
        _list.Columns.Add("CPU", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Total", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Screen", 70, HorizontalAlignment.Right);
        _list.Columns.Add("GPU", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Disk", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Network", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Other", 70, HorizontalAlignment.Right);
        _list.ColumnClick += (_, e) =>
        {
            if (e.Column == _sortColumn) _sortDescending = !_sortDescending;
            else { _sortColumn = e.Column; _sortDescending = e.Column != 0; }
            Render();
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 4, 8, 4), FlowDirection = FlowDirection.LeftToRight };
        _start.Click += (_, _) => StartCollector();
        _stop.Click += (_, _) => StopCollector();
        buttons.Controls.Add(_start);
        buttons.Controls.Add(_stop);

        _note.Text = "Shares of the Energy Estimation Engine's per-minute estimates (the data behind Task Manager's Power usage column), "
                   + "merged per process. Not watts. Screen energy is charged to the foreground window, so rank by CPU to find the real offender.";

        Controls.Add(_list);
        Controls.Add(_status);
        Controls.Add(buttons);
        Controls.Add(_note);

        _timer.Tick += (_, _) => Refresh(force: false);
        _timer.Start();
        Refresh(force: true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Keep the instance (and its sort state); the tray app re-shows it.
        e.Cancel = true;
        Hide();
        base.OnFormClosing(e);
    }

    private void StartCollector()
    {
        if (Environment.ProcessPath is not string exe) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe, $"--collect-e3 {Environment.ProcessId}") { UseShellExecute = true, Verb = "runas" });
            _status.Text = "Collector starting, first batch in about a minute\u2026";
        }
        catch (Win32Exception)
        {
            _status.Text = "Elevation was refused. The collector needs administrator rights to read the ETW provider.";
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

    private void Refresh(bool force)
    {
        try
        {
            var fi = new FileInfo(E3Collector.SnapshotPath);
            if (!fi.Exists)
            {
                _snap = null;
                _status.Text = "Collector not running. Start it to see which processes are using the battery (administrator rights needed).";
                _start.Enabled = true; _stop.Enabled = false;
                _list.Items.Clear();
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
        if (_snap is null) return;
        bool live = _snap.Status == "collecting";
        var age = DateTime.Now - _snap.Updated;
        _start.Enabled = !live || age > TimeSpan.FromMinutes(3);
        _stop.Enabled = live;

        _status.Text = _snap.Status switch
        {
            "collecting" when _snap.Apps.Count == 0 => "Collecting, first batch in about a minute\u2026",
            "collecting" when age > TimeSpan.FromMinutes(3) => $"Last batch {_snap.Updated:HH:mm:ss}, no update since: the collector may have stopped.",
            "collecting" => $"Last minute, batch of {_snap.Updated:HH:mm:ss} \u00B7 {_snap.Apps.Count} processes with energy \u00B7 sorted by {_list.Columns[_sortColumn].Text}",
            "starting" => "Collector starting\u2026",
            "stopped" when _snap.Apps.Count > 0 => $"Collector stopped; showing its last batch of {_snap.Updated:HH:mm:ss}.",
            "stopped" => "Collector stopped. Start it again to resume.",
            _ => "Collector error: " + _snap.Message,
        };
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
            Func<E3App, double> key = _sortColumn switch
            {
                1 => a => a.Cpu, 2 => a => a.Total, 3 => a => a.Display, 4 => a => a.Gpu, 5 => a => a.Disk, 6 => a => a.Network, 7 => a => a.Other, _ => a => 0,
            };
            IEnumerable<E3App> apps = _sortColumn == 0
                ? (_sortDescending ? _snap.Apps.OrderByDescending(a => a.Name) : _snap.Apps.OrderBy(a => a.Name))
                : (_sortDescending ? _snap.Apps.OrderByDescending(key) : _snap.Apps.OrderBy(key));

            foreach (var a in apps)
            {
                var item = new ListViewItem(a.Name);
                item.SubItems.Add(Pct(a.Cpu / cpu));
                item.SubItems.Add(Pct(a.Total / total));
                item.SubItems.Add(Pct(a.Display / total));
                item.SubItems.Add(Pct(a.Gpu / total));
                item.SubItems.Add(Pct(a.Disk / total));
                item.SubItems.Add(Pct(a.Network / total));
                item.SubItems.Add(Pct((a.Npu + a.Other) / total));
                _list.Items.Add(item);
            }
        }
        _list.EndUpdate();
    }

    private static string Pct(double share) => share < 0.0005 ? "" : $"{share * 100:0.0} %";
}
