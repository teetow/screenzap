using System;
using System.Drawing;
using System.Windows.Forms;

namespace screenzap.lib
{
    internal static class WindowLayoutHelper
    {
        private static readonly Size FallbackMinimumSize = new Size(800, 600);

        /// <summary>
        /// The share of the working area an auto-sized window is allowed to take. Screenshots are
        /// usually about as big as the screen they came from, so sizing one to fit at 1:1 nearly
        /// always asks for more room than exists; without a cap every large capture produced a
        /// window exactly the size of the working area, cornered — a window pretending to be
        /// maximized. The leftover tenth is what makes it read as a window.
        /// </summary>
        public const double MaxWorkingAreaFraction = 0.9;

        public static Rectangle GetDefaultBounds(Size minimumSize)
        {
            var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            return CenterOnAnchor(workArea, minimumSize, minimumSize);
        }

        /// <summary>
        /// The screen an anchor rectangle belongs to, chosen by its centre point.
        /// <see cref="Screen.FromRectangle"/> picks by largest intersection, so a proposed size
        /// wider than the monitor it started on can select a neighbour and teleport the window;
        /// a centre point cannot.
        /// </summary>
        public static Screen ScreenForAnchor(Rectangle anchor)
        {
            return Screen.FromPoint(CenterOf(anchor));
        }

        /// <summary>
        /// Bounds for a window that wants <paramref name="desiredOuter"/>: held to
        /// <see cref="MaxWorkingAreaFraction"/> of the anchor's screen, never below
        /// <paramref name="minimumOuter"/>, centred on the anchor's centre, and nudged fully
        /// inside the working area. All three sizes are outer (window) sizes, not client sizes.
        /// </summary>
        public static Rectangle CenterOnAnchor(Rectangle anchor, Size desiredOuter, Size minimumOuter)
        {
            var workArea = ScreenForAnchor(anchor).WorkingArea;

            var width = FitAxis(
                desiredOuter.Width,
                minimumOuter.Width,
                (int)Math.Round(workArea.Width * MaxWorkingAreaFraction),
                workArea.Width);
            var height = FitAxis(
                desiredOuter.Height,
                minimumOuter.Height,
                (int)Math.Round(workArea.Height * MaxWorkingAreaFraction),
                workArea.Height);

            var center = CenterOf(anchor);
            var centered = new Rectangle(center.X - width / 2, center.Y - height / 2, width, height);
            return ClampToWorkingArea(centered, workArea);
        }

        public static Rectangle ClampToWorkingArea(Rectangle proposedBounds)
        {
            return ClampToWorkingArea(proposedBounds, ScreenForAnchor(proposedBounds).WorkingArea);
        }

        public static Rectangle ClampToWorkingArea(Rectangle proposedBounds, Rectangle workArea)
        {
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
                // Centre for the size the window will actually have. Centring for a smaller size
                // and letting WinForms widen it up to MinimumSize afterwards left the window off
                // centre by half the difference.
                var minimum = target.MinimumSize.IsEmpty ? FallbackMinimumSize : target.MinimumSize;
                target.StartPosition = FormStartPosition.Manual;
                target.Bounds = GetDefaultBounds(minimum);
            }
        }

        private static Point CenterOf(Rectangle rect)
        {
            return new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        }

        /// <summary>
        /// One axis: the cap is a preference, the window's own minimum and the screen are not, so
        /// a minimum larger than the cap wins and the working area beats both.
        /// </summary>
        private static int FitAxis(int desired, int minimum, int cap, int available)
        {
            var ceiling = Math.Min(available, Math.Max(cap, minimum));
            return Math.Max(Math.Min(minimum, ceiling), Math.Min(desired, ceiling));
        }
    }
}
