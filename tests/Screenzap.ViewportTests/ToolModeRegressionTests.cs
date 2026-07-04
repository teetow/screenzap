using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Tool-mode hardening: object-click vs tool-icon activation must leave the editor in a
    /// consistent mode, plain click must replace the WHOLE selection (shapes + texts), and
    /// modal tools (straighten, censor) must own the keyboard while engaged. Each test drives
    /// the real input pipeline (TestFireMouse*/TestFireKeyDown).
    /// </summary>
    public class ToolModeRegressionTests
    {
        /// <summary>160×120 white canvas, no annotations.</summary>
        private static screenzap.ImageEditor PrepareEditor()
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(160, 120);
            using (var g = Graphics.FromImage(canvas))
                g.Clear(Color.White);
            editor.LoadImage(canvas);
            canvas.Dispose();
            return editor;
        }

        /// <summary>Draw a rect (15,15)-(55,45) through the pipeline, then exit the tool.</summary>
        private static void DrawRectInMoveMode(screenzap.ImageEditor editor)
        {
            editor.TestToggleRectTool();
            editor.TestFireMouseDownAtImagePixel(new Point(15, 15), MouseButtons.Left);
            editor.TestFireMouseMoveAtImagePixel(new Point(55, 45), MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(new Point(55, 45), MouseButtons.Left);
            editor.TestDeactivateDrawingTool();
        }

        private static void ClickAtImagePixel(screenzap.ImageEditor editor, Point p)
        {
            editor.TestFireMouseDownAtImagePixel(p, MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(p, MouseButtons.Left);
        }

        // Text sits well away from the rect; the click target is a few px inside its origin.
        private static readonly Point TextPosition = new Point(20, 90);
        private static readonly Point TextClickPoint = new Point(24, 94);

        [Fact]
        public void ClickText_WithDrawingToolActive_DropsToolAndSelectsText()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestAddTextAnnotation(TextPosition, "Note");

                editor.TestToggleArrowTool();
                Assert.Equal(screenzap.DrawingTool.Arrow, editor.TestActiveDrawingTool);

                ClickAtImagePixel(editor, TextClickPoint);

                // Same rule as clicking a shape: object selection wins, the tool drops.
                Assert.Equal(1, editor.TestSelectedTextCount);
                Assert.Equal(screenzap.DrawingTool.None, editor.TestActiveDrawingTool);

                // A follow-up canvas click must NOT draw an arrow.
                ClickAtImagePixel(editor, new Point(140, 20));
                Assert.Equal(0, editor.TestAnnotationShapeCount);
            });
        }

        [Fact]
        public void PlainClickShape_ClearsTextSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                DrawRectInMoveMode(editor);
                editor.TestAddTextAnnotation(TextPosition, "Note");

                ClickAtImagePixel(editor, TextClickPoint);
                Assert.Equal(1, editor.TestSelectedTextCount);

                // Plain click on the rect replaces the whole selection.
                ClickAtImagePixel(editor, new Point(35, 30));
                Assert.Equal(1, editor.TestSelectedShapeCount);
                Assert.Equal(0, editor.TestSelectedTextCount);
            });
        }

        [Fact]
        public void PlainClickText_ClearsShapeSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                DrawRectInMoveMode(editor);
                editor.TestAddTextAnnotation(TextPosition, "Note");

                ClickAtImagePixel(editor, new Point(35, 30));
                Assert.Equal(1, editor.TestSelectedShapeCount);

                ClickAtImagePixel(editor, TextClickPoint);
                Assert.Equal(1, editor.TestSelectedTextCount);
                Assert.Equal(0, editor.TestSelectedShapeCount);
            });
        }

        [Fact]
        public void ShiftClick_AcrossTypes_StillBuildsMixedSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                DrawRectInMoveMode(editor);
                editor.TestAddTextAnnotation(TextPosition, "Note");

                ClickAtImagePixel(editor, new Point(35, 30));
                editor.TestShiftClickAtImagePixel(TextClickPoint);

                Assert.Equal(1, editor.TestSelectedShapeCount);
                Assert.Equal(1, editor.TestSelectedTextCount);
            });
        }

        [Fact]
        public void DragShape_AfterPlainClick_DoesNotMoveStaleSelectedText()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                DrawRectInMoveMode(editor);
                editor.TestAddTextAnnotation(TextPosition, "Note");

                // Select the text, then plain-click + drag the rect by (+8, +5).
                ClickAtImagePixel(editor, TextClickPoint);
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(43, 35), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(43, 35), MouseButtons.Left);

                // The text must not have ridden along with the drag.
                Assert.Equal(1, editor.TestTextAnnotationCount);
                Assert.Contains($"pos={TextPosition}", editor.TestDescribeTextAnnotations());
            });
        }

        [Fact]
        public void StraightenButton_SecondClick_CancelsMode()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();

                editor.TestClickStraightenToolButton();
                Assert.True(editor.TestIsStraightenToolActive);
                Assert.True(editor.TestStraightenButtonChecked);

                // Rail icons toggle: clicking the checked icon cancels the mode (like Esc).
                editor.TestClickStraightenToolButton();
                Assert.False(editor.TestIsStraightenToolActive);
                Assert.False(editor.TestStraightenButtonChecked);
            });
        }

        [Fact]
        public void ActivateStraightenTool_WhenAlreadyActive_PreservesReferenceLine()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();

                editor.TestFireKeyDown(Keys.Control | Keys.L);
                Assert.True(editor.TestIsStraightenToolActive);

                // Draw a slightly tilted reference line.
                editor.TestFireMouseDownAtImagePixel(new Point(10, 10), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(100, 14), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(100, 14), MouseButtons.Left);
                Assert.Equal(new Point(10, 10), editor.TestStraightenLineStart);
                Assert.Equal(new Point(100, 14), editor.TestStraightenLineEnd);

                // Programmatic re-activation must be idempotent — no line reset.
                Assert.True(editor.ActivateStraightenTool());
                Assert.True(editor.TestIsStraightenToolActive);
                Assert.Equal(new Point(10, 10), editor.TestStraightenLineStart);
                Assert.Equal(new Point(100, 14), editor.TestStraightenLineEnd);

                // Escape exits the mode and unchecks the rail button.
                editor.TestFireKeyDown(Keys.Escape);
                Assert.False(editor.TestIsStraightenToolActive);
                Assert.False(editor.TestStraightenButtonChecked);
            });
        }

        [Fact]
        public void CtrlA_InCensorMode_SelectsAllRegions()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestEnterCensorModeWithRegions(
                    new Rectangle(10, 10, 40, 12),
                    new Rectangle(60, 40, 50, 14));
                Assert.Equal(0, editor.TestSelectedCensorRegionCount);

                editor.TestFireKeyDown(Keys.Control | Keys.A);

                Assert.Equal(2, editor.TestSelectedCensorRegionCount);

                // Escape cancels the mode and unchecks the rail button.
                editor.TestFireKeyDown(Keys.Escape);
                Assert.False(editor.TestIsCensorToolActive);
                Assert.False(editor.TestCensorButtonChecked);
            });
        }

        [Fact]
        public void ArmedTool_DragAcrossExistingShape_DrawsNewShape()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                DrawRectInMoveMode(editor);
                Assert.Equal(1, editor.TestAnnotationShapeCount);

                // Gesture rule: with a tool armed, a DRAG starting inside an existing
                // shape's bounds draws a new shape instead of selecting the old one.
                editor.TestToggleArrowTool();
                editor.TestFireMouseDownAtImagePixel(new Point(35, 30), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(50, 42), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(50, 42), MouseButtons.Left);

                Assert.Equal(2, editor.TestAnnotationShapeCount);
                Assert.Equal(screenzap.DrawingTool.Arrow, editor.TestActiveDrawingTool);
            });
        }

        [Fact]
        public void ArmedTool_ClickEmptyCanvas_KeepsToolArmed_DrawsNothing()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();

                editor.TestToggleArrowTool();
                ClickAtImagePixel(editor, new Point(100, 10));

                Assert.Equal(0, editor.TestAnnotationShapeCount);
                Assert.Equal(screenzap.DrawingTool.Arrow, editor.TestActiveDrawingTool);
                Assert.Equal(0, editor.TestSelectedShapeCount);
            });
        }

        [Fact]
        public void EscapeLadder_StepsOneLevelPerPress()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();

                // Draw a rect; the tool stays armed and the new shape is selected.
                editor.TestToggleRectTool();
                editor.TestFireMouseDownAtImagePixel(new Point(15, 15), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(55, 45), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(55, 45), MouseButtons.Left);
                Assert.Equal(1, editor.TestSelectedShapeCount);
                Assert.Equal(screenzap.DrawingTool.Rectangle, editor.TestActiveDrawingTool);

                // Esc #1: clear the selection, keep the tool.
                editor.TestFireKeyDown(Keys.Escape);
                Assert.Equal(0, editor.TestSelectedShapeCount);
                Assert.Equal(screenzap.DrawingTool.Rectangle, editor.TestActiveDrawingTool);

                // Esc #2: drop the tool → Move mode.
                editor.TestFireKeyDown(Keys.Escape);
                Assert.Equal(screenzap.DrawingTool.None, editor.TestActiveDrawingTool);
                Assert.True(editor.TestMoveButtonChecked);
            });
        }

        [Fact]
        public void EscapeLadder_MixedSelection_ClearsBothTypesInOneStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                DrawRectInMoveMode(editor);
                editor.TestAddTextAnnotation(TextPosition, "Note");

                ClickAtImagePixel(editor, new Point(35, 30));
                editor.TestShiftClickAtImagePixel(TextClickPoint);
                Assert.Equal(1, editor.TestSelectedShapeCount);
                Assert.Equal(1, editor.TestSelectedTextCount);

                // Selection is ONE ladder level: a single Esc clears both types.
                editor.TestFireKeyDown(Keys.Escape);
                Assert.Equal(0, editor.TestSelectedShapeCount);
                Assert.Equal(0, editor.TestSelectedTextCount);
            });
        }

        [Fact]
        public void MoveButton_TracksActiveTool_AndCancelsOnClick()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                Assert.True(editor.TestMoveButtonChecked);

                editor.TestToggleArrowTool();
                Assert.False(editor.TestMoveButtonChecked);

                editor.TestClickMoveToolButton();
                Assert.Equal(screenzap.DrawingTool.None, editor.TestActiveDrawingTool);
                Assert.True(editor.TestMoveButtonChecked);

                // Clicking Move during a modal tool cancels it too.
                editor.TestEnterCensorModeWithRegions(new Rectangle(10, 10, 40, 12));
                Assert.False(editor.TestMoveButtonChecked);
                editor.TestClickMoveToolButton();
                Assert.False(editor.TestIsCensorToolActive);
                Assert.True(editor.TestMoveButtonChecked);
            });
        }

        [Fact]
        public void BeginDraw_WithTextSelected_ClearsTextSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestAddTextAnnotation(TextPosition, "Note");
                ClickAtImagePixel(editor, TextClickPoint);
                Assert.Equal(1, editor.TestSelectedTextCount);

                // Draw a rect with the text still selected; the draft must not adopt the
                // text into its selection (a later drag would move it).
                editor.TestToggleRectTool();
                editor.TestFireMouseDownAtImagePixel(new Point(15, 15), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(55, 45), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(55, 45), MouseButtons.Left);

                Assert.Equal(0, editor.TestSelectedTextCount);
                Assert.Contains($"pos={TextPosition}", editor.TestDescribeTextAnnotations());
            });
        }

        [Fact]
        public void Delete_DuringStraightenMode_DoesNotDeleteSelectedText()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestAddTextAnnotation(TextPosition, "Note");
                ClickAtImagePixel(editor, TextClickPoint);
                Assert.Equal(1, editor.TestSelectedTextCount);

                editor.TestFireKeyDown(Keys.Control | Keys.L);
                Assert.True(editor.TestIsStraightenToolActive);

                // The modal tool owns the keyboard: Delete must not reach the text
                // selection sitting underneath the overlay.
                editor.TestFireKeyDown(Keys.Delete);
                Assert.Equal(1, editor.TestTextAnnotationCount);
            });
        }
    }
}
