using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ImageDocumentEditor
    {
        // Drive the document's pointer and keyboard handlers with deterministic input data.
        internal void TestFireMouseDownAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = viewport?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            ViewportMouseDown(viewport!, args);
        }

        internal void TestFireMouseMoveAtImagePixel(Point imagePixel, MouseButtons heldButton)
        {
            var clientPoint = viewport?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(heldButton, 0, clientPoint.X, clientPoint.Y, 0);
            ViewportMouseMove(viewport!, args);
        }

        internal void TestFireMouseUpAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = viewport?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            ViewportMouseUp(viewport!, args);
        }

        internal void TestFireMouseDownAtClientPoint(Point clientPoint, MouseButtons button)
        {
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            ViewportMouseDown(viewport!, args);
        }

        internal void TestFireMouseMoveAtClientPoint(Point clientPoint, MouseButtons heldButton)
        {
            var args = new MouseEventArgs(heldButton, 0, clientPoint.X, clientPoint.Y, 0);
            ViewportMouseMove(viewport!, args);
        }

        internal void TestFireMouseUpAtClientPoint(Point clientPoint, MouseButtons button)
        {
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            ViewportMouseUp(viewport!, args);
        }

        internal void TestFireDoubleClickAtImagePixel(Point pixel, MouseButtons button)
        {
            var point = viewport.PixelToClient(pixel);
            SurfacePointer(0, point, button);
            SurfacePointer(2, point, button);
            SurfacePointer(0, point, button, 2);
            SurfacePointer(3, point, button, 2);
            SurfacePointer(2, point, button, 2);
        }

        // Seams for the text-tool handlers that report whether they CLAIMED the input. The
        // public pipeline swallows that answer, which is why these were being reached through
        // reflection — a rename would have broken those tests silently at runtime instead of
        // at compile time.
        internal bool TestHandleTextToolMouseDown(Point imagePixel) => HandleTextToolMouseDown(imagePixel, ImageToViewport(imagePixel));
        internal bool TestHandleTextToolKeyDown(Keys keyData) => HandleTextToolKeyDown(new KeyEventArgs(keyData));
        internal bool TestHandleTextToolKeyPress(char ch) => HandleTextToolKeyPress(new KeyPressEventArgs(ch));
        internal void TestSuspendTextEditingForUiFocus() => SuspendTextEditingForUiFocus();
        internal void TestResumeSelectedTextEditing() => ResumeSelectedTextEditing();
        internal bool TestFireProcessCmdKey(Keys keyData)
        {
            return HandleNavigationKey(keyData);
        }

        internal void TestFireKeyDown(Keys keyData)
        {
            var args = new KeyEventArgs(keyData);
            HandleKeyDown(this, args);
        }

        /// <summary>
        /// Fire a KeyDown and report whether the editor swallowed the keystroke. Windows only
        /// raises the follow-up WM_CHAR (and so OnKeyPress) when KeyDown left SuppressKeyPress
        /// clear, so a suppressed key is a key that can never be typed.
        /// </summary>
        internal bool TestFireKeyDownSuppressed(Keys keyData)
        {
            var args = new KeyEventArgs(keyData);
            HandleKeyDown(this, args);
            return args.SuppressKeyPress;
        }

        private MouseButtons? mouseButtons_TestOverride;
        internal void TestSetMouseButtonsHeld(MouseButtons buttons) => mouseButtons_TestOverride = buttons;
        internal void TestFireKeyUp(Keys keyData)
        {
            var args = new KeyEventArgs(keyData);
            HandleKeyUp(this, args);
        }

        internal bool TestFireKeyPress(char ch) => HandleTextToolKeyPress(new KeyPressEventArgs(ch));
        internal Point TestImagePixelToClient(Point imagePixel)
        {
            return viewport?.PixelToClient(imagePixel) ?? imagePixel;
        }

        internal Point TestClientToImagePixel(Point clientPoint)
        {
            return viewport?.ClientToPixel(clientPoint) ?? clientPoint;
        }

        internal Bitmap TestRenderToBitmap() => TestRenderPictureBoxToBitmap();
        internal Bitmap TestRenderPictureBoxToBitmap()
        {
            var bitmap = new Bitmap(viewport.Width, viewport.Height);
            using var graphics = Graphics.FromImage(bitmap);
            RenderSurface(graphics);
            return bitmap;
        }

        internal Rectangle TestPictureBoxBoundsInForm() => viewport.ClientRectangle;
        internal void TestSetSize(int width, int height) => ResizeSurface(new Size(width, height));
        internal void TestToggleTextTool() => ToggleTextTool();
        internal bool TestIsTextToolActive => isTextToolActive;
        internal int TestTextAnnotationCount => textAnnotations.Count;

        internal void TestSetEmojiRecentStore(EmojiRecentStore store) => emojiRecentStore = store;
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

        internal void TestSetHighlighterThickness(float thickness) => SurfaceStyle("Width", thickness);
        internal void TestSetHighlighterOpacityPercent(int percent) => SurfaceStyle("Opacity", percent / 100f);
        internal int TestHighlighterOpacityPercent => (int)System.Math.Round(annotationHighlighterOpacity * 100);
        internal int TestSelectedHighlighterPointCount => selectedAnnotation?.Points?.Count ?? -1;

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
        // The live lists, not the selection: undo rebuilds both from cloned snapshots, so a
        // test holding a reference from before an undo would be inspecting an orphan.
        internal IReadOnlyList<AnnotationShape> TestAnnotationShapes => annotationShapes;
        internal IReadOnlyList<TextAnnotation> TestTextAnnotations => textAnnotations;
        internal IReadOnlyList<TextAnnotation> TestSelectedTexts => selectedTexts;

        // These PIN the modifier for the rest of the editor's life — passing false is an
        // assertion that Shift is up, not a hand-back to the real keyboard. Tests that need
        // the no-modifier path must say so, otherwise they read whatever the person running
        // them happens to be holding.
        internal void TestSetShiftHeld(bool held) => isShiftHeld_TestOverride = held;
        internal void TestSetCtrlHeld(bool held) => isCtrlHeld_TestOverride = held;
        internal void TestSetAltHeld(bool held) => isAltHeld_TestOverride = held;
        /// <summary>
        /// Fire a click at the given image pixel with Shift held throughout the down+up,
        /// then release. Same code path as a real shift-click — the shift state is read
        /// from <see cref = "IsMultiSelectModifierDown"/> during MouseDown handling.
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
                viewport?.Invalidate();
            }
        }

        internal void TestSetAnnotationLineThickness(float thickness) => SurfaceStyle("Width", thickness);
        internal void TestSetAnnotationArrowSize(float size) => SurfaceStyle("Arrow", (decimal)size);
        internal string TestDescribeAnnotationShapes()
        {
            if (annotationShapes.Count == 0)
                return "[]";
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < annotationShapes.Count; i++)
            {
                var a = annotationShapes[i];
                if (i > 0)
                    sb.Append(", ");
                sb.Append($"#{i} type={a.Type} start={a.Start} end={a.End} selected={a.Selected}");
            }

            sb.Append("]");
            return sb.ToString();
        }

        internal string TestDescribeTextAnnotations()
        {
            if (textAnnotations.Count == 0)
                return "[]";
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < textAnnotations.Count; i++)
            {
                var t = textAnnotations[i];
                if (i > 0)
                    sb.Append(", ");
                sb.Append($"#{i} pos={t.Position} text='{t.Text}' selected={t.Selected} editing={t.IsEditing}");
            }

            sb.Append("]");
            return sb.ToString();
        }

        internal void TestSetZoom(decimal zoom)
        {
            if (viewport != null)
                viewport.ZoomLevel = zoom;
        }

        internal Components.Shared.ViewportMetrics TestViewportMetrics => viewport?.Metrics ?? default;

        /// <summary>Pan the viewport the way a middle-drag does, so tests can dirty the view.</summary>
        internal void TestPanViewportBy(Size delta) => viewport?.PanBy(delta);
        internal bool TestAlphaViewEnabled => viewport?.AlphaViewEnabled ?? true;

        /// <summary>
        /// Activate free-rotate, set an exact angle (bypassing the imprecise handle drag), and
        /// apply — so tests can pin the committed rotation direction against the preview.
        /// </summary>
        internal void TestApplyFreeRotateAngle(float angleDeg)
        {
            if (!isFreeRotateToolActive)
            {
                ActivateFreeRotateTool();
            }

            freeRotateAngleDeg = angleDeg;
            DeactivateFreeRotateTool(apply: true);
        }

        /// <summary>Add a caller-built text annotation directly, for suites that need specific
        /// font/colour/outline settings on it.</summary>
        internal void TestAddTextAnnotation(TextAnnotation annotation) => textAnnotations.Add(annotation);
        /// <summary>Add a finalized (non-editing, unselected) text annotation directly.</summary>
        internal void TestAddTextAnnotation(Point position, string text)
        {
            textAnnotations.Add(new TextAnnotation { Position = position, Text = text, FontFamily = "Segoe UI", FontSize = 16f });
        }

        internal bool TestMoveButtonChecked => CurrentTool == ActiveTool.None;

        internal void TestClickMoveToolButton() => SetActiveTool(ActiveTool.None);
        internal bool TestIsStraightenToolActive => isStraightenToolActive;
        internal Point[]? TestStraightenCorners => straightenCorners?.ToArray();
        internal bool TestStraightenApplyEnabled => lib.ImageStraightener.IsValidPerspectiveQuad(straightenCorners);
        internal bool TestStraightenButtonChecked => isStraightenToolActive;

        internal void TestClickStraightenToolButton()
        {
            if (isStraightenToolActive)
                DeactivateStraightenTool(false);
            else
                ActivateStraightenTool();
        }

        internal bool TestIsCensorToolActive => isCensorToolActive;
        internal bool TestCensorButtonChecked => isCensorToolActive;
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
            viewport?.Invalidate();
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
            var pb = viewport;
            var imgInfo = pb?.Image == null ? "null" : $"{pb.Image.Width}x{pb.Image.Height}";
            var pbBounds = pb == null ? "null" : $"{pb.Width}x{pb.Height}";
            var formInfo = $"disposed={IsDisposed}";
            var zoom = pb == null ? "n/a" : $"zoom={pb.ZoomLevel} pan={pb.Metrics.PanOffset}";
            var layerInfo = "[]";
            if (imageLayers.Count > 0)
            {
                var sb = new System.Text.StringBuilder("[");
                for (int i = 0; i < imageLayers.Count; i++)
                {
                    var l = imageLayers[i];
                    if (i > 0)
                        sb.Append(", ");
                    sb.Append($"#{i} src={l.Source.Width}x{l.Source.Height} frame={l.Frame} fill={l.Fill}");
                }

                sb.Append("]");
                layerInfo = sb.ToString();
            }

            return $"form={formInfo} picturebox={pbBounds} image={imgInfo} {zoom} layers={imageLayers.Count} selected={selectedLayerIndex} {layerInfo}";
        }
    }
}
