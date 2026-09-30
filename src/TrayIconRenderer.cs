using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace WattBar;

/// <summary>Draws the tray icon: a sparkline of recent power behind a large wattage number.</summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Render(int size, double watts, PowerState state, double[] spark, bool lightTaskbar, string? label = null)
    {
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            DrawSparkline(g, size, spark, Theme.Accent(state, !lightTaskbar));
            DrawNumber(g, size, label ?? Label(watts, state), lightTaskbar);
        }

        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }

    /// <summary>Short text for the icon: "16", "7.5", "AC", "99+".</summary>
    public static string Label(double watts, PowerState state)
    {
        double w = Math.Abs(watts);
        if (state == PowerState.Idle && w < 0.5) return "AC";
        if (w >= 99.5) return "99+";
        if (w >= 9.95) return ((int)Math.Round(w)).ToString();
        return w.ToString("0.0");
    }

    private static void DrawSparkline(Graphics g, int size, double[] spark, Color accent)
    {
        if (spark.Length < 2) return;
        double max = Math.Max(spark.Max(), 1e-3);
        float bw = (float)size / spark.Length;
        using var brush = new SolidBrush(Color.FromArgb(170, accent));
        for (int i = 0; i < spark.Length; i++)
        {
            if (spark[i] <= 0) continue;
            float h = (float)(spark[i] / max * size * 0.92);
            g.FillRectangle(brush, i * bw, size - h, Math.Max(bw - 0.5f, 1f), h);
        }
    }

    private static readonly string[] DigitFamilies = ["Segoe UI"];

    private static FontFamily DigitFamily()
    {
        foreach (var name in DigitFamilies)
        {
            try { return new FontFamily(name); } catch (ArgumentException) { }
        }
        return FontFamily.GenericSansSerif;
    }

    /// <summary>Debug aid: renders sample labels at tray sizes, magnified, into one PNG.</summary>
    public static void DumpSheet(string path)
    {
        string[] labels = ["5.7", "11", "9.4", "AC", "99+"];
        int[] sizes = [16, 24, 32];
        const int zoom = 6, cell = 40 * zoom;
        using var sheet = new Bitmap(labels.Length * cell, sizes.Length * cell);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(32, 32, 32));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var spark = Enumerable.Range(0, 8).Select(i => 1.0 + i % 3).ToArray();
        for (int r = 0; r < sizes.Length; r++)
            for (int c = 0; c < labels.Length; c++)
            {
                using var icon = Render(sizes[r], 0, PowerState.Discharging, spark, false, labels[c]);
                using var bmp = icon.ToBitmap();
                int px = sizes[r] * zoom;
                g.DrawImage(bmp, c * cell + (cell - px) / 2, r * cell + (cell - px) / 2, px, px);
            }
        sheet.Save(path);
    }

    private static void DrawNumber(Graphics g, int size, string text, bool lightTaskbar)
    {
        using var path = new GraphicsPath();
        using var family = DigitFamily();
        using var fmt = (StringFormat)StringFormat.GenericTypographic.Clone();

        // Build the glyph outline at an arbitrary size, then scale it into the icon box.
        // (AddString with a layout rectangle silently drops text that does not fit.)
        var style = family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        path.AddString(text, family, (int)style, 64f, new PointF(0, 0), fmt);
        var b = path.GetBounds();
        if (b.Width <= 0 || b.Height <= 0) return;

        // Same digit height for every label: the scale that lets a three-glyph value such as
        // "9.9" span the box width. Longer labels shrink further, shorter ones do not grow.
        using var refPath = new GraphicsPath();
        refPath.AddString("9.9", family, (int)style, 64f, new PointF(0, 0), fmt);
        var rb = refPath.GetBounds();
        float scale = Math.Min((size - 1f) / rb.Width, size * 0.9f / rb.Height);
        scale = Math.Min(scale, (size - 1f) / b.Width);
        using var m = new Matrix();
        m.Translate(size / 2f, size / 2f);
        m.Scale(scale, scale);
        m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
        path.Transform(m);

        // Outline in the opposite tone so the digits stay legible over any taskbar colour.
        Color fill = lightTaskbar ? Color.Black : Color.White;
        Color outline = lightTaskbar ? Color.FromArgb(200, Color.White) : Color.FromArgb(200, Color.Black);
        using var pen = new Pen(outline, Math.Max(1.5f, size / 12f)) { LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(fill);
        g.DrawPath(pen, path);
        g.FillPath(brush, path);
    }
}
