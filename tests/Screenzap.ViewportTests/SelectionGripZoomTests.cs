using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Marquee grip hit-testing across zoom levels. The grips are a fixed screen-space band, so
    /// zooming in must open up the marquee's interior: the whole point of zooming to 32x on a 1px
    /// selection is that the pixel becomes a 32px target you can grab, move, stamp, and clone.
    /// Every test drives the real mouse pipeline through raw viewport client points.
    /// </summary>
    public class SelectionGripZoomTests
    {
        private static screenzap.ImageDocumentEditor PrepareEditor(Rectangle selection, decimal zoom, Color blockColor)
        {
            var editor = EditorFixture.WithCanvas(160, 120, graphics =>
            {
                using var brush = new SolidBrush(blockColor);
                graphics.FillRectangle(brush, selection);
            });

            editor.SetSelectionForDiagnostics(selection);
            editor.TestSetZoom(zoom);
            return editor;
        }

        /// <summary>Center of an image pixel in viewport client coordinates.</summary>
        private static Point PixelCenter(screenzap.ImageDocumentEditor editor, Point imagePixel, int zoom)
        {
            var topLeft = editor.TestImagePixelToClient(imagePixel);
            // Deliberately off the exact half-pixel: ClientToPixel rounds, and .5 lands on a
            // banker's-rounding boundary that would make the test's intent unclear.
            return new Point(topLeft.X + zoom / 2 - 1, topLeft.Y + zoom / 2 - 1);
        }

        /// <summary>
        /// The reported bug: a 1px marquee zoomed to 32x is a 32px square on screen, but the hit
        /// test used to quantize the cursor onto the zoom grid, so every click inside it snapped
        /// onto a corner and the grips took precedence — leaving no way to grab the selection.
        /// </summary>
        [Fact]
        public void OnePixelMarquee_AtHighZoom_AltDragClonesInsteadOfResizing()
        {
            StaTest.Run(() =>
            {
                const int zoom = 32;
                using var editor = PrepareEditor(new Rectangle(10, 10, 1, 1), zoom, Color.Red);

                var grab = PixelCenter(editor, new Point(10, 10), zoom);
                var drop = new Point(grab.X + 3 * zoom, grab.Y);

                editor.TestSetAltHeld(true);
                try
                {
                    editor.TestFireMouseDownAtClientPoint(grab, MouseButtons.Left);
                    editor.TestFireMouseMoveAtClientPoint(drop, MouseButtons.Left);
                    editor.TestFireMouseUpAtClientPoint(drop, MouseButtons.Left);
                }
                finally
                {
                    editor.TestSetAltHeld(false);
                }

                // Moved as a whole — still one pixel, three pixels to the right.
                Assert.Equal(new Rectangle(13, 10, 1, 1), editor.SelectionDiagnostics.Selection);

                using var result = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(Color.Red.ToArgb(), result.GetPixel(13, 10).ToArgb());
            });
        }

        /// <summary>
        /// A 1px marquee at 1:1 has no room for grips at all, so it stays movable rather than
        /// becoming an untouchable knot of overlapping corner handles. (Zoom in to resize it.)
        /// </summary>
        [Fact]
        public void OnePixelMarquee_AtOneToOne_IsStillMovable()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(new Rectangle(10, 10, 1, 1), 1m, Color.Red);

                var grab = editor.TestImagePixelToClient(new Point(10, 10));
                var drop = new Point(grab.X + 5, grab.Y);

                editor.TestFireMouseDownAtClientPoint(grab, MouseButtons.Left);
                editor.TestFireMouseMoveAtClientPoint(drop, MouseButtons.Left);
                editor.TestFireMouseUpAtClientPoint(drop, MouseButtons.Left);

                Assert.Equal(new Rectangle(15, 10, 1, 1), editor.SelectionDiagnostics.Selection);
            });
        }

        /// <summary>
        /// Interior clicks stay interior at high zoom: dragging from well inside a zoomed marquee
        /// translates it and leaves its size alone.
        /// </summary>
        [Fact]
        public void ZoomedMarquee_InteriorDrag_MovesWithoutResizing()
        {
            StaTest.Run(() =>
            {
                const int zoom = 16;
                var selection = new Rectangle(20, 20, 4, 4);
                using var editor = PrepareEditor(selection, zoom, Color.Blue);

                var grab = PixelCenter(editor, new Point(22, 22), zoom);
                var drop = new Point(grab.X + 2 * zoom, grab.Y + 1 * zoom);

                editor.TestFireMouseDownAtClientPoint(grab, MouseButtons.Left);
                editor.TestFireMouseMoveAtClientPoint(drop, MouseButtons.Left);
                editor.TestFireMouseUpAtClientPoint(drop, MouseButtons.Left);

                Assert.Equal(new Rectangle(22, 21, 4, 4), editor.SelectionDiagnostics.Selection);
            });
        }

        /// <summary>
        /// The grips themselves still work: grabbing within the screen-space band of the right
        /// edge resizes rather than moves, at any zoom.
        /// </summary>
        [Fact]
        public void ZoomedMarquee_EdgeDrag_StillResizes()
        {
            StaTest.Run(() =>
            {
                const int zoom = 16;
                var selection = new Rectangle(20, 20, 4, 4);
                using var editor = PrepareEditor(selection, zoom, Color.Blue);

                // 2px inside the right edge on screen — within the grip band, but nowhere near
                // the right edge in image pixels.
                var rightEdge = editor.TestImagePixelToClient(new Point(selection.Right, selection.Top));
                var grab = new Point(rightEdge.X - 2, rightEdge.Y + 2 * zoom);
                var drop = new Point(grab.X + 3 * zoom, grab.Y);

                editor.TestFireMouseDownAtClientPoint(grab, MouseButtons.Left);
                editor.TestFireMouseMoveAtClientPoint(drop, MouseButtons.Left);
                editor.TestFireMouseUpAtClientPoint(drop, MouseButtons.Left);

                var after = editor.SelectionDiagnostics.Selection;
                Assert.Equal(selection.Left, after.Left);
                Assert.Equal(selection.Top, after.Top);
                Assert.Equal(selection.Height, after.Height);
                Assert.True(after.Width > selection.Width, $"expected the right edge to be dragged out, got {after}");
            });
        }

        /// <summary>
        /// With no selection at all, clicking near the image origin must start a rubber band —
        /// not grab the phantom grips of an empty marquee sitting at (0,0).
        /// </summary>
        [Fact]
        public void EmptySelection_ClickNearOrigin_StartsARubberBandInsteadOfResizing()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(Rectangle.Empty, 1m, Color.White);

                var start = editor.TestImagePixelToClient(new Point(2, 2));
                var end = editor.TestImagePixelToClient(new Point(40, 30));

                editor.TestFireMouseDownAtClientPoint(start, MouseButtons.Left);
                editor.TestFireMouseMoveAtClientPoint(end, MouseButtons.Left);
                editor.TestFireMouseUpAtClientPoint(end, MouseButtons.Left);

                Assert.Equal(new Rectangle(2, 2, 38, 28), editor.SelectionDiagnostics.Selection);
            });
        }
    }
}
