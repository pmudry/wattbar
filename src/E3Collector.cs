using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace WattBar;

public sealed class E3App
{
    public string Name { get; set; } = "";
    public double Cpu { get; set; }
    public double Gpu { get; set; }
    public double Display { get; set; }
    public double Disk { get; set; }
    public double Network { get; set; }
    public double Npu { get; set; }
    public double Other { get; set; }
    /// <summary>Most interactive state seen this minute: Focus, Visible, Minimized or Background.</summary>
    public string State { get; set; } = "Background";
    public double Total => Cpu + Gpu + Display + Disk + Network + Npu + Other;
}

public sealed class E3Snapshot
{
    public string Status { get; set; } = "starting";   // starting | collecting | stopped | error
    public string? Message { get; set; }
    public DateTime Updated { get; set; }
    public int IntervalMs { get; set; }
    public string? Flags { get; set; }
    public double PackageMeter { get; set; }
    public List<E3App> Apps { get; set; } = [];
}

/// <summary>
/// Elevated side process: subscribes to the Energy Estimation Engine ETW provider (the data behind
/// Task Manager's "Power usage" column), merges each one-minute batch per process and writes it as JSON
/// to ProgramData for the unelevated tray app to display. Exits when the parent process goes away or
/// when a "stop" file appears.
/// </summary>
public static class E3Collector
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WattBar");
    public static readonly string SnapshotPath = Path.Combine(Dir, "e3.json");
    public static readonly string StopPath = Path.Combine(Dir, "stop");
    private static readonly string LogPath = Path.Combine(Dir, "e3.log");

    private static readonly Guid Provider = new("ddcc3826-a68a-4e0d-bcfd-9c06c27c6948");
    private const string SessionName = "WattBar-E3";

    // EnergyEstimate (event 37) payload, as observed live: AppName, UserId, CpuEnergy, GpuEnergy, DisplayEnergy,
    // DiskEnergy, NetworkEnergy, MbbEnergy, LossEnergy, OtherEnergy, EmiEnergy, ForInternalUse, TimeInMSec,
    // RecordFlags, RecordMeasured, InteractivityState, Committed, WorkOnBehalfCPUEnergy, AttributedCPUEnergy, NpuEnergy.

    private static readonly Regex PackagedSuffix = new(@"_[\d.]+_[a-z0-9]+__\w+$", RegexOptions.Compiled);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, E3App> Batch = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastEnergyEvent = DateTime.MinValue;
    private static int _intervalMs;
    private static string? _flags;
    private static double _packageMeter;
    private static long _events, _energyEvents;
    private static List<E3App> _lastApps = [];
    private static readonly HashSet<string> LoggedShapes = [];

    public static int Run(int parentPid)
    {
        try
        {
            PrepareDirectory();
            File.WriteAllText(LogPath, "");
            Log($"collector start, parent pid {parentPid}, elevated {IsElevated()}");

            if (!IsElevated())
            {
                WriteSnapshot(new E3Snapshot { Status = "error", Message = "The collector must run elevated.", Updated = DateTime.Now });
                return 2;
            }
            if (File.Exists(StopPath)) File.Delete(StopPath);
            WriteSnapshot(new E3Snapshot { Status = "starting", Updated = DateTime.Now });

            using var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            // Keyword 0x4 (EnergyEstimation) carries the per-minute batch; the other keywords add thousands of
            // CPU/frequency events a minute that we never read. Everything is still parsed by TraceEvent, so
            // fewer events means less CPU for the collector itself.
            session.EnableProvider(Provider, TraceEventLevel.Verbose, 0x4);
            session.Source.Dynamic.All += OnEvent;
            session.Source.UnhandledEvents += OnEvent;
            var pump = Task.Run(() => session.Source.Process());
            Log("session enabled");
            WriteSnapshot(new E3Snapshot { Status = "collecting", Message = "waiting for the first batch", Updated = DateTime.Now });

            // Started by the tray app: follow that process. Started by the scheduled task: stay while any
            // WattBar instance exists, with a grace period so a quick restart of the tray app does not end us.
            DateTime? noTraySince = null;
            int loop = 0;
            while (true)
            {
                Thread.Sleep(500);
                if (pump.IsCompleted) { Log("event pump ended"); break; }
                FlushIfIdle();
                // Process enumeration is the expensive part of this loop; do it every 5 s, not every 500 ms.
                if (++loop % 10 != 0) continue;
                if (File.Exists(StopPath)) { Log("stop requested"); break; }
                if (parentPid > 0 && !ProcessAlive(parentPid)) { Log("parent gone"); break; }
                if (parentPid == 0)
                {
                    if (TrayRunning()) noTraySince = null;
                    else if (noTraySince is null) noTraySince = DateTime.Now;
                    else if (DateTime.Now - noTraySince > TimeSpan.FromSeconds(10)) { Log("no WattBar instance left"); break; }
                }
            }

            session.Dispose();
            WriteSnapshot(new E3Snapshot { Status = "stopped", Updated = DateTime.Now, Apps = _lastApps });
            if (File.Exists(StopPath)) File.Delete(StopPath);
            return 0;
        }
        catch (Exception ex)
        {
            Log("fatal: " + ex);
            try { WriteSnapshot(new E3Snapshot { Status = "error", Message = ex.Message, Updated = DateTime.Now }); } catch { }
            return 1;
        }
    }

    private static void OnEvent(TraceEvent e)
    {
        _events++;
        string name = e.EventName ?? "";
        string opcode = e.OpcodeName ?? "";
        // Event 37 (QueryStats/EnergyEstimate) is the per-minute batch. Event 38 (MergeStats/EnergyEstimate)
        // carries the same shape on its own cadence and would overwrite or double the batch.
        bool isEnergy = (int)e.ID == 37 && opcode.Equals("EnergyEstimate", StringComparison.OrdinalIgnoreCase);

        if (LoggedShapes.Count < 12)
        {
            string shape = $"{name} / {opcode} / id {(int)e.ID} / {e.PayloadNames.Length} fields";
            if (LoggedShapes.Add(shape))
                Log($"event shape: {shape} [{string.Join(", ", e.PayloadNames)}]");
        }
        if (!isEnergy) return;
        if (e.PayloadByName("AppName") is not string appId || e.PayloadByName("DisplayEnergy") is null) return;

        double Num(string field) { try { return e.PayloadByName(field) is object v ? Convert.ToDouble(v) : 0; } catch { return 0; } }

        string appName = Normalize(appId);
        lock (Gate)
        {
            _lastEnergyEvent = DateTime.Now;
            _energyEvents++;
            _intervalMs = (int)Num("TimeInMSec");
            _flags ??= e.PayloadByName("RecordFlags")?.ToString();

            if (appName.StartsWith("EMI_RAPL", StringComparison.OrdinalIgnoreCase))
            {
                if (appName.EndsWith("_PKG", StringComparison.OrdinalIgnoreCase)) _packageMeter += Num("EmiEnergy");
                return;
            }
            if (appName.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return;

            if (!Batch.TryGetValue(appName, out var app))
                Batch[appName] = app = new E3App { Name = appName };
            app.Cpu += Num("CpuEnergy");
            app.Gpu += Num("GpuEnergy");
            app.Display += Num("DisplayEnergy");
            app.Disk += Num("DiskEnergy");
            app.Network += Num("NetworkEnergy");
            app.Npu += Num("NpuEnergy");
            app.Other += Num("OtherEnergy") + Num("MbbEnergy") + Num("LossEnergy");

            int si = e.PayloadIndex("InteractivityState");
            string state = si >= 0 ? (e.PayloadString(si) ?? "") : "";
            if (Rank(state) > Rank(app.State)) app.State = Canonical(state);
        }
    }

    private static int Rank(string state) => Canonical(state) switch { "Focus" => 3, "Visible" => 2, "Minimized" => 1, _ => 0 };

    private static string Canonical(string state)
    {
        if (state.Contains("Focus", StringComparison.OrdinalIgnoreCase)) return "Focus";
        if (state.Contains("Visible", StringComparison.OrdinalIgnoreCase)) return "Visible";
        if (state.Contains("Minimized", StringComparison.OrdinalIgnoreCase)) return "Minimized";
        return "Background";
    }

    /// <summary>A batch arrives as a burst; two quiet seconds after the last event it is complete.</summary>
    private static void FlushIfIdle()
    {
        E3Snapshot? snap = null;
        lock (Gate)
        {
            if (Batch.Count == 0 || DateTime.Now - _lastEnergyEvent < TimeSpan.FromSeconds(2)) return;
            snap = new E3Snapshot
            {
                Status = "collecting",
                Updated = DateTime.Now,
                IntervalMs = _intervalMs,
                Flags = _flags,
                PackageMeter = _packageMeter,
                Apps = Batch.Values.Where(a => a.Total > 0).OrderByDescending(a => a.Cpu).ToList(),
            };
            _lastApps = snap.Apps;
            Batch.Clear();
            _flags = null;
            _packageMeter = 0;
        }
        WriteSnapshot(snap);
        Log($"batch written: {snap.Apps.Count} apps, {_energyEvents} energy events of {_events} total so far");
    }

    /// <summary>Device path, packaged-app id or service tag to something readable.</summary>
    private static string Normalize(string appId)
    {
        string s = appId.Trim().Trim('"');
        int slash = s.LastIndexOf('\\');
        if (slash >= 0) s = s[(slash + 1)..];
        s = PackagedSuffix.Replace(s, "");
        return s.TrimStart('!');
    }

    private static void PrepareDirectory()
    {
        var dir = Directory.CreateDirectory(Dir);
        if (!IsElevated()) return;
        try
        {
            // Let the unelevated tray app drop the "stop" file here.
            var sec = dir.GetAccessControl();
            sec.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            dir.SetAccessControl(sec);
        }
        catch (Exception ex)
        {
            Log("acl: " + ex.Message);
        }
    }

    private static void WriteSnapshot(E3Snapshot snap)
    {
        string tmp = SnapshotPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(snap, JsonOptions));
        File.Move(tmp, SnapshotPath, overwrite: true);
    }

    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static bool TrayRunning()
    {
        try { return Process.GetProcessesByName("WattBar").Any(p => p.Id != Environment.ProcessId); }
        catch { return true; }
    }

    private static bool ProcessAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void Log(string line)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"); } catch { }
    }
}
