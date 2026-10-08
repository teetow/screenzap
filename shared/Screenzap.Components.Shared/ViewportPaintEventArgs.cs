using System;
using System.Drawing;

namespace screenzap.Components.Shared;

public sealed class ViewportPaintEventArgs(Graphics graphics, Rectangle clipRectangle) : EventArgs
{
    public Graphics Graphics { get; } = graphics;
    public Rectangle ClipRectangle { get; } = clipRectangle;
}
