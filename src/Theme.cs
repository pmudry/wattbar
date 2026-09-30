using Microsoft.Win32;

namespace WattBar;

/// <summary>Brand colours and Windows light/dark detection.</summary>
public static class Theme
{
    // ISC signature magenta (light / dark variants) and the "embedded systems" teal for charging.
    public static readonly Color MagentaLight = ColorTranslator.FromHtml("#ec008b");
    public static readonly Color MagentaDark = ColorTranslator.FromHtml("#f04ea3");
    public static readonly Color Teal = Color.FromArgb(152, 199, 191);
    // ISC "informatique logicielle" blue, used for the CPU package series.
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

    /// <summary>True when apps should use the dark theme.</summary>
    public static bool AppsAreDark()
    {
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
