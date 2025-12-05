using System.Drawing;

namespace screenzap.Components.Shared
{
    public readonly struct SelectionMetrics
    {
        public static readonly SelectionMetrics Empty = new SelectionMetrics(Rectangle.Empty, Rectangle.Empty);

        public SelectionMetrics(Rectangle selection, Rectangle imageBounds)
        {
            Selection = selection;
            ImageBounds = imageBounds;
            ClampedSelection = Intersect(selection, imageBounds);
        }

        public Rectangle Selection { get; }
        public Rectangle ImageBounds { get; }
        public Rectangle ClampedSelection { get; }
        public bool IsWithinBounds => Selection == ClampedSelection;
        public bool HasSelection => Selection.Width > 0 && Selection.Height > 0;
        public bool IsSquare => HasSelection && Selection.Width == Selection.Height;

        public static SelectionMetrics From(Rectangle selection, Rectangle imageBounds)
        {
            return new SelectionMetrics(selection, imageBounds);
        }

        private static Rectangle Intersect(Rectangle a, Rectangle b)
        {
            if (a.IsEmpty || b.IsEmpty)
            {
                return Rectangle.Empty;
            }

            return Rectangle.Intersect(a, b);
        }
    }
}
