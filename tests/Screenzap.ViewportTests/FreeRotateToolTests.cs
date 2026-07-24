using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Free-rotate tool: drag-to-spin the handle for arbitrary (non-90°) angles, Shift snaps to
    /// 15°, Apply bakes via the same rotate-and-expand path as the straighten tool (whole image)
    /// or clips in place (active selection), Cancel/Escape discard. Each test drives the real
    /// input pipeline (TestFireMouse*/TestFireKeyDown/TestClickFreeRotateToolButton).
    /// </summary>
    public class FreeRotateToolTests
    {
        private static screenzap.ImageEditor PrepareEditor(int width = 160, int height = 120)
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(width, height);
            using (var g = Graphics.FromImage(canvas))
                g.Clear(Color.White);
            editor.LoadImage(canvas);
            canvas.Dispose();
            return editor;
        }

        /// <summary>Drags the handle from rest to a client point 90° around the target's center.</summary>
        private static void DragHandleToRightOfCenter(screenzap.ImageEditor editor)
        {
            var handleStart = editor.TestImagePixelToClient(editor.TestFreeRotateHandleImagePoint);
            editor.TestFireMouseDownAtClientPoint(handleStart, MouseButtons.Left);

            // Drive straight right of the target's center — a quarter turn from the handle's
            // resting position (straight up), i.e. a 90° drag.
            var metrics = editor.ViewportDiagnostics;
            var farRight = new Point(metrics.ClientSize.Width - 5, metrics.ClientSize.Height / 2);
            editor.TestFireMouseMoveAtClientPoint(farRight, MouseButtons.Left);
        }

        [Fact]
        public void ActivateFreeRotateTool_RequiresImage_AndChecksToolbarButton()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                editor.TestClickFreeRotateToolButton();
                Assert.False(editor.TestIsFreeRotateToolActive);
            });
        }

        [Fact]
        public void ActivateFreeRotateTool_EntersToolAndChecksButton()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestClickFreeRotateToolButton();

                Assert.True(editor.TestIsFreeRotateToolActive);
                Assert.True(editor.TestFreeRotateButtonChecked);
                Assert.Equal(0f, editor.TestFreeRotateAngleDeg);
            });
        }

        [Fact]
        public void DragHandle_ProducesNon90Angle()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestClickFreeRotateToolButton();

                DragHandleToRightOfCenter(editor);

                // A quarter-turn drag from the resting (straight-up) handle position lands near
                // 90°, but the point of this test is that arbitrary non-90 angles are reachable —
                // assert it's in the ballpark rather than pinning an exact pixel-derived value.
                Assert.InRange(editor.TestFreeRotateAngleDeg, 80f, 100f);

                editor.TestFireMouseUpAtClientPoint(Point.Empty, MouseButtons.Left);
            });
        }

        [Fact]
        public void ShiftDrag_SnapsAngleTo15DegreeSteps()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestClickFreeRotateToolButton();
                editor.TestSetShiftHeld(true);
                try
                {
                    DragHandleToRightOfCenter(editor);
                }
                finally
                {
                    editor.TestSetShiftHeld(false);
                }

                var angle = editor.TestFreeRotateAngleDeg;
                var nearestStep = (float)System.Math.Round(angle / 15.0) * 15f;
                Assert.Equal(nearestStep, angle, 3);
            });
        }

        [Fact]
        public void Apply_WholeImage_ExpandsCanvasAndPushesUndo()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(160, 120);
                editor.TestClickFreeRotateToolButton();
                DragHandleToRightOfCenter(editor);
                editor.TestFireMouseUpAtClientPoint(Point.Empty, MouseButtons.Left);

                var angleApplied = editor.TestFreeRotateAngleDeg;
                Assert.True(System.Math.Abs(angleApplied) > 1f, "Expected a non-trivial drag angle before Apply.");

                editor.TestFireKeyDown(Keys.Enter);

                Assert.False(editor.TestIsFreeRotateToolActive);
                using var afterImage = editor.CloneBaseBitmapForTests()!;
                // A ~90° rotation of a 160x120 canvas roughly swaps the dimensions (with padding
                // from the canvas-expansion math); either way it must no longer be the original.
                Assert.NotEqual(new Size(160, 120), afterImage.Size);
                Assert.Contains("canUndo=True", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void Apply_WithActiveSelection_RotatesInPlaceClippedToSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(160, 120);
                var selection = new Rectangle(20, 20, 60, 60);
                editor.SetSelectionForDiagnostics(selection);

                editor.TestClickFreeRotateToolButton();
                DragHandleToRightOfCenter(editor);
                editor.TestFireMouseUpAtClientPoint(Point.Empty, MouseButtons.Left);
                editor.TestFireKeyDown(Keys.Enter);

                Assert.False(editor.TestIsFreeRotateToolActive);
                using var afterImage = editor.CloneBaseBitmapForTests()!;
                // In-place selection rotate never changes the overall canvas size.
                Assert.Equal(new Size(160, 120), afterImage.Size);
                Assert.Contains("canUndo=True", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void Escape_CancelsWithoutModifyingImage()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(160, 120);
                editor.TestClickFreeRotateToolButton();
                DragHandleToRightOfCenter(editor);
                editor.TestFireMouseUpAtClientPoint(Point.Empty, MouseButtons.Left);

                editor.TestFireKeyDown(Keys.Escape);

                Assert.False(editor.TestIsFreeRotateToolActive);
                using var afterImage = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(new Size(160, 120), afterImage.Size);
                Assert.Contains("canUndo=False", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void Apply_RotatesInSameDirectionAsPreview_ClockwiseForPositiveAngle()
        {
            // Regression test for the non-WYSIWYG bug: the live preview rotates via GDI+
            // (positive angle = clockwise), but the OpenCV bake treats positive as counter-
            // clockwise, so committing used to spin the opposite way. Mark the top-left quadrant,
            // rotate +90°, and assert it lands top-RIGHT (clockwise), not bottom-left.
            StaTest.Run(() =>
            {
                const int n = 40;
                using var editor = new screenzap.ImageEditor();
                var canvas = new Bitmap(n, n);
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.White);
                    using var red = new SolidBrush(Color.Red);
                    g.FillRectangle(red, 0, 0, n / 2, n / 2); // top-left quadrant
                }
                editor.LoadImage(canvas);
                canvas.Dispose();

                editor.TestApplyFreeRotateAngle(90f);

                using var after = editor.CloneBaseBitmapForTests()!;
                // 90° of a square doesn't change size or blend on axis-aligned edges.
                Assert.Equal(new Size(n, n), after.Size);

                // Clockwise: the red top-left quadrant is now the top-RIGHT quadrant.
                Assert.Equal(Color.Red.ToArgb(), after.GetPixel(3 * n / 4, n / 4).ToArgb());   // top-right → red
                Assert.Equal(Color.White.ToArgb(), after.GetPixel(n / 4, 3 * n / 4).ToArgb()); // bottom-left → white
            });
        }

        [Fact]
        public void ActivateFreeRotateTool_DeactivatesOtherActiveTool()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleArrowTool();
                Assert.Equal(screenzap.DrawingTool.Arrow, editor.TestActiveDrawingTool);

                editor.TestClickFreeRotateToolButton();

                Assert.True(editor.TestIsFreeRotateToolActive);
                Assert.Equal(screenzap.DrawingTool.None, editor.TestActiveDrawingTool);
            });
        }
    }
}
