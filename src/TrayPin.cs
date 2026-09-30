using Microsoft.Win32;

namespace WattBar;

/// <summary>
/// Windows 11 hides new tray icons in the overflow menu. Explorer keeps one entry per icon under
/// NotifyIconSettings; setting IsPromoted there is what the Settings toggle does.
/// </summary>
public static class TrayPin
{
    private const string Root = @"Control Panel\NotifyIconSettings";

    /// <summary>Returns true once our entry was found and promoted (or already was).</summary>
    public static bool Promote()
    {
        if (Environment.ProcessPath is not string exe) return false;
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Root, writable: true);
            if (root is null) return false;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name, writable: true);
                if (key?.GetValue("ExecutablePath") is not string path) continue;
                if (!string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)) continue;
                if (key.GetValue("IsPromoted") is not int v || v != 1)
                    key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                return true;
            }
        }
        catch
        {
            // best effort only
        }
        return false;
    }
}
