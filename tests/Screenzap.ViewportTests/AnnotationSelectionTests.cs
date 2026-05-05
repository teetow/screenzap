using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Verifies annotation shape (Arrow / Rectangle) Move-mode interaction: select by click,
    /// translate by drag, delete, deselect by Escape. Uses the real WinForms input pipeline via
    /// TestFireMouseDown/Move/Up so the full pictureBox1_MouseDown cascade is exercised.
    /// </summary>
    public class AnnotationSelectionTests
    {
        /// <summary>
        /// Set up a 120×80 canvas with an arrow drawn from (20,20) to (60,50), arrow tool then
        /// deactivated so we're in Move mode.
        /// </summary>
        private static screenzap.ImageEditor PrepareEditorWithArrow()
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(120, 80);
            using (var g = Graphics.FromImage(canvas))
                g.Clear(Color.White);
            editor.LoadImage(canvas);
            canvas.Dispose();

            // Draw the arrow through the real input pipeline.
            editor.TestToggleArrowTool();
            editor.TestFireMouseDownAtImagePixel(new Point(20, 20), MouseButtons.Left);
            editor.TestFireMouseMoveAtImagePixel(new Point(60, 50), MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(new Point(60, 50), MouseButtons.Left);

            // Deactivate the drawing tool → Move mode.
            editor.TestDeactivateDrawingTool();
            Assert.Equal(screenzap.DrawingTool.None, editor.TestActiveDrawingTool);
            Assert.Equal(1, editor.TestAnnotationShapeCount);
            return editor;
        }

        [Fact]
        public void DrawArrow_InMoveMode_CanSelectByBodyClick()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithArrow();

                // Deselect first (the arrow is selected after drawing).
                editor.TestFireMouseDownAtImagePixel(new Point(100, 5), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(100, 5), MouseButtons.Left);
                Assert.Null(editor.TestSelectedAnnotation);

                // Click on the arrow midpoint.
                editor.TestFireMouseDownAtImagePixel(new Point(40, 35), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(40, 35), MouseButtons.Left);
                Assert.NotNull(editor.TestSelectedAnnotation);
                Assert.Equal(screenzap.AnnotationType.Arrow, editor.TestSelectedAnnotation!.Type);
            });
        }

        [Fact]
        public void SelectArrow_ThenDrag_TranslatesPosition()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithArrow();

                // Arrow is already selected after draw. Drag its body (+10, +10).
                editor.TestFireMouseDownAtImagePixel(new Point(40, 35), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(50, 45), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(50, 45), MouseButtons.Left);

                var a = editor.TestSelectedAnnotation;
                Assert.NotNull(a);
                Assert.Equal(new Point(30, 30), a!.Start);
                Assert.Equal(new Point(70, 60), a.End);
            });
        }

        [Fact]
        public void SelectArrow_ThenDelete_RemovesAnnotation()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithArrow();
                Assert.Equal(1, editor.TestAnnotationShapeCount);

                var msg = new Message();
                editor.TestFireKeyDown(Keys.Delete);

                Assert.Equal(0, editor.TestAnnotationShapeCount);
                Assert.Null(editor.TestSelectedAnnotation);
            });
        }

        [Fact]
        public void SelectArrow_ThenEscape_Deselects()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithArrow();
                Assert.NotNull(editor.TestSelectedAnnotation);

                editor.TestFireKeyDown(Keys.Escape);

                // Still exists, but deselected.
                Assert.Equal(1, editor.TestAnnotationShapeCount);
                Assert.Null(editor.TestSelectedAnnotation);
            });
        }

        [Fact]
        public void DrawRect_InMoveMode_CanSelectByBodyClick()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                var canvas = new Bitmap(120, 80);
                using (var g = Graphics.FromImage(canvas))
                    g.Clear(Color.White);
                editor.LoadImage(canvas);
                canvas.Dispose();

                editor.TestToggleRectTool();
                editor.TestFireMouseDownAtImagePixel(new Point(15, 15), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(55, 45), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(55, 45), MouseButtons.Left);
                editor.TestDeactivateDrawingTool();

                // Deselect.
                editor.TestFireMouseDownAtImagePixel(new Point(100, 5), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(100, 5), MouseButtons.Left);
                Assert.Null(editor.TestSelectedAnnotation);

                // Click inside the rectangle body.
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                Assert.NotNull(editor.TestSelectedAnnotation);
                Assert.Equal(screenzap.AnnotationType.Rectangle, editor.TestSelectedAnnotation!.Type);
            });
        }

        [Fact]
        public void EmptyClick_InMoveMode_DeselectsAnnotation()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithArrow();
                Assert.NotNull(editor.TestSelectedAnnotation);

                // Click on empty canvas area far from the arrow.
                editor.TestFireMouseDownAtImagePixel(new Point(100, 5), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(100, 5), MouseButtons.Left);

                Assert.Null(editor.TestSelectedAnnotation);
                Assert.Equal(1, editor.TestAnnotationShapeCount); // still exists
            });
        }
    }
}
