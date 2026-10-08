namespace screenzap.Components;

// Physical pixels throughout; the native surface has exactly the frozen monitor's bounds.
internal sealed class CaptureSelection
{
    private readonly Size size;
    private Point start, end;
    internal bool IsDragging { get; private set; }
    internal Point End => end;
    internal Rectangle Area
    {
        get
        {
            var rect = Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
            return Rectangle.Intersect(new Rectangle(Point.Empty, size), rect);
        }
    }
    internal CaptureSelection(Size size) => this.size = size;
    internal void Begin(Point point) { start = end = point; IsDragging = true; }
    internal void Move(Point point, bool pan, bool center, bool square, bool snap)
    {
        if (!IsDragging) return;
        var difference = new Size(point.X - end.X, point.Y - end.Y);
        end = point;
        if (pan) start += difference;
        else
        {
            if (center) start -= difference;
            if (square)
            {
                int length = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
                end = new Point(start.X + length * (end.X > start.X ? 1 : -1), start.Y + length * (end.Y > start.Y ? 1 : -1));
            }
        }
        if (snap) end = new Point(start.X + (int)Math.Round((end.X - start.X) / 16d) * 16, start.Y + (int)Math.Round((end.Y - start.Y) / 16d) * 16);
    }
    internal Rectangle Finish() { IsDragging = false; return Area; }
    internal void Cancel() { IsDragging = false; start = end = Point.Empty; }
}
