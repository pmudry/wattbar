namespace WattBar;

static class Program
{
    public const string ShowEventName = @"Local\WattBar.Show";
    public const string WhoEventName = @"Local\WattBar.Who";

    [STAThread]
    static void Main(string[] args)
    {
        // Elevated side process for per-process energy; runs outside the single-instance guard.
        int collect = Array.IndexOf(args, "--collect-e3");
        if (collect >= 0)
        {
            int pid = collect + 1 < args.Length && int.TryParse(args[collect + 1], out var p) ? p : 0;
            Environment.Exit(E3Collector.Run(pid));
        }

        bool show = args.Contains("--show");
        bool who = args.Contains("--who");

        int dump = Array.IndexOf(args, "--dump-icons");
        if (dump >= 0 && dump + 1 < args.Length)
        {
            TrayIconRenderer.DumpSheet(args[dump + 1]);
            return;
        }

        using var mutex = new Mutex(true, @"Local\WattBar.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            // Already running: a second launch with --show just opens the chart of the running instance.
            try
            {
                if (show) EventWaitHandle.OpenExisting(ShowEventName).Set();
                if (who) EventWaitHandle.OpenExisting(WhoEventName).Set();
            }
            catch { /* instance is shutting down */ }
            return;
        }

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // dark title bars and controls for the "who is using it" window
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
        Application.Run(new TrayApp(showFlyout: show, showOffenders: who));
    }
}
