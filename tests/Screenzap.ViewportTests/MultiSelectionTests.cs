using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Cross-type multi-selection: shift-click to add, color picker writes the union,
    /// drag/delete operate on every selected item. Single-target legacy behaviour for
    /// line width / arrow size / font controls is covered by existing test suites.
    /// </summary>
    public class MultiSelectionTests
    {
        /// <summary>
        /// 140x100 canvas with three annotations placed so each has unambiguous body hits:
        ///   - Rect at (15,15)-(55,45), interior (35,30)
        ///   - Arrow from (60,60)-(110,75), midpoint (85,67)
        ///   - Text "M" at position (10,80). Text bounds vary by font metrics, but the
        ///     position point itself is the click target used by HitTestTextAnnotation
        ///     in object-mode (clamp matches bounds containment for a 1-char string).
        /// All three are drawn through the real input pipeline, then their drawing tools
        /// are deactivated → editor sits in Move mode for the assertion phase.
        /// </summary>
        private static screenzap.ImageEditor PrepareEditorWithRectArrowAndText()
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(160, 120);
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

            return editor;
        }

        [Fact]
        public void ShiftClick_AddsSecondShape_ToSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();

                // Single-click rect → only rect selected.
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                Assert.Equal(1, editor.TestSelectedShapeCount);
                Assert.Equal(screenzap.AnnotationType.Rectangle, editor.TestSelectedShapes[0].Type);

                // Shift-click arrow → both selected.
                editor.TestShiftClickAtImagePixel(new Point(85, 67));
                Assert.Equal(2, editor.TestSelectedShapeCount);
                Assert.Contains(editor.TestSelectedShapes, s => s.Type == screenzap.AnnotationType.Rectangle);
                Assert.Contains(editor.TestSelectedShapes, s => s.Type == screenzap.AnnotationType.Arrow);
            });
        }

        [Fact]
        public void ShiftClick_OnSelectedShape_RemovesFromSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestShiftClickAtImagePixel(new Point(85, 67));
                Assert.Equal(2, editor.TestSelectedShapeCount);

                // Shift-click rect again → toggles it off.
                editor.TestShiftClickAtImagePixel(new Point(35, 30));
                Assert.Equal(1, editor.TestSelectedShapeCount);
                Assert.Equal(screenzap.AnnotationType.Arrow, editor.TestSelectedShapes[0].Type);
            });
        }

        [Fact]
        public void PlainClick_OnSelectedShape_PreservesMultiSelection()
        {
            // Figma-style: clicking an item that's already part of the multi-selection
            // doesn't collapse to single-select — that would silently break drag-the-group.
            // The user can shift-click off items or click empty canvas to narrow the
            // selection instead.
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestShiftClickAtImagePixel(new Point(85, 67));
                Assert.Equal(2, editor.TestSelectedShapeCount);

                // Plain click on the rect (already selected) → multi-selection preserved.
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                Assert.Equal(2, editor.TestSelectedShapeCount);
            });
        }

        [Fact]
        public void PlainClick_OnUnselectedShape_ReplacesSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();

                // Select only the rect first.
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                Assert.Equal(1, editor.TestSelectedShapeCount);
                Assert.Equal(screenzap.AnnotationType.Rectangle, editor.TestSelectedShapes[0].Type);

                // Plain click on the arrow (NOT in the selection) → selection replaces.
                editor.TestFireMouseDownAtImagePixel(new Point(85, 67), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(85, 67), MouseButtons.Left);
                Assert.Equal(1, editor.TestSelectedShapeCount);
                Assert.Equal(screenzap.AnnotationType.Arrow, editor.TestSelectedShapes[0].Type);
            });
        }

        [Fact]
        public void ApplyColorToSelection_PaintsAllSelectedShapes()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestShiftClickAtImagePixel(new Point(85, 67));
                Assert.Equal(2, editor.TestSelectedShapeCount);

                editor.TestApplyColorToSelection(Color.Blue);

                foreach (var shape in editor.TestSelectedShapes)
                {
                    Assert.Equal(Color.Blue.ToArgb(), shape.Color.ToArgb());
                }
            });
        }

        [Fact]
        public void ColorButton_ShowsMixedLabel_WhenSelectedColorsDiffer()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();

                // Select rect, paint it blue.
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestApplyColorToSelection(Color.Blue);

                // Add arrow to selection — still has default red. Now selection is mixed.
                editor.TestShiftClickAtImagePixel(new Point(85, 67));
                Assert.Equal(2, editor.TestSelectedShapeCount);
                Assert.Equal("Mixed", editor.TestAnnotationColorButtonText);
            });
        }

        [Fact]
        public void ColorButton_ShowsUnanimousColor_WhenAllSelectedAgree()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestShiftClickAtImagePixel(new Point(85, 67));
                editor.TestApplyColorToSelection(Color.Green);

                Assert.Equal("Color", editor.TestAnnotationColorButtonText);
                Assert.Equal(Color.Green.ToArgb(), editor.TestAnnotationColorButtonBackColor.ToArgb());
            });
        }

        [Fact]
        public void MultiDrag_ShapeBody_MovesAllSelectedShapes()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestShiftClickAtImagePixel(new Point(85, 67));
                Assert.Equal(2, editor.TestSelectedShapeCount);

                var rect = editor.TestSelectedShapes[0];
                var arrow = editor.TestSelectedShapes[1];
                var rectStart = rect.Start; var rectEnd = rect.End;
                var arrowStart = arrow.Start; var arrowEnd = arrow.End;

                // Drag from inside the rect body by (+8, +5). The arrow shouldn't have been
                // grabbed directly but it should still translate together because it's in
                // the selection.
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(43, 35), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(43, 35), MouseButtons.Left);

                Assert.Equal(new Point(rectStart.X + 8, rectStart.Y + 5), rect.Start);
                Assert.Equal(new Point(rectEnd.X + 8, rectEnd.Y + 5), rect.End);
                Assert.Equal(new Point(arrowStart.X + 8, arrowStart.Y + 5), arrow.Start);
                Assert.Equal(new Point(arrowEnd.X + 8, arrowEnd.Y + 5), arrow.End);
            });
        }

        [Fact]
        public void MultiDelete_RemovesEveryShapeInSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();
                Assert.Equal(2, editor.TestAnnotationShapeCount);

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestShiftClickAtImagePixel(new Point(85, 67));
                Assert.Equal(2, editor.TestSelectedShapeCount);

                editor.TestFireKeyDown(Keys.Delete);

                Assert.Equal(0, editor.TestAnnotationShapeCount);
                Assert.Equal(0, editor.TestSelectedShapeCount);
            });
        }

        [Fact]
        public void MultiDelete_IsUndoable_AsSingleStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestShiftClickAtImagePixel(new Point(85, 67));
                editor.TestFireKeyDown(Keys.Delete);
                Assert.Equal(0, editor.TestAnnotationShapeCount);

                editor.TestFireKeyDown(Keys.Control | Keys.Z);

                Assert.Equal(2, editor.TestAnnotationShapeCount);
            });
        }

        [Fact]
        public void MultiDrag_IsUndoable_AsSingleStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestShiftClickAtImagePixel(new Point(85, 67));

                var rect = editor.TestSelectedShapes[0];
                var arrow = editor.TestSelectedShapes[1];
                var rectStartBefore = rect.Start;
                var arrowStartBefore = arrow.Start;

                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(43, 35), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(43, 35), MouseButtons.Left);

                editor.TestFireKeyDown(Keys.Control | Keys.Z);

                // Both should be restored from the same undo step.
                Assert.Equal(rectStartBefore, editor.TestSelectedShapes[0].Start);
                Assert.Equal(arrowStartBefore, editor.TestSelectedShapes[1].Start);
            });
        }

        [Fact]
        public void ApplyColorToSelection_IsUndoable_AsSingleStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithRectArrowAndText();
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestShiftClickAtImagePixel(new Point(85, 67));

                editor.TestApplyColorToSelection(Color.Blue);
                foreach (var s in editor.TestSelectedShapes)
                    Assert.Equal(Color.Blue.ToArgb(), s.Color.ToArgb());

                editor.TestFireKeyDown(Keys.Control | Keys.Z);

                foreach (var s in editor.TestSelectedShapes)
                    Assert.Equal(Color.Red.ToArgb(), s.Color.ToArgb());
            });
        }
    }
}
