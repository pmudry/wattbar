using Microsoft.Win32;

namespace WattBar;

/// <summary>Accent colours and Windows light/dark detection.</summary>
public static class Theme
{
    // Magenta accent (light / dark variants) and a teal for charging.
    public static readonly Color MagentaLight = ColorTranslator.FromHtml("#ec008b");
    public static readonly Color MagentaDark = ColorTranslator.FromHtml("#f04ea3");
    public static readonly Color Teal = Color.FromArgb(152, 199, 191);
    // Blue, used for the CPU package series.
    public static readonly Color Blue = Color.FromArgb(140, 198, 230);
    public static readonly Color BlueLight = Color.FromArgb(60, 140, 190);

    public static Color Package(bool dark) => dark ? Blue : BlueLight;

    public static Color Accent(PowerState state, bool dark) =>
        state == PowerState.Charging ? Teal : (dark ? MagentaDark : MagentaLight);

    /// <summary>True when the taskbar uses the light theme.</summary>
    public static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Theme override chosen by the user; System follows Windows.</summary>
    public enum Mode { System, Dark, Light }

    public static Mode Selected
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\WattBar");
                return Enum.TryParse<Mode>(key?.GetValue("Theme") as string, ignoreCase: true, out var m) ? m : Mode.System;
            }
            catch { return Mode.System; }
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\WattBar");
            key.SetValue("Theme", value.ToString());
        }
    }

    public static SystemColorMode ColorMode => Selected switch
    {
        Mode.Dark => SystemColorMode.Dark,
        Mode.Light => SystemColorMode.Classic,
        _ => SystemColorMode.System,
    };

    /// <summary>True when the app should draw dark: the user's choice, or Windows' setting when following the system.</summary>
    public static bool AppsAreDark()
    {
        switch (Selected)
        {
            case Mode.Dark: return true;
            case Mode.Light: return false;
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v == 0;
        }
        catch
        {
            return true;
        }
    }
}
