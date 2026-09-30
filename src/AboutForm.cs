using System.Diagnostics;
using System.Reflection;

using static WattBar.L10n;

namespace WattBar;

/// <summary>Small about box.</summary>
public sealed class AboutForm : Form
{
    public const string RepoUrl = "https://github.com/pmudry/wattbar";

    public AboutForm()
    {
        Text = T("About WattBar");
        if (TrayApp.AppIcon is Icon ico) Icon = ico;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;

        _ = Handle;
        float f = DeviceDpi / 96f;
        ClientSize = new Size((int)(420 * f), (int)(230 * f));
        int pad = (int)(20 * f);

        string version = Assembly.GetExecutingAssembly().GetName().Version is Version v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";
        var title = new Label { Text = "WattBar", Font = new Font("Segoe UI", 18f, FontStyle.Bold), AutoSize = true, Location = new Point(pad, pad) };
        var ver = new Label { Text = T("version {0}", version), AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(pad, (int)(pad + 40 * f)) };
        var desc = new Label
        {
            Text = T("Battery discharge power in the Windows 11 tray, with a sparkline icon, a chart flyout, CPU package power and a per-process view fed by Windows’ own energy estimates."),
            AutoSize = false,
            Location = new Point(pad, (int)(pad + 66 * f)),
        };
        // Wrapped height must be known before the controls below are placed.
        int descWidth = ClientSize.Width - 2 * pad;
        desc.Size = new Size(descWidth, desc.GetPreferredSize(new Size(descWidth, 0)).Height + (int)(4 * f));
        var accent = Theme.AppsAreDark() ? Theme.MagentaDark : Theme.MagentaLight;
        var link = new LinkLabel { Text = RepoUrl, AutoSize = true, Location = new Point(pad, desc.Bottom + (int)(10 * f)), LinkColor = accent, ActiveLinkColor = accent, VisitedLinkColor = accent };
        link.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true }); } catch { }
        };
        var license = new Label { Text = T("MIT License · Made with ♥ by mui, 2026"), AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(pad, link.Bottom + (int)(8 * f)) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size((int)(90 * f), (int)(30 * f)) };
        ClientSize = new Size(ClientSize.Width, license.Bottom + (int)(16 * f) + ok.Height + pad);
        ok.Location = new Point(ClientSize.Width - pad - ok.Width, ClientSize.Height - pad - ok.Height);
        AcceptButton = ok;
        CancelButton = ok;

        Controls.AddRange([title, ver, desc, link, license, ok]);
    }
}
