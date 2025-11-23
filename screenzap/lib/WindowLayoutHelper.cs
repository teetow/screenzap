using System;
using System.Drawing;
using System.Windows.Forms;

namespace screenzap.lib
{
    internal static class WindowLayoutHelper
    {
        private static readonly Size DefaultMinimumSize = new Size(800, 600);

        public static Rectangle GetDefaultBounds()
        {
            var screen = Screen.FromPoint(Cursor.Position);
            return CenterWithin(screen.WorkingArea, DefaultMinimumSize);
        }

        public static Rectangle CenterWithin(Rectangle container, Size desired)
        {
            var width = Math.Max(desired.Width, DefaultMinimumSize.Width);
            var height = Math.Max(desired.Height, DefaultMinimumSize.Height);

            var left = container.Left + Math.Max(0, (container.Width - width) / 2);
            var top = container.Top + Math.Max(0, (container.Height - height) / 2);
            return new Rectangle(left, top, Math.Min(width, container.Width), Math.Min(height, container.Height));
        }

        public static Rectangle ClampToWorkingArea(Rectangle proposedBounds)
        {
            var screen = Screen.FromRectangle(proposedBounds);
            var workArea = screen.WorkingArea;
            var width = Math.Min(proposedBounds.Width, workArea.Width);
            var height = Math.Min(proposedBounds.Height, workArea.Height);

            var left = Math.Max(workArea.Left, Math.Min(workArea.Right - width, proposedBounds.Left));
            var top = Math.Max(workArea.Top, Math.Min(workArea.Bottom - height, proposedBounds.Top));
            return new Rectangle(left, top, width, height);
        }

        public static void ApplyInitialGeometry(Form target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (!target.StartPosition.Equals(FormStartPosition.Manual) || target.Bounds.Width == 0 || target.Bounds.Height == 0)
            {
                var bounds = GetDefaultBounds();
                target.StartPosition = FormStartPosition.Manual;
                target.Bounds = bounds;
            }
        }
    }
}
