using Microsoft.Win32;

namespace WattBar;

/// <summary>Per-user settings under HKCU\Software\WattBar.</summary>
public static class Settings
{
    private const string Key = @"Software\WattBar";
    public static readonly int[] PackageWindows = [1, 5, 10, 30];

    private static int? _packageWindow;

    /// <summary>Moving-average window, in seconds, applied to the CPU package readout. 1 means raw.</summary>
    public static int PackageWindowSeconds
    {
        get
        {
            if (_packageWindow is int w) return w;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Key);
                int v = key?.GetValue("PackageWindowSeconds") is int i ? i : 1;
                _packageWindow = PackageWindows.Contains(v) ? v : 1;
            }
            catch { _packageWindow = 1; }
            return _packageWindow.Value;
        }
        set
        {
            _packageWindow = value;
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue("PackageWindowSeconds", value, RegistryValueKind.DWord);
        }
    }

    public static TimeSpan PackageWindow => TimeSpan.FromSeconds(PackageWindowSeconds);
}
