using System.Runtime.InteropServices;

namespace WattBar;

/// <summary>
/// DPI of a given monitor. Forms remember the DPI they were created at, and hidden windows are not told
/// when the display changes, so anything that positions itself near the taskbar asks for the live value.
/// </summary>
public static class Dpi
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    public static int ForPoint(Point p)
    {
        try
        {
            IntPtr mon = MonitorFromPoint(new POINT { X = p.X, Y = p.Y }, MonitorDefaultToNearest);
            if (mon != IntPtr.Zero && GetDpiForMonitor(mon, EffectiveDpi, out uint x, out _) == 0 && x > 0) return (int)x;
        }
        catch { }
        return 96;
    }

    /// <summary>DPI of the primary monitor, which is the one carrying the system tray.</summary>
    public static int ForPrimary() => ForPoint((Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds.Location);

    public static float ScaleForPrimary() => ForPrimary() / 96f;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
