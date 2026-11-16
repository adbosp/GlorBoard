using System.Windows;
using System.Windows.Media;

namespace GlorBoard;

internal static class WindowGlass
{
    internal const double Radius = 32;

    internal static void Attach(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.Resources["GlassCornerRadius"] = new CornerRadius(Radius - 2);

        // Clip the finished surface, including its fallback fill, rim, and all child visuals.
        if (window.Content is FrameworkElement content) AttachClip(content);
    }

    private static void AttachClip(FrameworkElement surface)
    {
        var mask = new RectangleGeometry { RadiusX = Radius, RadiusY = Radius };
        surface.Clip = mask;
        mask.Rect = new Rect(surface.RenderSize);
        surface.SizeChanged += (_, e) => mask.Rect = new Rect(e.NewSize);
    }
}
