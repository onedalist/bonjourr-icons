using System.Windows;
using System.Windows.Media;

namespace BonjourrIconStudio.Helpers;

public static class SquircleGeometry
{
    public const double DefaultExponent = 5.0;

    public static Geometry Create(double width, double height, double exponent = DefaultExponent)
    {
        if (width <= 0 || height <= 0) return Geometry.Empty;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        var centerX = width / 2d;
        var centerY = height / 2d;
        const int segments = 256;

        using (var context = geometry.Open())
        {
            for (var i = 0; i <= segments; i++)
            {
                var angle = 2d * Math.PI * i / segments;
                var cos = Math.Cos(angle);
                var sin = Math.Sin(angle);
                var x = centerX + centerX * Math.Sign(cos) * Math.Pow(Math.Abs(cos), 2d / exponent);
                var y = centerY + centerY * Math.Sign(sin) * Math.Pow(Math.Abs(sin), 2d / exponent);
                var point = new Point(x, y);

                if (i == 0) context.BeginFigure(point, true, true);
                else context.LineTo(point, true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    public static bool ContainsNormalized(double normalizedX, double normalizedY, double exponent = DefaultExponent) =>
        Math.Pow(Math.Abs(normalizedX), exponent) + Math.Pow(Math.Abs(normalizedY), exponent) <= 1d;
}
