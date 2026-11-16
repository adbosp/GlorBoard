using System;
using System.Windows;

namespace GlorBoard;

internal static class LensGeometry
{
    private const double RimDepth = 36;

    internal static Point Refract(Point point, Rect pane, double radius, double bend)
    {
        double localX = point.X - (pane.X + pane.Width / 2);
        double localY = point.Y - (pane.Y + pane.Height / 2);
        double x = Math.Abs(localX) - (pane.Width / 2 - radius);
        double y = Math.Abs(localY) - (pane.Height / 2 - radius);
        double outsideX = Math.Max(x, 0);
        double outsideY = Math.Max(y, 0);
        double cornerDistance = outsideX > 0 && outsideY > 0
            ? Math.Sqrt(outsideX * outsideX + outsideY * outsideY) : Math.Max(outsideX, outsideY);
        double distance = cornerDistance + Math.Min(Math.Max(x, y), 0) - radius;
        if (distance > 0) return point;

        double normalX, normalY;
        if (outsideX > 0 && outsideY > 0)
        {
            normalX = Math.CopySign(outsideX / cornerDistance, localX);
            normalY = Math.CopySign(outsideY / cornerDistance, localY);
        }
        else
        {
            normalX = x >= y ? Math.Sign(localX) : 0;
            normalY = x < y ? Math.Sign(localY) : 0;
        }

        double depth = -distance;
        double edge = Math.Max(0, 1 - depth / RimDepth);
        double displacement = bend * Smooth(Math.Min(1, depth / 9)) * edge * edge;
        double magnification = .018 * Smooth(Math.Min(1, depth / 14));
        return new Point(point.X - localX * magnification + normalX * displacement,
                         point.Y - localY * magnification + normalY * displacement);
    }

    private static double Smooth(double value) => value * value * (3 - 2 * value);
}
