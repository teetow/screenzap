using System;
using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Where wheel zoom leaves the picture. Zoom anchors on the cursor so zooming in on a detail
    /// works, but the anchoring alone used to let a zoom-out sequence walk the picture off the
    /// side of the viewport — one step at a time, each one contracting it toward whichever edge
    /// the pointer was near, until only the overscroll margin was still on screen.
    /// </summary>
    public class ZoomAnchorTests
    {
        private static ImageViewportControl NewViewport(Size client, Size image, decimal zoom = 1m)
        {
            var control = new ImageViewportControl { ClientSize = client };
            control.Image = new Bitmap(image.Width, image.Height);
            control.ZoomLevel = zoom;
            control.CenterImage();
            return control;
        }

        /// <summary>The reported bug: wheel out with the pointer parked in a corner.</summary>
        [Fact]
        public void ZoomOut_RepeatedlyFromACorner_NeverStrandsTheImage()
        {
            using var control = NewViewport(new Size(800, 600), new Size(4000, 3000));
            var corner = new Point(795, 5);

            foreach (var zoom in new[] { 0.75m, 0.5m, 0.4m, 0.3m, 0.2m, 0.1m, 0.05m })
            {
                control.ZoomAround(zoom, corner);

                var rect = control.Metrics.ImageClientRectangle;
                var overlapX = Math.Min(rect.Right, 800) - Math.Max(rect.Left, 0);
                var overlapY = Math.Min(rect.Bottom, 600) - Math.Max(rect.Top, 0);

                // At every step the picture is still fully on screen: either it covers the
                // viewport, or all of it is inside.
                Assert.True(
                    overlapX >= Math.Min(rect.Width, 800) - 1,
                    $"at {zoom:P0} only {overlapX:F0}px of {rect.Width:F0}px was on screen horizontally");
                Assert.True(
                    overlapY >= Math.Min(rect.Height, 600) - 1,
                    $"at {zoom:P0} only {overlapY:F0}px of {rect.Height:F0}px was on screen vertically");
            }
        }

        [Fact]
        public void ZoomOut_UntilTheImageFits_CentresIt()
        {
            using var control = NewViewport(new Size(800, 600), new Size(4000, 3000));

            // 10%: a 400x300 picture in an 800x600 viewport, anchored at the bottom-right corner.
            control.ZoomAround(0.1m, new Point(799, 599));

            var rect = control.Metrics.ImageClientRectangle;
            Assert.Equal(200f, rect.Left);
            Assert.Equal(150f, rect.Top);
        }

        /// <summary>
        /// The step before the picture fits is the one that used to open the gap that the next
        /// step then ran away with.
        /// </summary>
        [Fact]
        public void ZoomOut_WhileTheImageStillOverflows_DoesNotUncoverAnEdge()
        {
            using var control = NewViewport(new Size(800, 600), new Size(4000, 3000));

            control.ZoomAround(0.5m, new Point(795, 5));

            var rect = control.Metrics.ImageClientRectangle;
            Assert.True(rect.Width > 800 && rect.Height > 600, "precondition: still larger than the viewport");
            Assert.True(rect.Left <= 0, $"left edge {rect.Left:F0} uncovered the viewport");
            Assert.True(rect.Top <= 0, $"top edge {rect.Top:F0} uncovered the viewport");
            Assert.True(rect.Right >= 800, $"right edge {rect.Right:F0} uncovered the viewport");
            Assert.True(rect.Bottom >= 600, $"bottom edge {rect.Bottom:F0} uncovered the viewport");
        }

        /// <summary>Zooming in on a detail still holds that detail under the pointer.</summary>
        [Fact]
        public void ZoomIn_KeepsTheFocusPixelUnderTheCursor()
        {
            using var control = NewViewport(new Size(800, 600), new Size(4000, 3000));

            var cursor = new Point(600, 200);
            var pixelUnderCursor = control.ClientToPixel(cursor);

            control.ZoomAround(4m, cursor);

            var afterZoom = control.PixelToClient(pixelUnderCursor);
            Assert.InRange(afterZoom.X, cursor.X - 1, cursor.X + 1);
            Assert.InRange(afterZoom.Y, cursor.Y - 1, cursor.Y + 1);
        }

        /// <summary>
        /// Overscroll from a drag is a deliberate gesture — someone pushing the edge they are
        /// working on away from the overlay tool strips — so zooming in leaves it alone.
        /// </summary>
        [Fact]
        public void ZoomIn_KeepsOverscrollADragSetUp()
        {
            // 1000x800 in an 800x600 viewport: larger than the viewport, so a drag can open a gap
            // without the picture being small enough to centre.
            using var control = NewViewport(new Size(800, 600), new Size(500, 400), 2m);

            control.PanBy(new Size(300, 0));
            var before = control.Metrics.ImageClientRectangle;
            Assert.Equal(200f, before.Left);

            control.ZoomAround(3m, new Point(400, 300));

            var after = control.Metrics.ImageClientRectangle;
            Assert.True(after.Width > 800, "precondition: still larger than the viewport");
            Assert.True(
                after.Left > 0,
                $"zooming in snapped the gap shut (left {after.Left:F0}) instead of leaving the drag alone");
        }

        /// <summary>
        /// An axis the picture does not fill is centred whichever way the wheel turned, so zooming
        /// back in with the pointer over empty canvas cannot push a small picture aside either.
        /// </summary>
        [Fact]
        public void ZoomIn_WhileTheImageStillFits_CentresIt()
        {
            using var control = NewViewport(new Size(800, 600), new Size(400, 300), 0.5m);

            // Pointer far off the picture, on the empty canvas to the right.
            control.ZoomAround(0.75m, new Point(790, 300));

            // 400x300 at 75% is 300x225, centred in 800x600.
            var rect = control.Metrics.ImageClientRectangle;
            Assert.Equal(300f, rect.Width);
            Assert.Equal(225f, rect.Height);
            Assert.Equal(250f, rect.Left);
            Assert.Equal(188f, rect.Top);
        }

        /// <summary>Dragging keeps its full overscroll freedom; only zoom tidies up.</summary>
        [Fact]
        public void PanBy_StillOverscrollsASmallImage()
        {
            using var control = NewViewport(new Size(800, 600), new Size(200, 150));

            control.PanBy(new Size(5000, 5000));

            var rect = control.Metrics.ImageClientRectangle;
            Assert.Equal(800f - ImageViewportControl.OverscrollVisibleMargin, rect.Left);
            Assert.Equal(600f - ImageViewportControl.OverscrollVisibleMargin, rect.Top);
        }
    }
}
