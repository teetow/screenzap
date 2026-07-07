using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Stamp (ctrl) / clone (alt) marquee gestures: dragging may push the marquee past the
    /// canvas edge (the stamped content clips to the image), and Ctrl/Alt+Arrow performs the
    /// same gestures from the keyboard — one image pixel per press, one undo step per gesture,
    /// closed when the modifier is released. Each test drives the real input pipeline
    /// (TestFireMouse*/TestFireProcessCmdKey/TestFireKeyUp).
    /// </summary>
    public class SelectionStampCloneGestureTests
    {
        /// <summary>White canvas with a red block; the marquee starts on the block.</summary>
        private static screenzap.ImageEditor PrepareEditor(Size canvasSize, Rectangle redBlock)
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(canvasSize.Width, canvasSize.Height);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
                using var brush = new SolidBrush(Color.Red);
                g.FillRectangle(brush, redBlock);
            }
            editor.LoadImage(canvas);
            canvas.Dispose();
            editor.SetSelectionForDiagnostics(redBlock);
            return editor;
        }

        private static int Argb(Color color) => color.ToArgb();

        private static int PixelAt(screenzap.ImageEditor editor, int x, int y)
        {
            using var bitmap = editor.CloneBaseBitmapForTests()!;
            return bitmap.GetPixel(x, y).ToArgb();
        }

        [Fact]
        public void AltDrag_MovesMarqueePastImageEdge_AndClipsClone()
        {
            StaTest.Run(() =>
            {
                var block = new Rectangle(100, 40, 40, 30);
                using var editor = PrepareEditor(new Size(160, 120), block);

                editor.TestSetAltHeld(true);
                try
                {
                    editor.TestFireMouseDownAtImagePixel(new Point(110, 50), MouseButtons.Left);
                    editor.TestFireMouseMoveAtImagePixel(new Point(150, 50), MouseButtons.Left);
                    editor.TestFireMouseUpAtImagePixel(new Point(150, 50), MouseButtons.Left);
                }
                finally
                {
                    editor.TestSetAltHeld(false);
                }

                // The marquee travelled the full 40px even though its right edge left the canvas.
                Assert.Equal(new Rectangle(140, 40, 40, 30), editor.SelectionDiagnostics.Selection);

                // The visible strip of the clone landed; the source block is untouched.
                Assert.Equal(Argb(Color.Red), PixelAt(editor, 155, 55));
                Assert.Equal(Argb(Color.Red), PixelAt(editor, 105, 55));
                Assert.Contains("canUndo=True", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void CtrlDrag_StampsPastImageEdge()
        {
            StaTest.Run(() =>
            {
                var block = new Rectangle(100, 40, 40, 30);
                using var editor = PrepareEditor(new Size(160, 120), block);

                editor.TestSetCtrlHeld(true);
                try
                {
                    editor.TestFireMouseDownAtImagePixel(new Point(110, 50), MouseButtons.Left);
                    editor.TestFireMouseMoveAtImagePixel(new Point(150, 50), MouseButtons.Left);
                    editor.TestFireMouseUpAtImagePixel(new Point(150, 50), MouseButtons.Left);
                }
                finally
                {
                    editor.TestSetCtrlHeld(false);
                }

                Assert.Equal(new Rectangle(140, 40, 40, 30), editor.SelectionDiagnostics.Selection);

                // The stamp trail reaches the canvas edge, clipped there.
                Assert.Equal(Argb(Color.Red), PixelAt(editor, 159, 55));
                Assert.Contains("canUndo=True", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void CtrlArrow_StampsPerPress_AccumulatingOneUndoStep()
        {
            StaTest.Run(() =>
            {
                var block = new Rectangle(10, 10, 20, 20);
                using var editor = PrepareEditor(new Size(160, 120), block);

                for (int i = 0; i < 5; i++)
                {
                    Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Right));
                }

                // Marquee moved 5px right and the stamp trail already painted (before key-up).
                Assert.Equal(new Rectangle(15, 10, 20, 20), editor.SelectionDiagnostics.Selection);
                Assert.Equal(Argb(Color.Red), PixelAt(editor, 34, 15));

                editor.TestFireKeyUp(Keys.ControlKey);
                Assert.Contains("canUndo=True", editor.TestDescribeUndoStack());

                // All 5 presses undo as a single step.
                editor.TestFireKeyDown(Keys.Control | Keys.Z);
                Assert.Equal(Argb(Color.White), PixelAt(editor, 34, 15));
                Assert.Contains("canUndo=False", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void AltArrow_ClonesAtFinalPositionOnModifierRelease()
        {
            StaTest.Run(() =>
            {
                var block = new Rectangle(10, 10, 20, 20);
                using var editor = PrepareEditor(new Size(160, 120), block);

                for (int i = 0; i < 5; i++)
                {
                    Assert.True(editor.TestFireProcessCmdKey(Keys.Alt | Keys.Right));
                }

                // Clone floats until the modifier is released: base image still untouched.
                Assert.Equal(new Rectangle(15, 10, 20, 20), editor.SelectionDiagnostics.Selection);
                Assert.Equal(Argb(Color.White), PixelAt(editor, 34, 15));

                editor.TestFireKeyUp(Keys.Menu);
                Assert.Equal(Argb(Color.Red), PixelAt(editor, 34, 15));
                Assert.Contains("canUndo=True", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void AltArrow_MovesMarqueePastImageEdge()
        {
            StaTest.Run(() =>
            {
                var block = new Rectangle(130, 40, 20, 20);
                using var editor = PrepareEditor(new Size(160, 120), block);

                for (int i = 0; i < 20; i++)
                {
                    Assert.True(editor.TestFireProcessCmdKey(Keys.Alt | Keys.Right));
                }

                // Right edge is 10px past the canvas — no clamping.
                Assert.Equal(new Rectangle(150, 40, 20, 20), editor.SelectionDiagnostics.Selection);

                editor.TestFireKeyUp(Keys.Menu);

                // The on-canvas strip of the clone landed.
                Assert.Equal(Argb(Color.Red), PixelAt(editor, 155, 45));
                Assert.Contains("canUndo=True", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void PlainArrow_MovesMarqueeWithoutTouchingPixels()
        {
            StaTest.Run(() =>
            {
                var block = new Rectangle(10, 10, 20, 20);
                using var editor = PrepareEditor(new Size(160, 120), block);

                for (int i = 0; i < 5; i++)
                {
                    Assert.True(editor.TestFireProcessCmdKey(Keys.Right));
                }
                Assert.True(editor.TestFireProcessCmdKey(Keys.Shift | Keys.Down));

                // 5px right, 10px (shift-accelerated) down; the image itself is untouched.
                Assert.Equal(new Rectangle(15, 20, 20, 20), editor.SelectionDiagnostics.Selection);
                Assert.Equal(Argb(Color.Red), PixelAt(editor, 15, 15));
                Assert.Equal(Argb(Color.White), PixelAt(editor, 34, 25));
                Assert.Contains("canUndo=False", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void PlainArrow_MovesMarqueePastImageEdge()
        {
            StaTest.Run(() =>
            {
                var block = new Rectangle(130, 40, 20, 20);
                using var editor = PrepareEditor(new Size(160, 120), block);

                for (int i = 0; i < 3; i++)
                {
                    Assert.True(editor.TestFireProcessCmdKey(Keys.Shift | Keys.Right));
                }

                // The marquee moves freely past the canvas edge; no pixels changed.
                Assert.Equal(new Rectangle(160, 40, 20, 20), editor.SelectionDiagnostics.Selection);
                Assert.Contains("canUndo=False", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void PlainDrag_MovesMarqueePastImageEdge_WithoutTouchingPixels()
        {
            StaTest.Run(() =>
            {
                var block = new Rectangle(100, 40, 40, 30);
                using var editor = PrepareEditor(new Size(160, 120), block);

                editor.TestFireMouseDownAtImagePixel(new Point(110, 50), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(150, 50), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(150, 50), MouseButtons.Left);

                Assert.Equal(new Rectangle(140, 40, 40, 30), editor.SelectionDiagnostics.Selection);
                Assert.Equal(Argb(Color.White), PixelAt(editor, 155, 55));
                Assert.Contains("canUndo=False", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void CtrlArrow_WithoutMarquee_IsNotIntercepted()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(new Size(160, 120), new Rectangle(10, 10, 20, 20));
                editor.SetSelectionForDiagnostics(Rectangle.Empty);

                Assert.False(editor.TestFireProcessCmdKey(Keys.Control | Keys.Right));
                Assert.Contains("canUndo=False", editor.TestDescribeUndoStack());
            });
        }
    }
}
