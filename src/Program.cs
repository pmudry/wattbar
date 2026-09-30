namespace WattBar;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\WattBar.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(showFlyout: args.Contains("--show")));
    }
}
