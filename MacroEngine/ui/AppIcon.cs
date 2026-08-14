using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MacroEngine.UI;

/// <summary>
/// Loads the application icon from icon.png next to the executable,
/// or renders a simple fallback (dark circle + teal glyph) if missing.
/// </summary>
internal static class AppIcon
{
    private static WindowIcon? _cached;

    public static WindowIcon Get()
    {
        if (_cached != null) return _cached;

        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
        _cached = File.Exists(path)
            ? new WindowIcon(path)
            : new WindowIcon(RenderFallback());
        return _cached;
    }

    private static Bitmap RenderFallback()
    {
        var rtb = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            var bg = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            var ring = new Pen(new SolidColorBrush(Color.FromRgb(0, 180, 140)), 2);
            ctx.DrawEllipse(bg, ring, new Point(16, 16), 14, 14);

            var accent = new SolidColorBrush(Color.FromRgb(0, 210, 160));
            var segments = new PathSegments
            {
                new LineSegment { Point = new Point(23, 16) },
                new LineSegment { Point = new Point(12, 23) }
            };
            var figure = new PathFigure
            {
                StartPoint = new Point(12, 9),
                IsClosed = true,
                Segments = segments
            };
            var arrow = new PathGeometry { Figures = new PathFigures { figure } };
            ctx.DrawGeometry(accent, null, arrow);
        }
        return rtb;
    }
}
