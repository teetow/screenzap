using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Keyboard positioning/sizing of annotation objects, the Shift-square rectangle draft, and
    /// the arrow-key ownership split between text EDITING (caret) and text OBJECT selection
    /// (nudge). Everything is driven through the real input pipeline — ProcessCmdKey for arrows
    /// (they never reach KeyDown), pictureBox1_Mouse* for gestures.
    /// </summary>
    public class ShapeKeyboardTransformTests
    {
        private static screenzap.ImageEditor NewEditor(int width = 200, int height = 160)
        {
            var editor = new screenzap.ImageEditor();
            editor.CreateControl();
            using var canvas = new Bitmap(width, height);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
            }
            editor.LoadImage(canvas);
            // Pin the modifiers down (up, rather) for the whole class. Otherwise every
            // assertion about no-modifier behaviour quietly reads the keyboard of whoever is
            // running the suite, and fails if they happen to be holding Shift.
            editor.TestSetShiftHeld(false);
            editor.TestSetCtrlHeld(false);
            editor.TestSetAltHeld(false);
            return editor;
        }

        /// <summary>Draw a rectangle from start to end with the rect tool, then drop to Move mode.</summary>
        private static screenzap.AnnotationShape DrawRectangle(
            screenzap.ImageEditor editor, Point start, Point end, bool shiftHeld = false)
        {
            editor.TestToggleRectTool();
            editor.TestSetShiftHeld(shiftHeld);
            try
            {
                editor.TestFireMouseDownAtImagePixel(start, MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(end, MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(end, MouseButtons.Left);
            }
            finally
            {
                editor.TestSetShiftHeld(false);
            }

            editor.TestDeactivateDrawingTool();
            var shape = editor.TestSelectedAnnotation;
            Assert.NotNull(shape);
            return shape!;
        }

        // ── Shift-drag constrains a rectangle to a square ────────────────────────────

        [Fact]
        public void ShiftDragRectangle_StaysSquareAfterMouseUp()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();

                // Wide, shallow drag: the square must take the LONGER side (60), not the cursor.
                var shape = DrawRectangle(editor, new Point(20, 20), new Point(80, 50), shiftHeld: true);

                Assert.Equal(new Point(20, 20), shape.Start);
                Assert.Equal(new Point(80, 80), shape.End);
            });
        }

        [Fact]
        public void ShiftDragRectangle_UpAndLeft_StaysSquare()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();

                // Drag toward the origin: the constrained corner mirrors on both axes.
                var shape = DrawRectangle(editor, new Point(100, 100), new Point(60, 90), shiftHeld: true);

                // Normalized, so Start is the top-left of the 40x40 square.
                Assert.Equal(new Point(60, 60), shape.Start);
                Assert.Equal(new Point(100, 100), shape.End);
            });
        }

        [Fact]
        public void ShiftDragRectangle_PastCanvasEdge_ShrinksInsteadOfShearing()
        {
            StaTest.Run(() =>
            {
                // 200x160 canvas: a square anchored at (20,20) has 180px of room to the right
                // but only 140 downward, so the side length is capped at 140 — clamping the
                // corner per-axis would have left a 180x140 rectangle.
                using var editor = NewEditor();

                var shape = DrawRectangle(editor, new Point(20, 20), new Point(400, 400), shiftHeld: true);

                Assert.Equal(new Point(20, 20), shape.Start);
                Assert.Equal(new Point(160, 160), shape.End);
            });
        }

        [Fact]
        public void DragRectangle_WithoutShift_KeepsFreeAspect()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();

                var shape = DrawRectangle(editor, new Point(20, 20), new Point(80, 50));

                Assert.Equal(new Point(20, 20), shape.Start);
                Assert.Equal(new Point(80, 50), shape.End);
            });
        }

        // ── Arrow keys position the selected object ──────────────────────────────────

        [Fact]
        public void ArrowKeys_MoveSelectedRectangle_OnePixelPerPress()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                var shape = DrawRectangle(editor, new Point(20, 20), new Point(80, 50));

                Assert.True(editor.TestFireProcessCmdKey(Keys.Right));
                Assert.True(editor.TestFireProcessCmdKey(Keys.Down));
                Assert.True(editor.TestFireProcessCmdKey(Keys.Down));

                Assert.Equal(new Point(21, 22), shape.Start);
                Assert.Equal(new Point(81, 52), shape.End);
            });
        }

        [Fact]
        public void ShiftArrow_MovesSelectedRectangle_TenPixels()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                var shape = DrawRectangle(editor, new Point(20, 20), new Point(80, 50));

                Assert.True(editor.TestFireProcessCmdKey(Keys.Shift | Keys.Left));

                Assert.Equal(new Point(10, 20), shape.Start);
                Assert.Equal(new Point(70, 50), shape.End);
            });
        }

        [Fact]
        public void ArrowKeys_ClampSelectedShapeInsideCanvas()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                var shape = DrawRectangle(editor, new Point(0, 20), new Point(60, 50));

                // Already flush against the left edge — the press is claimed but moves nothing.
                Assert.True(editor.TestFireProcessCmdKey(Keys.Left));

                Assert.Equal(new Point(0, 20), shape.Start);
                Assert.Equal(new Point(60, 50), shape.End);
            });
        }

        [Fact]
        public void ArrowKeys_MoveSelectedTextAnnotation()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");

                // Select it as an object (Move mode — the text tool stays off).
                editor.TestFireMouseDownAtImagePixel(new Point(34, 36), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(34, 36), MouseButtons.Left);
                Assert.Equal(1, editor.TestSelectedTextCount);
                Assert.False(editor.TestIsTextToolActive);

                Assert.True(editor.TestFireProcessCmdKey(Keys.Right));
                Assert.True(editor.TestFireProcessCmdKey(Keys.Shift | Keys.Down));

                Assert.Equal(new Point(31, 40), editor.TestTextAnnotations[0].Position);
            });
        }

        [Fact]
        public void ArrowKeys_MoveWholeMultiSelectionTogether()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                var shape = DrawRectangle(editor, new Point(90, 90), new Point(140, 130));

                // Shift-click the text into the shape's selection.
                editor.TestShiftClickAtImagePixel(new Point(34, 36));
                Assert.Equal(1, editor.TestSelectedShapeCount);
                Assert.Equal(1, editor.TestSelectedTextCount);

                Assert.True(editor.TestFireProcessCmdKey(Keys.Right));

                Assert.Equal(new Point(91, 90), shape.Start);
                Assert.Equal(new Point(31, 30), editor.TestTextAnnotations[0].Position);
            });
        }

        // ── Ctrl+Arrow sizes the selected shape ─────────────────────────────────────

        [Fact]
        public void CtrlArrow_ResizesSelectedRectangleFromFarCorner()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                var shape = DrawRectangle(editor, new Point(20, 20), new Point(80, 50));

                Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Right));
                Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Shift | Keys.Down));

                // Top-left anchored, bottom-right moved: +1 wide, +10 tall.
                Assert.Equal(new Point(20, 20), shape.Start);
                Assert.Equal(new Point(81, 60), shape.End);
            });
        }

        [Fact]
        public void CtrlArrow_ShrinkingRectangle_StopsAtMinimumSize()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                var shape = DrawRectangle(editor, new Point(20, 20), new Point(24, 50));

                for (int i = 0; i < 10; i++)
                {
                    Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Left));
                }

                // Never collapses past the 2px floor that keeps AnnotationShape.IsValid() true.
                Assert.Equal(new Point(22, 50), shape.End);
            });
        }

        [Fact]
        public void CtrlArrow_ResizesSelectedArrowEndpoint()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestToggleArrowTool();
                editor.TestFireMouseDownAtImagePixel(new Point(20, 20), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(60, 50), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(60, 50), MouseButtons.Left);
                editor.TestDeactivateDrawingTool();

                var shape = editor.TestSelectedAnnotation;
                Assert.NotNull(shape);

                Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Shift | Keys.Right));

                Assert.Equal(new Point(20, 20), shape!.Start);
                Assert.Equal(new Point(70, 50), shape.End);
            });
        }

        [Fact]
        public void CtrlArrow_ScalesSelectedHighlighterStroke()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestToggleHighlighterTool();
                editor.TestDrawHighlighterStroke(new[]
                {
                    new Point(20, 40), new Point(40, 60), new Point(60, 40), new Point(80, 60)
                });
                editor.TestDeactivateDrawingTool();

                var shape = editor.TestSelectedAnnotation;
                Assert.NotNull(shape);
                var before = shape!.GetBounds();

                Assert.True(editor.TestFireProcessCmdKey(Keys.Control | Keys.Shift | Keys.Right));

                var after = shape.GetBounds();
                // Top-left anchored, box 10px wider, height untouched — the stroke is scaled,
                // not translated.
                Assert.Equal(before.Location, after.Location);
                Assert.Equal(before.Width + 10, after.Width);
                Assert.Equal(before.Height, after.Height);
                Assert.Equal(shape.Points!.Count, editor.TestSelectedHighlighterPointCount);
            });
        }

        // ── Shift squares an existing rect through its corner handles ───────────────

        /// <summary>Grab a corner handle in Move mode and drag it, optionally with Shift held.</summary>
        private static void DragCornerHandle(
            screenzap.ImageEditor editor, Point handlePixel, Point toPixel, bool shiftHeld)
        {
            editor.TestFireMouseDownAtImagePixel(handlePixel, MouseButtons.Left);
            editor.TestSetShiftHeld(shiftHeld);
            try
            {
                editor.TestFireMouseMoveAtImagePixel(toPixel, MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(toPixel, MouseButtons.Left);
            }
            finally
            {
                editor.TestSetShiftHeld(false);
            }
        }

        [Fact]
        public void ShiftDragCornerHandle_OnExistingRect_ForcesSquare()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                var shape = DrawRectangle(editor, new Point(20, 20), new Point(80, 50));

                // Re-select it the way a user would, then Shift-drag the bottom-right corner.
                editor.TestFireMouseDownAtImagePixel(new Point(50, 35), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(50, 35), MouseButtons.Left);
                Assert.Equal(1, editor.TestSelectedShapeCount);

                DragCornerHandle(editor, new Point(80, 50), new Point(120, 70), shiftHeld: true);

                // Anchored at the untouched top-left, sized by the longer axis (100 vs 50).
                Assert.Equal(new Point(20, 20), shape.Start);
                Assert.Equal(new Point(120, 120), shape.End);
            });
        }

        [Fact]
        public void ShiftDragTopLeftHandle_SquaresAgainstTheOppositeCorner()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                var shape = DrawRectangle(editor, new Point(40, 40), new Point(100, 100));

                editor.TestFireMouseDownAtImagePixel(new Point(70, 70), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(70, 70), MouseButtons.Left);

                DragCornerHandle(editor, new Point(40, 40), new Point(20, 55), shiftHeld: true);

                // Bottom-right (100,100) is the anchor; the longer axis is 80, so the corner
                // lands at (20,20) — not the (20,55) the cursor asked for.
                Assert.Equal(new Point(20, 20), shape.Start);
                Assert.Equal(new Point(100, 100), shape.End);
            });
        }

        [Fact]
        public void DragCornerHandle_WithoutShift_KeepsFreeAspect()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                var shape = DrawRectangle(editor, new Point(20, 20), new Point(80, 50));

                editor.TestFireMouseDownAtImagePixel(new Point(50, 35), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(50, 35), MouseButtons.Left);

                DragCornerHandle(editor, new Point(80, 50), new Point(120, 70), shiftHeld: false);

                Assert.Equal(new Point(20, 20), shape.Start);
                Assert.Equal(new Point(120, 70), shape.End);
            });
        }

        [Fact]
        public void DragCornerHandle_PastTheAnchor_FlipsInsteadOfCollapsing()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                var shape = DrawRectangle(editor, new Point(60, 60), new Point(120, 100));

                editor.TestFireMouseDownAtImagePixel(new Point(90, 80), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(90, 80), MouseButtons.Left);

                // Drag the bottom-right corner up past the top-left anchor in two steps: the
                // rect must mirror around (60,60), not shrink toward the crossing point.
                editor.TestFireMouseDownAtImagePixel(new Point(120, 100), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(50, 50), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(20, 20), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(20, 20), MouseButtons.Left);

                Assert.Equal(new Point(20, 20), shape.Start);
                Assert.Equal(new Point(60, 60), shape.End);
            });
        }

        // ── Escape ladder: leaving a mode outranks dropping the selection ───────────

        [Fact]
        public void EscapeFromTextEditing_LeavesEditing_ThenPutsTheToolAway()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                editor.TestFireDoubleClickAtImagePixel(new Point(34, 36), MouseButtons.Left);
                Assert.True(editor.TestTextAnnotations[0].IsEditing);
                Assert.True(editor.TestIsTextToolActive);

                // Esc #1: leave text editing, stay in the tool with the block selected.
                editor.TestFireKeyDown(Keys.Escape);
                Assert.False(editor.TestTextAnnotations[0].IsEditing);
                Assert.True(editor.TestIsTextToolActive);

                // Esc #2: put the text tool away — no press wasted on deselecting first.
                editor.TestFireKeyDown(Keys.Escape);
                Assert.False(editor.TestIsTextToolActive);
                Assert.True(editor.TestMoveButtonChecked);

                // Esc #3: now the selection goes.
                editor.TestFireKeyDown(Keys.Escape);
                Assert.Equal(0, editor.TestSelectedTextCount);
            });
        }

        // ── Engaging a tool drops the previous act's selection ──────────────────────

        [Fact]
        public void ActivatingTextTool_DeselectsSelectedRectangle()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                DrawRectangle(editor, new Point(20, 20), new Point(80, 50));
                Assert.Equal(1, editor.TestSelectedShapeCount);

                editor.TestToggleTextTool();

                Assert.Equal(0, editor.TestSelectedShapeCount);
                Assert.Null(editor.TestSelectedAnnotation);
            });
        }

        [Fact]
        public void ActivatingDrawingTool_DeselectsSelectedText()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                editor.TestFireMouseDownAtImagePixel(new Point(34, 36), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(34, 36), MouseButtons.Left);
                Assert.Equal(1, editor.TestSelectedTextCount);

                editor.TestToggleArrowTool();

                Assert.Equal(0, editor.TestSelectedTextCount);
            });
        }

        [Fact]
        public void SwitchingToMoveMode_KeepsTheSelection()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestToggleRectTool();
                editor.TestFireMouseDownAtImagePixel(new Point(20, 20), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(80, 50), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(80, 50), MouseButtons.Left);
                Assert.Equal(1, editor.TestSelectedShapeCount);

                // Move/Select is where you handle what is already selected — it must not
                // throw the fresh shape away on the way in.
                editor.TestClickMoveToolButton();

                Assert.Equal(1, editor.TestSelectedShapeCount);
            });
        }

        [Fact]
        public void EnterToEditSelectedText_SurvivesTheImplicitToolSwitch()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                editor.TestFireMouseDownAtImagePixel(new Point(34, 36), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(34, 36), MouseButtons.Left);
                Assert.False(editor.TestIsTextToolActive);

                // Enter promotes to editing AND engages the tool. The promotion is about the
                // block already picked, so the tool switch must not deselect it out from under.
                editor.TestFireKeyDown(Keys.Enter);

                Assert.True(editor.TestIsTextToolActive);
                Assert.True(editor.TestTextAnnotations[0].IsEditing);
                Assert.Equal(1, editor.TestSelectedTextCount);
                Assert.True(editor.TestFireKeyPress('!'));
                Assert.Equal("Hello!", editor.TestTextAnnotations[0].Text);
            });
        }

        // ── A freshly drawn shape reads as selected ─────────────────────────────────

        [Fact]
        public void FreshlyDrawnShape_ShowsSelectionOutline_WhileToolStaysArmed()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestSetSize(320, 260);

                editor.TestToggleArrowTool();
                editor.TestFireMouseDownAtImagePixel(new Point(30, 30), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(120, 90), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(120, 90), MouseButtons.Left);

                // Still armed, and the arrow is genuinely selected (arrow keys move it).
                Assert.Equal(screenzap.DrawingTool.Arrow, editor.TestActiveDrawingTool);
                Assert.Equal(1, editor.TestSelectedShapeCount);

                using var painted = editor.TestRenderPictureBoxToBitmap();
                Assert.True(
                    ContainsSelectionOutlineBlue(painted),
                    "a selected shape drew no selection chrome, so it looked dropped");
            });
        }

        /// <summary>
        /// Look for the dashed DodgerBlue selection outline. The shapes themselves are drawn in
        /// the annotation colour (red by default), so blue-dominant pixels can only be chrome.
        /// </summary>
        private static bool ContainsSelectionOutlineBlue(Bitmap bitmap)
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    if (c.B > 120 && c.B > c.R + 40 && c.B > c.G + 20)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ── Undo folds a held key into one step ─────────────────────────────────────

        [Fact]
        public void ArrowKeyNudges_CoalesceIntoOneUndoStepPerKeyPress()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                DrawRectangle(editor, new Point(20, 20), new Point(80, 50));

                // Auto-repeat: three KeyDowns, one KeyUp.
                editor.TestFireProcessCmdKey(Keys.Right);
                editor.TestFireProcessCmdKey(Keys.Right);
                editor.TestFireProcessCmdKey(Keys.Right);
                editor.TestFireKeyUp(Keys.Right);

                Assert.Equal(new Point(23, 20), editor.TestAnnotationShapes[0].Start);

                editor.TestFireKeyDown(Keys.Control | Keys.Z);

                // One Ctrl+Z takes all three presses back to where the draw left the shape.
                Assert.Equal(new Point(20, 20), editor.TestAnnotationShapes[0].Start);
            });
        }

        [Fact]
        public void ArrowKeyNudge_ThatMovesNothing_PushesNoUndoStep()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                DrawRectangle(editor, new Point(0, 20), new Point(60, 50));

                // Flush against the left edge: nothing moves, so nothing should be undoable
                // beyond the draw itself.
                editor.TestFireProcessCmdKey(Keys.Left);
                editor.TestFireKeyUp(Keys.Left);

                editor.TestFireKeyDown(Keys.Control | Keys.Z);

                // The single Ctrl+Z undid the DRAW, not an empty nudge step.
                Assert.Equal(0, editor.TestAnnotationShapeCount);
            });
        }

        // ── Text editing owns the arrows; text selection does not ───────────────────

        [Fact]
        public void DoubleClickTextObject_InMoveMode_EntersEditModeAndEngagesTool()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                Assert.False(editor.TestIsTextToolActive);

                editor.TestFireDoubleClickAtImagePixel(new Point(34, 36), MouseButtons.Left);

                Assert.True(editor.TestTextAnnotations[0].IsEditing);
                Assert.True(editor.TestIsTextToolActive);

                // The whole point of engaging the tool: typing now reaches the annotation.
                Assert.True(editor.TestFireKeyPress('!'));
                Assert.Equal("Hello!", editor.TestTextAnnotations[0].Text);
            });
        }

        [Fact]
        public void ArrowKeys_DoNotMoveTextBlockWhileEditingIt()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                editor.TestFireDoubleClickAtImagePixel(new Point(34, 36), MouseButtons.Left);
                Assert.True(editor.TestTextAnnotations[0].IsEditing);

                // Every arrow is claimed by the editor (so none escapes to focus navigation)
                // and none of them relocates the box being typed into.
                foreach (var key in new[] { Keys.Left, Keys.Right, Keys.Up, Keys.Down })
                {
                    Assert.True(editor.TestFireProcessCmdKey(key), $"{key} leaked out of text editing");
                }

                Assert.Equal(new Point(30, 30), editor.TestTextAnnotations[0].Position);
            });
        }

        [Fact]
        public void LeftRightArrows_StillMoveTheCaretWhileEditing()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                editor.TestFireDoubleClickAtImagePixel(new Point(34, 36), MouseButtons.Left);

                // Caret starts at the end; two Lefts put it between the 'l's.
                editor.TestFireProcessCmdKey(Keys.Left);
                editor.TestFireProcessCmdKey(Keys.Left);
                Assert.True(editor.TestFireKeyPress('X'));

                Assert.Equal("HelXlo", editor.TestTextAnnotations[0].Text);
            });
        }

        [Fact]
        public void ArrowKeys_MoveTextBlock_AfterEditingEndsAndItIsMerelySelected()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor();
                editor.TestAddTextAnnotation(new Point(30, 30), "Hello");
                editor.TestFireDoubleClickAtImagePixel(new Point(34, 36), MouseButtons.Left);
                Assert.True(editor.TestTextAnnotations[0].IsEditing);

                // Enter confirms the edit and drops back to object-selection mode.
                editor.TestFireKeyDown(Keys.Enter);
                Assert.False(editor.TestTextAnnotations[0].IsEditing);
                Assert.Equal(1, editor.TestSelectedTextCount);

                Assert.True(editor.TestFireProcessCmdKey(Keys.Right));
                Assert.Equal(new Point(31, 30), editor.TestTextAnnotations[0].Position);
            });
        }

        // ── The marquee keeps the arrows when no object is selected ─────────────────

        [Fact]
        public void ArrowKeys_StillMoveTheMarquee_WhenNoAnnotationIsSelected()
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
