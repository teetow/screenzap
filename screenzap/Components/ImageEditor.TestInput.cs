using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ImageEditor
    {
        // Real input simulation for tests. These methods construct real WinForms event-args and
        // fire them through the actual editor pipeline (pictureBox1_MouseDown / _MouseMove /
        // _MouseUp, ProcessCmdKey, ImageEditor_KeyDown). Tests using these helpers exercise the
        // exact code path that user input takes — no diagnostic shortcuts.

        internal void TestFireMouseDownAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, args);
        }

        internal void TestFireMouseMoveAtImagePixel(Point imagePixel, MouseButtons heldButton)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(heldButton, 0, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseMove(pictureBox1!, args);
        }

        internal void TestFireMouseUpAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, args);
        }

        internal void TestFireMouseDownAtClientPoint(Point clientPoint, MouseButtons button)
        {
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, args);
        }

        internal void TestFireMouseMoveAtClientPoint(Point clientPoint, MouseButtons heldButton)
        {
            var args = new MouseEventArgs(heldButton, 0, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseMove(pictureBox1!, args);
        }

        internal void TestFireMouseUpAtClientPoint(Point clientPoint, MouseButtons button)
        {
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, args);
        }

        internal void TestFireDoubleClickAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            // Double-click in WinForms is signalled by Clicks=2 on the second MouseDown.
            var down1 = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, down1);
            var up1 = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, up1);
            var down2 = new MouseEventArgs(button, 2, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, down2);
            // Fire the picturebox DoubleClick event, which any handler hooked to it depends on.
            pictureBox1?.GetType()
                .GetMethod("OnDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(pictureBox1, new object[] { System.EventArgs.Empty });
            var up2 = new MouseEventArgs(button, 2, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, up2);
        }

        internal bool TestFireProcessCmdKey(Keys keyData)
        {
            var msg = new Message();
            return ProcessCmdKey(ref msg, keyData);
        }

        internal void TestFireKeyDown(Keys keyData)
        {
            var args = new KeyEventArgs(keyData);
            ImageEditor_KeyDown(this, args);
        }

        internal bool TestFireKeyPress(char ch)
        {
            var args = new KeyPressEventArgs(ch);
            // Fire the protected OnKeyPress override directly via reflection (no friendlier path).
            typeof(System.Windows.Forms.Form)
                .GetMethod("OnKeyPress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(this, new object[] { args });
            return args.Handled;
        }

        internal Point TestImagePixelToClient(Point imagePixel)
        {
            return pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
        }

        internal Point TestClientToImagePixel(Point clientPoint)
        {
            return pictureBox1?.ClientToPixel(clientPoint) ?? clientPoint;
        }

        internal Bitmap TestRenderToBitmap()
        {
            // Render the entire editor form to a bitmap. Forces a full paint pass.
            if (Width <= 0 || Height <= 0)
            {
                return new Bitmap(1, 1);
            }
            var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            return bmp;
        }

        internal Bitmap TestRenderPictureBoxToBitmap()
        {
            if (pictureBox1 == null || pictureBox1.Width <= 0 || pictureBox1.Height <= 0)
            {
                return new Bitmap(1, 1);
            }
            var bmp = new Bitmap(pictureBox1.Width, pictureBox1.Height);
            pictureBox1.DrawToBitmap(bmp, new Rectangle(0, 0, pictureBox1.Width, pictureBox1.Height));
            return bmp;
        }

        internal Rectangle TestPictureBoxBoundsInForm()
        {
            if (pictureBox1 == null) return Rectangle.Empty;
            var topLeft = pictureBox1.PointToScreen(Point.Empty);
            var formTopLeft = PointToScreen(Point.Empty);
            return new Rectangle(
                topLeft.X - formTopLeft.X,
                topLeft.Y - formTopLeft.Y,
                pictureBox1.Width,
                pictureBox1.Height);
        }

        internal void TestSetSize(int width, int height)
        {
            ClientSize = new Size(width, height);
        }

        internal void TestToggleTextTool() => ToggleTextTool();

        internal bool TestIsTextToolActive => isTextToolActive;

        internal int TestTextAnnotationCount => textAnnotations.Count;

        internal void TestToggleArrowTool() => ToggleDrawingTool(DrawingTool.Arrow);

        internal void TestToggleRectTool() => ToggleDrawingTool(DrawingTool.Rectangle);

        internal void TestToggleHighlighterTool() => ToggleDrawingTool(DrawingTool.Highlighter);

        /// <summary>Clear the annotation selection without touching the active tool.</summary>
        internal void TestClearAnnotationSelection() => SelectAnnotation(null);

        /// <summary>
        /// Drive a full freehand highlighter stroke: mouse-down at the first point, a move per
        /// subsequent point, then mouse-up at the last. Mirrors a real drag so decimation /
        /// smoothing runs in CompleteAnnotationDraft. Requires the highlighter tool to be active.
        /// </summary>
        internal void TestDrawHighlighterStroke(System.Collections.Generic.IReadOnlyList<Point> imagePixels)
        {
            if (imagePixels == null || imagePixels.Count == 0)
                throw new System.ArgumentException("stroke needs at least one point");

            TestFireMouseDownAtImagePixel(imagePixels[0], MouseButtons.Left);
            for (int i = 1; i < imagePixels.Count; i++)
            {
                TestFireMouseMoveAtImagePixel(imagePixels[i], MouseButtons.Left);
            }
            TestFireMouseUpAtImagePixel(imagePixels[imagePixels.Count - 1], MouseButtons.Left);
        }

        internal void TestSetHighlighterThickness(float thickness)
        {
            if (highlighterThicknessComboBox == null)
                throw new System.InvalidOperationException("highlighterThicknessComboBox not initialized");
            int idx = highlighterThicknessComboBox.Items.IndexOf(thickness.ToString());
            if (idx < 0)
                throw new System.ArgumentException($"thickness {thickness} not in combobox items");
            highlighterThicknessComboBox.SelectedIndex = idx;
        }

        internal int TestHighlighterThicknessComboBoxSelectedIndex => highlighterThicknessComboBox?.SelectedIndex ?? -2;

        internal void TestSetHighlighterOpacityPercent(int percent)
        {
            if (highlighterOpacityTrackBar == null)
                throw new System.InvalidOperationException("highlighterOpacityTrackBar not initialized");
            int clamped = System.Math.Clamp(percent, highlighterOpacityTrackBar.Minimum, highlighterOpacityTrackBar.Maximum);
            highlighterOpacityTrackBar.Value = clamped;
        }

        internal int TestHighlighterOpacityPercent => highlighterOpacityTrackBar?.Value ?? -1;

        internal string TestHighlighterOpacityValueLabelText => highlighterOpacityValueLabel?.Text ?? string.Empty;

        internal int TestSelectedHighlighterPointCount =>
            selectedAnnotation?.Points?.Count ?? -1;

        internal void TestDeactivateDrawingTool()
        {
            if (activeDrawingTool != DrawingTool.None)
                ToggleDrawingTool(activeDrawingTool); // toggle same tool off
        }

        internal DrawingTool TestActiveDrawingTool => activeDrawingTool;

        internal int TestAnnotationShapeCount => annotationShapes.Count;

        internal AnnotationShape? TestSelectedAnnotation => selectedAnnotation;

        internal float TestAnnotationLineThickness => annotationLineThickness;

        internal Color TestAnnotationColorDefault => annotationColor;

        internal int TestSelectedShapeCount => selectedShapes.Count;
        internal int TestSelectedTextCount => selectedTexts.Count;
        internal IReadOnlyList<AnnotationShape> TestSelectedShapes => selectedShapes;
        internal IReadOnlyList<TextAnnotation> TestSelectedTexts => selectedTexts;
        internal string TestAnnotationColorButtonText => annotationColorButton?.Text ?? string.Empty;
        internal Color TestAnnotationColorButtonBackColor => annotationColorButton?.BackColor ?? Color.Empty;

        internal void TestSetShiftHeld(bool held) => isShiftHeld_TestOverride = held;

        /// <summary>
        /// Fire a click at the given image pixel with Shift held throughout the down+up,
        /// then release. Same code path as a real shift-click — the shift state is read
        /// from <see cref="IsMultiSelectModifierDown"/> during MouseDown handling.
        /// </summary>
        internal void TestShiftClickAtImagePixel(Point imagePixel)
        {
            isShiftHeld_TestOverride = true;
            try
            {
                TestFireMouseDownAtImagePixel(imagePixel, MouseButtons.Left);
                TestFireMouseUpAtImagePixel(imagePixel, MouseButtons.Left);
            }
            finally
            {
                isShiftHeld_TestOverride = false;
            }
        }

        /// <summary>Apply a color to all selected shapes + texts via the same path the color picker uses.</summary>
        internal void TestApplyColorToSelection(Color color)
        {
            annotationColor = color;
            ApplyColorToSelection(color);
            UpdateAnnotationColorButtonAppearance();
        }

        /// <summary>
        /// Apply a color to the selected annotation through the same code path the picker uses,
        /// updating both the selection and the tool default. Skips opening a real ColorDialog.
        /// </summary>
        internal void TestSetAnnotationColor(Color color)
        {
            annotationColor = color;
            if (selectedAnnotation != null)
            {
                annotationSnapshotBeforeEdit = CloneAnnotations();
                selectedAnnotation.Color = color;
                CommitAnnotationUndo();
                pictureBox1?.Invalidate();
            }
            UpdateAnnotationColorButtonAppearance();
        }

        /// <summary>
        /// Drive the line-thickness combobox the way a real user does: change SelectedIndex,
        /// letting SelectedIndexChanged fire naturally. Throws if the requested value isn't in
        /// the combobox's Items list.
        /// </summary>
        internal void TestSetAnnotationLineThickness(float thickness)
        {
            if (lineThicknessComboBox == null)
                throw new System.InvalidOperationException("lineThicknessComboBox not initialized");
            int idx = lineThicknessComboBox.Items.IndexOf(thickness.ToString());
            if (idx < 0)
                throw new System.ArgumentException($"thickness {thickness} not in combobox items");
            lineThicknessComboBox.SelectedIndex = idx;
        }

        internal void TestSetAnnotationArrowSize(float size)
        {
            if (arrowSizeComboBox == null)
                throw new System.InvalidOperationException("arrowSizeComboBox not initialized");
            int idx = arrowSizeComboBox.Items.IndexOf(ArrowSizeToComboBoxText(size));
            if (idx < 0)
                throw new System.ArgumentException($"arrow size {size} not in combobox items");
            arrowSizeComboBox.SelectedIndex = idx;
        }

        internal int TestLineThicknessComboBoxSelectedIndex => lineThicknessComboBox?.SelectedIndex ?? -2;
        internal int TestArrowSizeComboBoxSelectedIndex => arrowSizeComboBox?.SelectedIndex ?? -2;
        // .Visible reflects effective visibility (including parent visibility), so it's
        // unreliable in tests where the form isn't shown. .Available is the explicit
        // intent the editor sets — checking that lets tests verify visibility logic
        // without instantiating a real top-level form.
        internal bool TestArrowSizeComboBoxAvailable => arrowSizeComboBox?.Available ?? false;

        internal string TestDescribeAnnotationShapes()
        {
            if (annotationShapes.Count == 0) return "[]";
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < annotationShapes.Count; i++)
            {
                var a = annotationShapes[i];
                if (i > 0) sb.Append(", ");
                sb.Append($"#{i} type={a.Type} start={a.Start} end={a.End} selected={a.Selected}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        internal string TestDescribeTextAnnotations()
        {
            if (textAnnotations.Count == 0) return "[]";
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < textAnnotations.Count; i++)
            {
                var t = textAnnotations[i];
                if (i > 0) sb.Append(", ");
                sb.Append($"#{i} pos={t.Position} text='{t.Text}' selected={t.Selected} editing={t.IsEditing}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        internal void TestSetZoom(decimal zoom)
        {
            if (pictureBox1 != null) pictureBox1.ZoomLevel = zoom;
        }

        /// <summary>Add a finalized (non-editing, unselected) text annotation directly.</summary>
        internal void TestAddTextAnnotation(Point position, string text)
        {
            textAnnotations.Add(new TextAnnotation
            {
                Position = position,
                Text = text,
                FontFamily = "Segoe UI",
                FontSize = 16f
            });
        }

        internal bool TestMoveButtonChecked => moveToolStripButton?.Checked == true;

        /// <summary>Fire the Move/Select rail button's Click handler (the real toolbar path).</summary>
        internal void TestClickMoveToolButton() => moveToolStripButton_Click(this, System.EventArgs.Empty);

        internal bool TestIsStraightenToolActive => isStraightenToolActive;

        internal Point? TestStraightenLineStart => straightenLineStartPixel;

        internal Point? TestStraightenLineEnd => straightenLineEndPixel;

        internal bool TestStraightenButtonChecked => straightenToolStripButton?.Checked == true;

        /// <summary>Fire the straighten rail button's Click handler (the real toolbar path).</summary>
        internal void TestClickStraightenToolButton() => straightenToolStripButton_Click(this, System.EventArgs.Empty);

        internal bool TestIsCensorToolActive => isCensorToolActive;

        internal bool TestCensorButtonChecked => censorToolStripButton?.Checked == true;

        internal int TestCensorRegionCount => censorRegions.Count;

        internal int TestSelectedCensorRegionCount => censorRegions.Count(r => r.Selected);

        /// <summary>
        /// Enter censor mode with pre-seeded regions, skipping OCR detection and preview
        /// rendering. Mirrors the state ActivateCensorTool leaves behind so keyboard and
        /// toolbar behavior can be tested without Tesseract.
        /// </summary>
        internal void TestEnterCensorModeWithRegions(params Rectangle[] regionBounds)
        {
            censorRegions.Clear();
            foreach (var bounds in regionBounds)
            {
                censorRegions.Add(new CensorRegion(bounds, 1f));
            }

            isCensorToolActive = true;
            if (censorToolStripButton != null)
            {
                censorToolStripButton.Checked = true;
            }
            if (censorToolStrip != null)
            {
                censorToolStrip.Visible = true;
                PositionOverlayToolStrips();
            }
            UpdateCensorToolbarState();
            pictureBox1?.Invalidate();
        }

        internal float TestGetLayerRotationDeg(int index) => imageLayers[index].RotationDeg;

        internal void TestSetLayerRotationDeg(int index, float deg) => imageLayers[index].RotationDeg = deg;

        internal ImageLayerHandle TestActiveLayerHandle => activeLayerHandle;

        internal string TestDescribeUndoStack()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"canUndo={undoStack.CanUndo} canRedo={undoStack.CanRedo}");
            return sb.ToString();
        }

        internal string TestDescribeState()
        {
            var pb = pictureBox1;
            var imgInfo = pb?.Image == null ? "null" : $"{pb.Image.Width}x{pb.Image.Height}";
            var pbBounds = pb == null ? "null" : $"{pb.Width}x{pb.Height} visible={pb.Visible}";
            var formInfo = $"{Width}x{Height} visible={Visible} created={IsHandleCreated}";
            var zoom = pb == null ? "n/a" : $"zoom={pb.ZoomLevel} pan={pb.Metrics.PanOffset}";
            var layerInfo = "[]";
            if (imageLayers.Count > 0)
            {
                var sb = new System.Text.StringBuilder("[");
                for (int i = 0; i < imageLayers.Count; i++)
                {
                    var l = imageLayers[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append($"#{i} src={l.Source.Width}x{l.Source.Height} frame={l.Frame} fill={l.Fill}");
                }
                sb.Append("]");
                layerInfo = sb.ToString();
            }
            return $"form={formInfo} picturebox={pbBounds} image={imgInfo} {zoom} layers={imageLayers.Count} selected={selectedLayerIndex} {layerInfo}";
        }
    }
}
