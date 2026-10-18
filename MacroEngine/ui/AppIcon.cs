using System.Drawing;

namespace MacroEngine.UI;

/// <summary>
/// Loads the application tray icon from icon.png.
/// </summary>
internal static class AppIcon
{
    private static Icon? _cached;
    private static Icon? _paused;

    /// <summary>Greyed-out variant shown in the tray while the engine is paused.</summary>
    public static Icon GetPaused()
    {
        if (_paused != null) return _paused;
        using var src = Get().ToBitmap();
        using var bmp = new Bitmap(src.Width, src.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            var gray = new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new[] { 0.3f, 0.3f, 0.3f, 0f, 0f },
                new[] { 0.59f, 0.59f, 0.59f, 0f, 0f },
                new[] { 0.11f, 0.11f, 0.11f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0.55f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f }
            });
            using var attrs = new System.Drawing.Imaging.ImageAttributes();
            attrs.SetColorMatrix(gray);
            g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }
        _paused = Icon.FromHandle(bmp.GetHicon());
        return _paused;
    }

    public static Icon Get()
    {
        if (_cached != null) return _cached;

        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
        if (File.Exists(path))
        {
            using var bmp = new Bitmap(path);
            _cached = Icon.FromHandle(bmp.GetHicon());
        }
        else
        {
            _cached = GenerateFallbackIcon();
        }

        return _cached;
    }

    private static Icon GenerateFallbackIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var bgBrush = new SolidBrush(Color.FromArgb(45, 45, 48));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);
        using var ringPen = new Pen(Color.FromArgb(0, 180, 140), 2f);
        g.DrawEllipse(ringPen, 1, 1, 30, 30);
        using var font = new Font("Segoe UI", 14, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(0, 210, 160));
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("▸", font, textBrush, new RectangleF(0, -1, 32, 32), sf);
        return Icon.FromHandle(bmp.GetHicon());
    }
}
