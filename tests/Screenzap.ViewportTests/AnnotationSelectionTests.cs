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

        /// <summary>
        /// Set up two annotations (rect then arrow), each drawn through the real pipeline and
        /// then deactivated → Move mode. The rect sits at (15,15)–(55,45) (interior (35,30)),
        /// the arrow runs from (60,60) to (110,75) (midpoint ~(85,67)). They don't overlap, so
        /// hit-tests for each are unambiguous.
        /// </summary>
        private static screenzap.ImageEditor PrepareEditorWithRectThenArrow()
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(140, 100);
            using (var g = Graphics.FromImage(canvas))
                g.Clear(Color.White);
            editor.LoadImage(canvas);
            canvas.Dispose();

            editor.TestToggleRectTool();
            editor.TestFireMouseDownAtImagePixel(new Point(15, 15), MouseButtons.Left);
            editor.TestFireMouseMoveAtImagePixel(new Point(55, 45), MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(new Point(55, 45), MouseButtons.Left);
            editor.TestDeactivateDrawingTool();

            editor.TestToggleArrowTool();
            editor.TestFireMouseDownAtImagePixel(new Point(60, 60), MouseButtons.Left);
            editor.TestFireMouseMoveAtImagePixel(new Point(110, 75), MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(new Point(110, 75), MouseButtons.Left);
            editor.TestDeactivateDrawingTool();

            Assert.Equal(2, editor.TestAnnotationShapeCount);
            return editor;
        }

        /// <summary>
        /// Variant: user never explicitly exits the drawing tool — they just switch tools.
        /// Mirrors what likely happens in the running app: pick rect tool, drag rect, pick
        /// arrow tool, drag arrow. After step 2 activeTool is still Arrow.
        /// </summary>
        private static screenzap.ImageEditor PrepareEditorWithRectThenArrow_ToolStaysActive()
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(140, 100);
            using (var g = Graphics.FromImage(canvas))
                g.Clear(Color.White);
            editor.LoadImage(canvas);
            canvas.Dispose();

            editor.TestToggleRectTool();
            editor.TestFireMouseDownAtImagePixel(new Point(15, 15), MouseButtons.Left);
            editor.TestFireMouseMoveAtImagePixel(new Point(55, 45), MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(new Point(55, 45), MouseButtons.Left);

            editor.TestToggleArrowTool();
            editor.TestFireMouseDownAtImagePixel(new Point(60, 60), MouseButtons.Left);
            editor.TestFireMouseMoveAtImagePixel(new Point(110, 75), MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(new Point(110, 75), MouseButtons.Left);

            Assert.Equal(2, editor.TestAnnotationShapeCount);
            return editor;
        }

        /// <summary>
        /// User's full repro: rect, arrow, click rect, change line width, select arrow,
        /// then move rect. Drawing tool stays active throughout — the editor should
        /// recognize clicks on existing shapes as selection (and exit drawing-tool
        /// mode), not as new-shape creation.
        /// </summary>
        [Fact]
        public void RectThenArrow_ToolStaysActive_ClickRect_SelectsRect_NoNewShape()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectThenArrow_ToolStaysActive();
                Assert.Equal(screenzap.DrawingTool.Arrow, editor.TestActiveDrawingTool);

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);

                Assert.Equal(2, editor.TestAnnotationShapeCount);
                Assert.Equal(screenzap.AnnotationType.Rectangle, editor.TestSelectedAnnotation!.Type);
                // Selecting an existing shape exits drawing-tool mode.
                Assert.Equal(screenzap.DrawingTool.None, editor.TestActiveDrawingTool);
            });
        }

        [Fact]
        public void RectThenArrow_ToolStaysActive_ClickRectThenChangeLineWidth_StillSelectsArrow()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectThenArrow_ToolStaysActive();

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                Assert.Equal(screenzap.AnnotationType.Rectangle, editor.TestSelectedAnnotation!.Type);

                editor.TestSetAnnotationLineThickness(6f);
                Assert.Equal(6f, editor.TestSelectedAnnotation!.LineThickness);

                editor.TestFireMouseDownAtImagePixel(new Point(85, 67), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(85, 67), MouseButtons.Left);
                Assert.Equal(screenzap.AnnotationType.Arrow, editor.TestSelectedAnnotation!.Type);
                Assert.Equal(2, editor.TestAnnotationShapeCount);
            });
        }

        [Fact]
        public void RectThenArrow_ToolStaysActive_ClickRectThenChangeLineWidth_RectStillMovable()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectThenArrow_ToolStaysActive();

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                Assert.Equal(screenzap.AnnotationType.Rectangle, editor.TestSelectedAnnotation!.Type);
                var rectBefore = editor.TestSelectedAnnotation!;
                var startBefore = rectBefore.Start;
                var endBefore = rectBefore.End;

                editor.TestSetAnnotationLineThickness(6f);

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(43, 35), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(43, 35), MouseButtons.Left);

                Assert.Same(rectBefore, editor.TestSelectedAnnotation);
                Assert.Equal(new Point(startBefore.X + 8, startBefore.Y + 5), editor.TestSelectedAnnotation!.Start);
                Assert.Equal(new Point(endBefore.X + 8, endBefore.Y + 5), editor.TestSelectedAnnotation!.End);
            });
        }

        /// <summary>
        /// User-reported repro: rect, then arrow, then click rect to select, then change its
        /// line width via the toolbar, then try to select the arrow. After the line-width
        /// change, arrow selection must still work — and the rect must still be movable.
        /// </summary>
        [Fact]
        public void RectThenArrow_ChangeRectLineWidth_StillSelectsArrow()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectThenArrow();

                // Click rect interior — selects rect.
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                Assert.NotNull(editor.TestSelectedAnnotation);
                Assert.Equal(screenzap.AnnotationType.Rectangle, editor.TestSelectedAnnotation!.Type);

                // Change line width through the real combobox event.
                editor.TestSetAnnotationLineThickness(6f);
                Assert.Equal(6f, editor.TestSelectedAnnotation!.LineThickness);

                // Now click the arrow midpoint — it should become selected.
                editor.TestFireMouseDownAtImagePixel(new Point(85, 67), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(85, 67), MouseButtons.Left);
                Assert.NotNull(editor.TestSelectedAnnotation);
                Assert.Equal(screenzap.AnnotationType.Arrow, editor.TestSelectedAnnotation!.Type);
            });
        }

        [Fact]
        public void RectThenArrow_ChangeRectLineWidth_RectStillMovable()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectThenArrow();

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                Assert.Equal(screenzap.AnnotationType.Rectangle, editor.TestSelectedAnnotation!.Type);
                var rectBefore = editor.TestSelectedAnnotation!;
                var startBefore = rectBefore.Start;
                var endBefore = rectBefore.End;

                editor.TestSetAnnotationLineThickness(6f);

                // Drag the rect body by (+8, +5) — must translate.
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(43, 35), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(43, 35), MouseButtons.Left);

                Assert.Same(rectBefore, editor.TestSelectedAnnotation);
                Assert.Equal(new Point(startBefore.X + 8, startBefore.Y + 5), editor.TestSelectedAnnotation!.Start);
                Assert.Equal(new Point(endBefore.X + 8, endBefore.Y + 5), editor.TestSelectedAnnotation!.End);
            });
        }

        [Fact]
        public void NewShape_UsesToolDefaultColor()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                var canvas = new Bitmap(120, 80);
                using (var g = Graphics.FromImage(canvas))
                    g.Clear(Color.White);
                editor.LoadImage(canvas);
                canvas.Dispose();

                // Default color is Red on a fresh editor.
                Assert.Equal(Color.Red.ToArgb(), editor.TestAnnotationColorDefault.ToArgb());

                editor.TestToggleArrowTool();
                editor.TestFireMouseDownAtImagePixel(new Point(20, 20), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(60, 50), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(60, 50), MouseButtons.Left);

                Assert.Equal(Color.Red.ToArgb(), editor.TestSelectedAnnotation!.Color.ToArgb());
            });
        }

        [Fact]
        public void SetColor_AppliesToSelection_AndBecomesNewDefault()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithArrow();
                Assert.NotNull(editor.TestSelectedAnnotation);
                Assert.Equal(Color.Red.ToArgb(), editor.TestSelectedAnnotation!.Color.ToArgb());

                editor.TestSetAnnotationColor(Color.Blue);

                Assert.Equal(Color.Blue.ToArgb(), editor.TestSelectedAnnotation!.Color.ToArgb());
                Assert.Equal(Color.Blue.ToArgb(), editor.TestAnnotationColorDefault.ToArgb());

                // Draw a second arrow — should pick up the new default.
                editor.TestToggleArrowTool();
                editor.TestFireMouseDownAtImagePixel(new Point(80, 20), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(110, 60), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(110, 60), MouseButtons.Left);

                Assert.Equal(Color.Blue.ToArgb(), editor.TestSelectedAnnotation!.Color.ToArgb());
            });
        }

        [Fact]
        public void SetColor_IsUndoable()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithArrow();
                var arrow = editor.TestSelectedAnnotation!;
                Assert.Equal(Color.Red.ToArgb(), arrow.Color.ToArgb());

                editor.TestSetAnnotationColor(Color.Green);
                Assert.Equal(Color.Green.ToArgb(), editor.TestSelectedAnnotation!.Color.ToArgb());

                editor.TestFireKeyDown(Keys.Control | Keys.Z);

                // Undo restores prior color. The annotation list was rebuilt from the snapshot
                // so the in-list shape may not be the same reference — look up the only
                // remaining annotation in the editor.
                Assert.Equal(1, editor.TestAnnotationShapeCount);
                Assert.Equal(Color.Red.ToArgb(), editor.TestSelectedAnnotation!.Color.ToArgb());
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
