using System;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// The constraints and keyboard transforms that were asymmetric after the first pass:
    /// Shift on arrows and highlighters (not just rectangles), Ctrl+Arrow sizing a text block,
    /// and arrow keys driving a selected image layer the way they already drive shapes.
    /// </summary>
    public class ConstraintAndLayerKeyboardTests
    {
        private static screenzap.ImageDocumentEditor NewEditor(int width = 200, int height = 160)
            => EditorFixture.WithCanvas(width, height);

        // ── Shift snaps an arrow to 45° ─────────────────────────────────────────────

        [Fact]
        public void ShiftDragArrow_SnapsToNearestFortyFiveDegrees()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestToggleArrowTool();
                editor.TestSetShiftHeld(true);
                try
                {
                    editor.TestFireMouseDownAtImagePixel(new Point(40, 40), MouseButtons.Left);
                    // 50 across, 8 down — a shallow drag that should flatten to horizontal.
                    editor.TestFireMouseMoveAtImagePixel(new Point(90, 48), MouseButtons.Left);
                    editor.TestFireMouseUpAtImagePixel(new Point(90, 48), MouseButtons.Left);
                }
                finally
                {
                    editor.TestSetShiftHeld(false);
                }

                var shape = editor.TestSelectedAnnotation;
                Assert.NotNull(shape);
                Assert.Equal(new Point(40, 40), shape!.Start);
                Assert.Equal(40, shape.End.Y);          // snapped flat
                Assert.True(shape.End.X > 80, $"expected the length preserved, got {shape.End}");
            });
        }

        [Fact]
        public void ShiftDragArrow_NearDiagonal_SnapsToExactDiagonal()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestToggleArrowTool();
                editor.TestSetShiftHeld(true);
                try
                {
                    editor.TestFireMouseDownAtImagePixel(new Point(30, 30), MouseButtons.Left);
                    editor.TestFireMouseMoveAtImagePixel(new Point(90, 82), MouseButtons.Left);
                    editor.TestFireMouseUpAtImagePixel(new Point(90, 82), MouseButtons.Left);
                }
                finally
                {
                    editor.TestSetShiftHeld(false);
                }

                var shape = editor.TestSelectedAnnotation!;
                int dx = shape.End.X - shape.Start.X;
                int dy = shape.End.Y - shape.Start.Y;
                Assert.Equal(dx, dy);
            });
        }

        [Fact]
        public void DragArrow_WithoutShift_KeepsTheFreeAngle()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestToggleArrowTool();
                editor.TestFireMouseDownAtImagePixel(new Point(40, 40), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(90, 48), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(90, 48), MouseButtons.Left);

                var shape = editor.TestSelectedAnnotation!;
                Assert.Equal(new Point(90, 48), shape.End);
            });
        }

        [Fact]
        public void ShiftDragArrowEndHandle_SnapsAboutTheOtherEnd()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestToggleArrowTool();
                editor.TestFireMouseDownAtImagePixel(new Point(40, 40), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(100, 60), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(100, 60), MouseButtons.Left);
                editor.TestDeactivateDrawingTool();

                var shape = editor.TestSelectedAnnotation!;

                // Grab the arrowhead and Shift-drag it to a shallow angle.
                editor.TestFireMouseDownAtImagePixel(new Point(100, 60), MouseButtons.Left);
                editor.TestSetShiftHeld(true);
                try
                {
                    editor.TestFireMouseMoveAtImagePixel(new Point(140, 46), MouseButtons.Left);
                    editor.TestFireMouseUpAtImagePixel(new Point(140, 46), MouseButtons.Left);
                }
                finally
                {
                    editor.TestSetShiftHeld(false);
                }

                // Start (the pivot) is untouched, and the arrow ends up axis-aligned.
                Assert.Equal(new Point(40, 40), shape.Start);
                Assert.Equal(40, shape.End.Y);
            });
        }

        // ── Shift locks a highlighter's aspect on a corner drag ─────────────────────

        [Fact]
        public void ShiftDragHighlighterCorner_ScalesUniformly()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestToggleHighlighterTool();
                editor.TestDrawHighlighterStroke(new[]
                {
                    new Point(20, 20), new Point(50, 40), new Point(80, 20), new Point(100, 40)
                });
                editor.TestDeactivateDrawingTool();

                var shape = editor.TestSelectedAnnotation!;
                var before = shape.GetBounds();

                // Drag the bottom-right corner outward with Shift held.
                editor.TestFireMouseDownAtImagePixel(new Point(before.Right, before.Bottom), MouseButtons.Left);
                editor.TestSetShiftHeld(true);
                try
                {
                    var target = new Point(before.Left + before.Width * 2, before.Top + before.Height + 4);
                    editor.TestFireMouseMoveAtImagePixel(target, MouseButtons.Left);
                    editor.TestFireMouseUpAtImagePixel(target, MouseButtons.Left);
                }
                finally
                {
                    editor.TestSetShiftHeld(false);
                }

                var after = shape.GetBounds();
                // Uniform scale: both axes grew by the same factor, so the aspect is preserved.
                double aspectBefore = (double)before.Width / before.Height;
                double aspectAfter = (double)after.Width / after.Height;
                Assert.True(
                    Math.Abs(aspectBefore - aspectAfter) < 0.15,
                    $"aspect drifted: {aspectBefore:F3} -> {aspectAfter:F3} ({before} -> {after})");
                Assert.True(after.Width > before.Width, "the stroke should have grown");
            });
        }

        // ── Ctrl+Arrow sizes a text block by font size ──────────────────────────────

        [Fact]
        public void CtrlArrow_ChangesSelectedTextFontSize()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                editor.TestFireMouseDownAtImagePixel(new Point(34, 36), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(34, 36), MouseButtons.Left);
                Assert.Equal(1, editor.TestSelectedTextCount);

                float before = editor.TestTextAnnotations[0].FontSize;

                Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Down));
                Assert.Equal(before + 1f, editor.TestTextAnnotations[0].FontSize);

                Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Shift | Keys.Up));
                Assert.Equal(before - 9f, editor.TestTextAnnotations[0].FontSize);
            });
        }

        [Fact]
        public void CtrlArrow_ShrinkingText_StopsAtTheMinimumSize()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                editor.TestFireMouseDownAtImagePixel(new Point(34, 36), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(34, 36), MouseButtons.Left);

                for (int i = 0; i < 20; i++)
                {
                    editor.TestFireProcessCmdKey(Keys.Control | Keys.Shift | Keys.Left);
                }

                Assert.Equal(4f, editor.TestTextAnnotations[0].FontSize);
            });
        }

        [Fact]
        public void PlainArrow_StillMovesTextRatherThanResizingIt()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                editor.TestFireMouseDownAtImagePixel(new Point(34, 36), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(34, 36), MouseButtons.Left);

                float size = editor.TestTextAnnotations[0].FontSize;
                Assert.True(editor.TestFireProcessCmdKey(Keys.Right));

                Assert.Equal(new Point(31, 30), editor.TestTextAnnotations[0].Position);
                Assert.Equal(size, editor.TestTextAnnotations[0].FontSize);
            });
        }

        // ── Arrow keys drive a selected image layer ─────────────────────────────────

        private static screenzap.ImageDocumentEditor EditorWithSelectedLayer(out RectangleF frame)
        {
            var editor = NewEditor(120, 100);
            using (var pasted = EditorFixture.Canvas(20, 14, Color.Magenta))
            {
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());
            }

            frame = editor.GetImageLayerFrameForTests(0);
            return editor;
        }

        [Fact]
        public void ArrowKeys_MoveSelectedImageLayer()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorWithSelectedLayer(out var frame);

                Assert.True(editor.TestFireProcessCmdKey(Keys.Right));
                Assert.True(editor.TestFireProcessCmdKey(Keys.Shift | Keys.Down));

                var moved = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.X + 1f, moved.X);
                Assert.Equal(frame.Y + 10f, moved.Y);
                Assert.Equal(frame.Size, moved.Size);
            });
        }

        [Fact]
        public void CtrlArrow_ResizesSelectedImageLayerFromTopLeft()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorWithSelectedLayer(out var frame);

                Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Right));
                Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Shift | Keys.Down));

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.Location, resized.Location);
                Assert.Equal(frame.Width + 1f, resized.Width);
                Assert.Equal(frame.Height + 10f, resized.Height);
            });
        }

        [Fact]
        public void LayerArrowKeyNudges_CoalesceIntoOneUndoStep()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorWithSelectedLayer(out var frame);

                editor.TestFireProcessCmdKey(Keys.Right);
                editor.TestFireProcessCmdKey(Keys.Right);
                editor.TestFireProcessCmdKey(Keys.Right);
                editor.TestFireKeyUp(Keys.Right);
                Assert.Equal(frame.X + 3f, editor.GetImageLayerFrameForTests(0).X);

                editor.TestFireKeyDown(Keys.Control | Keys.Z);

                Assert.Equal(frame.X, editor.GetImageLayerFrameForTests(0).X);
            });
        }

        [Fact]
        public void ArrowKeys_WithNoLayerSelected_StillReachTheMarquee()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.SetSelectionForDiagnostics(new Rectangle(40, 40, 20, 20));

                Assert.True(editor.TestFireProcessCmdKey(Keys.Right));

                Assert.Equal(new Rectangle(41, 40, 20, 20), editor.SelectionDiagnostics.Selection);
            });
        }
    }
}
