using System.Diagnostics;
using System.Security.Principal;

namespace WattBar;

/// <summary>How the per-process collector obtains administrator rights.</summary>
public enum E3Mode { Ask, Task, Off }

/// <summary>
/// Scheduled-task route for the collector: a task registered once (with consent) to run the collector with
/// highest privileges can afterwards be started by the same user without a UAC prompt.
/// </summary>
public static class E3Task
{
    public const string Name = "WattBar E3 collector";

    public static E3Mode Mode
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\WattBar");
                return Enum.TryParse<E3Mode>(key?.GetValue("E3Mode") as string, ignoreCase: true, out var m) ? m : E3Mode.Ask;
            }
            catch { return E3Mode.Ask; }
        }
        set
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\WattBar");
            key.SetValue("E3Mode", value.ToString());
        }
    }

    /// <summary>True when the task exists and points at this exe.</summary>
    public static bool IsInstalled()
    {
        var (code, output) = Exec("schtasks.exe", $"/query /tn \"{Name}\" /xml");
        return code == 0 && Environment.ProcessPath is string exe && output.Contains(exe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Starts the task; works unelevated for the task's own user.</summary>
    public static bool Run() => Exec("schtasks.exe", $"/run /tn \"{Name}\"").code == 0;

    /// <summary>Relaunches this exe elevated to register the task. Returns false when consent was refused or it failed.</summary>
    public static bool InstallElevated() => RunElevated($"--install-task \"{WindowsIdentity.GetCurrent().Name}\"");

    public static bool UninstallElevated() => RunElevated("--uninstall-task");

    /// <summary>Elevated side: registers the task for <paramref name="user"/>.</summary>
    public static int Install(string user)
    {
        if (Environment.ProcessPath is not string exe) return 1;
        string script =
            $"$a = New-ScheduledTaskAction -Execute '{exe.Replace("'", "''")}' -Argument '--collect-e3'; " +
            $"$p = New-ScheduledTaskPrincipal -UserId '{user.Replace("'", "''")}' -LogonType Interactive -RunLevel Highest; " +
            "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -Hidden; " +
            $"Register-ScheduledTask -TaskName '{Name}' -Action $a -Principal $p -Settings $s -Force | Out-Null";
        return Exec("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"").code;
    }

    /// <summary>Elevated side: removes the task.</summary>
    public static int Uninstall() => Exec("schtasks.exe", $"/delete /tn \"{Name}\" /f").code;

    private static bool RunElevated(string args)
    {
        if (Environment.ProcessPath is not string exe) return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas" });
            if (p is null) return false;
            p.WaitForExit(60_000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // consent refused
        }
    }

    private static (int code, string output) Exec(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            if (p is null) return (-1, "");
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30_000);
            return (p.HasExited ? p.ExitCode : -1, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
