using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace WattBar;

/// <summary>Draws the tray icon: a sparkline of recent power behind a large wattage number.</summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Render(int size, double watts, PowerState state, double[] spark, bool lightTaskbar)
    {
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            DrawSparkline(g, size, spark, Theme.Accent(state, !lightTaskbar));
            DrawNumber(g, size, Label(watts, state), lightTaskbar);
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

    /// <summary>Short text that fits a 16 px icon: "16", "7.5", "AC", "99+".</summary>
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

    private static void DrawNumber(Graphics g, int size, string text, bool lightTaskbar)
    {
        using var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        using var fmt = (StringFormat)StringFormat.GenericTypographic.Clone();

        // Build the glyph outline at an arbitrary size, then scale it to fill the icon box.
        // (AddString with a layout rectangle silently drops text that does not fit.)
        path.AddString(text, family, (int)FontStyle.Bold, 64f, new PointF(0, 0), fmt);
        var b = path.GetBounds();
        if (b.Width <= 0 || b.Height <= 0) return;

        float scale = Math.Min((size - 1f) / b.Width, size * 0.9f / b.Height);
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
