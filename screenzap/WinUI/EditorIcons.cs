using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Point = Windows.Foundation.Point;

namespace screenzap.WinUI;

internal static class EditorIcons
{
    internal static IconElement Create(string glyph)
    {
        if (glyph is "perspective" or "censor")
        {
            var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
            if (glyph == "perspective")
            {
                geometry.Children.Add(Polygon(new Point(3, 2), new Point(16, 4), new Point(14, 16), new Point(1, 13)));
                // Opposite winding leaves a hollow quadrilateral.
                geometry.Children.Add(Polygon(new Point(4, 4), new Point(3, 12), new Point(13, 14), new Point(14, 5)));
                foreach (var point in new[] { new Point(3, 2), new Point(16, 4), new Point(14, 16), new Point(1, 13) })
                    geometry.Children.Add(new EllipseGeometry { Center = point, RadiusX = 1.4, RadiusY = 1.4 });
            }
            else
            {
                geometry.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(1, 2, 16, 2) });
                geometry.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(1, 6, 10, 6) });
                geometry.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(13, 6, 4, 2) });
                geometry.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(1, 14, 13, 2) });
            }
            return new PathIcon { Data = geometry, Width = 18, Height = 18 };
        }
        return new FontIcon { Glyph = glyph, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 18, Width = 18, Height = 18 };
    }
    private static PathGeometry Polygon(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        foreach (var point in points.Skip(1)) figure.Segments.Add(new LineSegment { Point = point });
        var path = new PathGeometry { FillRule = FillRule.Nonzero }; path.Figures.Add(figure); return path;
    }
}
