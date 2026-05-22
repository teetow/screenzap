using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using screenzap.lib;

namespace screenzap
{
    internal enum DrawingTool
    {
        None,
        Arrow,
        Rectangle
    }

    internal enum AnnotationHandle
    {
        None,
        Move,
        ArrowStart,
        ArrowEnd,
        RectTopLeft,
        RectTopRight,
        RectBottomLeft,
        RectBottomRight
    }

    internal enum AnnotationType
    {
        Arrow,
        Rectangle
    }

    internal sealed class AnnotationShape
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public AnnotationType Type { get; init; }
        public Point Start { get; set; }
        public Point End { get; set; }
        public float LineThickness { get; set; } = 2f;
        public float ArrowSize { get; set; } = 1f; // Multiplier for arrow head size
        public Color Color { get; set; } = Color.Red;
        public bool Selected { get; set; }

        public AnnotationShape Clone()
        {
            return new AnnotationShape
            {
                Id = Id,
                Type = Type,
                Start = Start,
                End = End,
                LineThickness = LineThickness,
                ArrowSize = ArrowSize,
                Color = Color,
                Selected = Selected
            };
        }

        public Rectangle GetBounds()
        {
            return RectangleExt.fromPoints(Start, End);
        }

        public bool IsValid()
        {
            if (Type == AnnotationType.Arrow)
            {
                return !Start.Equals(End);
            }

            var bounds = GetBounds();
            return bounds.Width > 1 && bounds.Height > 1;
        }
    }

    public partial class ImageEditor
    {
        private readonly List<AnnotationShape> annotationShapes = new List<AnnotationShape>();
        private bool isDrawingAnnotation;
        private AnnotationShape? workingAnnotation;
        // Multi-selection: selectedShapes is the source of truth, selectedAnnotation mirrors
        // selectedShapes.LastOrDefault() to keep the ~30 existing single-target read sites
        // working without churn. All writes go through SelectAnnotation / SyncSelectedAnnotation.
        private readonly List<AnnotationShape> selectedShapes = new List<AnnotationShape>();
        private AnnotationShape? selectedAnnotation;
        private AnnotationShape? hoveredAnnotation;
        private AnnotationHandle activeAnnotationHandle = AnnotationHandle.None;
        private Point annotationDragOriginPixel;
        private Point annotationDraftAnchorPixel;
        private bool annotationTranslateModeActive;
        private Point annotationTranslationOriginPixel;
        private Point annotationTranslationStartSnapshot;
        private Point annotationTranslationEndSnapshot;
        private Point annotationTranslationAnchorSnapshot;
        private List<AnnotationShape>? annotationSnapshotBeforeEdit;
        private bool annotationChangedDuringDrag;

        // Annotation tool settings
        private float annotationLineThickness = 2f;
        private float annotationArrowSize = 1f;
        private Color annotationColor = Color.Red;

        // Tests can't manipulate real keyboard state and Control.ModifierKeys is a static
        // property tied to OS input. Set via TestSetShiftHeld to simulate Shift-during-click.
        private bool isShiftHeld_TestOverride;
        private bool IsMultiSelectModifierDown =>
            isShiftHeld_TestOverride || ModifierKeys.HasFlag(Keys.Shift);

        // True while UpdateAnnotationToolbarFromSelection is programmatically pushing
        // values into the comboboxes so SelectedIndexChanged handlers don't write the
        // displayed value back into the selection (would create no-op undo steps and,
        // when the displayed value is a "Mixed" blank, would clobber the real per-shape
        // values).
        private bool isSyncingAnnotationToolbarControls;

        private List<AnnotationShape> CloneAnnotations()
        {
            return annotationShapes.Select(a => a.Clone()).ToList();
        }

        /// <summary>
        /// Update the shape selection. When <paramref name="add"/> is false (the default,
        /// matching legacy single-select behaviour), the selection is replaced with the
        /// target (or cleared when target is null). When add is true, the target is
        /// toggled into or out of the existing selection; passing null with add=true is
        /// a no-op.
        /// </summary>
        private void SelectAnnotation(AnnotationShape? target, bool add = false)
        {
            if (add)
            {
                if (target == null)
                {
                    return;
                }
                if (selectedShapes.Contains(target))
                {
                    selectedShapes.Remove(target);
                }
                else
                {
                    selectedShapes.Add(target);
                }
            }
            else
            {
                selectedShapes.Clear();
                if (target != null)
                {
                    selectedShapes.Add(target);
                }
            }

            SyncSelectionFlagsFromList();
            selectedAnnotation = selectedShapes.LastOrDefault();
            UpdateAnnotationToolbarFromSelection();
            UpdateAnnotationToolbarVisibility();
            pictureBox1?.Invalidate();
        }

        private void SyncSelectionFlagsFromList()
        {
            foreach (var annotation in annotationShapes)
            {
                annotation.Selected = selectedShapes.Contains(annotation);
            }
        }

        private void SyncSelectedAnnotation()
        {
            // Rebuild selectedShapes from each shape's Selected flag (used after undo/redo
            // or any code path that mutates annotationShapes wholesale).
            selectedShapes.Clear();
            foreach (var annotation in annotationShapes)
            {
                if (annotation.Selected)
                {
                    selectedShapes.Add(annotation);
                }
            }
            selectedAnnotation = selectedShapes.LastOrDefault();
        }

        private void UpdateDrawingToolButtons()
        {
            bool enable = HasEditableImage;

            if (!enable)
            {
                activeDrawingTool = DrawingTool.None;
                CancelAnnotationPreview();
            }

            if (arrowToolStripButton != null)
            {
                arrowToolStripButton.Enabled = enable;
                arrowToolStripButton.Checked = enable && activeDrawingTool == DrawingTool.Arrow;
            }

            if (rectangleToolStripButton != null)
            {
                rectangleToolStripButton.Enabled = enable;
                rectangleToolStripButton.Checked = enable && activeDrawingTool == DrawingTool.Rectangle;
            }

            UpdateAnnotationToolbarVisibility();
        }

        private void UpdateAnnotationToolbarVisibility()
        {
            bool isAnnotationToolActive = activeDrawingTool != DrawingTool.None;
            // Any selected shape (arrow or rect) keeps the options panel up. With multi-
            // selection this is "are there any shapes in the selection?" — texts alone
            // don't need the line-width / arrow controls visible.
            bool hasShapeSelection = selectedShapes.Count > 0;
            bool showPanel = isAnnotationToolActive || hasShapeSelection;
            bool showArrowControls =
                activeDrawingTool == DrawingTool.Arrow || AnyArrowInSelection();

            if (annotationToolSeparator != null)
            {
                annotationToolSeparator.Visible = showPanel;
            }
            if (lineThicknessLabel != null)
            {
                lineThicknessLabel.Visible = showPanel;
            }
            if (lineThicknessComboBox != null)
            {
                lineThicknessComboBox.Visible = showPanel;
            }
            if (annotationColorButton != null)
            {
                annotationColorButton.Visible = showPanel;
            }
            if (arrowSizeLabel != null)
            {
                arrowSizeLabel.Visible = showArrowControls;
            }
            if (arrowSizeComboBox != null)
            {
                arrowSizeComboBox.Visible = showArrowControls;
            }

            if (annotationOptionsToolStrip != null)
            {
                annotationOptionsToolStrip.Visible = showPanel;
                PositionOverlayToolStrips();
            }
        }

        private void ToggleDrawingTool(DrawingTool tool)
        {
            if (!HasEditableImage)
            {
                return;
            }

            // Deactivate text tool when switching to drawing tools
            if (isTextToolActive)
            {
                FinalizeActiveTextAnnotation();
                isTextToolActive = false;
                UpdateTextToolButtons();
                UpdateTextToolbarVisibility();
            }

            if (activeDrawingTool == tool && !isDrawingAnnotation)
            {
                activeDrawingTool = DrawingTool.None;
            }
            else
            {
                activeDrawingTool = tool;
            }

            CancelAnnotationPreview();
            UpdateDrawingToolButtons();
        }

        private void CancelAnnotationPreview()
        {
            if (isDrawingAnnotation && workingAnnotation != null)
            {
                annotationShapes.Remove(workingAnnotation);
            }

            isDrawingAnnotation = false;
            workingAnnotation = null;
            activeAnnotationHandle = AnnotationHandle.None;
            annotationSnapshotBeforeEdit = null;
            annotationChangedDuringDrag = false;
            annotationTranslateModeActive = false;
            annotationDraftAnchorPixel = Point.Empty;
            pictureBox1?.Invalidate();
        }

        private enum AnnotationSurface
        {
            Screen,
            Image
        }

        private void DrawAnnotations(Graphics graphics, AnnotationSurface surface)
        {
            if (annotationShapes.Count == 0)
            {
                return;
            }

            var previousSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            try
            {
                foreach (var annotation in annotationShapes)
                {
                    DrawAnnotationShape(graphics, annotation, surface);
                    if (surface == AnnotationSurface.Screen)
                    {
                        DrawAnnotationHandles(graphics, annotation);
                    }
                }
            }
            finally
            {
                graphics.SmoothingMode = previousSmoothing;
            }
        }

        private void DrawAnnotationShape(Graphics graphics, AnnotationShape annotation, AnnotationSurface surface)
        {
            float scale = surface == AnnotationSurface.Screen ? (float)ZoomLevel : 1f;
            float strokeWidth = Math.Max(1f, annotation.LineThickness * scale);

            // Selection feedback comes from the corner handles drawn separately in
            // DrawAnnotationHandles, so the stroke uses the user-chosen color regardless of
            // selection state.
            using var pen = new Pen(annotation.Color, strokeWidth)
            {
                Alignment = System.Drawing.Drawing2D.PenAlignment.Center
            };

            if (annotation.Type == AnnotationType.Arrow)
            {
                float arrowScale = scale * annotation.ArrowSize;
                using var arrowCap = new System.Drawing.Drawing2D.AdjustableArrowCap(4f * arrowScale, 6f * arrowScale, true);
                pen.CustomEndCap = arrowCap;
                var start = ConvertAnnotationPoint(annotation.Start, surface);
                var end = ConvertAnnotationPoint(annotation.End, surface);
                graphics.DrawLine(pen, start, end);
            }
            else
            {
                var rect = ConvertAnnotationRectangle(annotation.GetBounds(), surface);
                graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        private void DrawAnnotationHandles(Graphics graphics, AnnotationShape annotation)
        {
            // Draw hover hitbox for non-selected annotations
            if (annotation == hoveredAnnotation && !annotation.Selected)
            {
                DrawAnnotationHoverHitbox(graphics, annotation);
            }

            if (!annotation.Selected)
            {
                return;
            }

            const int handleSize = 8;
            var handles = GetAnnotationHandleRects(annotation, handleSize);
            foreach (var kvp in handles)
            {
                var rect = kvp.Value;
                graphics.FillRectangle(Brushes.White, rect);
                graphics.DrawRectangle(Pens.Black, rect);
            }
        }

        private void DrawAnnotationHoverHitbox(Graphics graphics, AnnotationShape annotation)
        {
            using var pen = new Pen(Color.FromArgb(180, Color.Cyan), 2f);
            pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;

            if (annotation.Type == AnnotationType.Arrow)
            {
                // Draw a stroke around the arrow line
                var start = PixelToFormCoordF(annotation.Start);
                var end = PixelToFormCoordF(annotation.End);
                float hitWidth = Math.Max(8f, 4f * (float)ZoomLevel);
                using var hitPen = new Pen(Color.FromArgb(60, Color.Cyan), hitWidth);
                graphics.DrawLine(hitPen, start, end);
                graphics.DrawLine(pen, start, end);
            }
            else
            {
                // Draw an inflated hitbox outside the rectangle
                var bounds = PixelToFormCoord(annotation.GetBounds());
                const int padding = 4;
                bounds.Inflate(padding, padding);
                
                using var fillBrush = new SolidBrush(Color.FromArgb(30, Color.Cyan));
                graphics.FillRectangle(fillBrush, bounds);
                graphics.DrawRectangle(pen, bounds);
            }
        }

        private PointF ConvertAnnotationPoint(Point pixelPoint, AnnotationSurface surface)
        {
            return surface == AnnotationSurface.Screen ? PixelToFormCoordF(pixelPoint) : new PointF(pixelPoint.X, pixelPoint.Y);
        }

        private RectangleF ConvertAnnotationRectangle(Rectangle rect, AnnotationSurface surface)
        {
            return surface == AnnotationSurface.Screen ? PixelToFormCoordF(rect) : new RectangleF(rect.Location, rect.Size);
        }

        private Dictionary<AnnotationHandle, Rectangle> GetAnnotationHandleRects(AnnotationShape annotation, int size)
        {
            var handles = new Dictionary<AnnotationHandle, Rectangle>();
            int half = size / 2;

            void AddHandle(AnnotationHandle handle, Point pixelPoint)
            {
                var center = PixelToFormCoordF(pixelPoint);
                var rect = new Rectangle((int)Math.Round(center.X) - half, (int)Math.Round(center.Y) - half, size, size);
                handles[handle] = rect;
            }

            if (annotation.Type == AnnotationType.Arrow)
            {
                AddHandle(AnnotationHandle.ArrowStart, annotation.Start);
                AddHandle(AnnotationHandle.ArrowEnd, annotation.End);
            }
            else
            {
                var bounds = annotation.GetBounds();
                AddHandle(AnnotationHandle.RectTopLeft, new Point(bounds.Left, bounds.Top));
                AddHandle(AnnotationHandle.RectTopRight, new Point(bounds.Right, bounds.Top));
                AddHandle(AnnotationHandle.RectBottomLeft, new Point(bounds.Left, bounds.Bottom));
                AddHandle(AnnotationHandle.RectBottomRight, new Point(bounds.Right, bounds.Bottom));
            }

            return handles;
        }

        private AnnotationShape? HitTestAnnotation(Point pixelPoint, Point formPoint)
        {
            for (int i = annotationShapes.Count - 1; i >= 0; i--)
            {
                var annotation = annotationShapes[i];
                if (annotation.Type == AnnotationType.Arrow)
                {
                    if (IsPointNearArrow(pixelPoint, annotation))
                    {
                        return annotation;
                    }
                }
                else
                {
                    var bounds = PixelToFormCoord(annotation.GetBounds());
                    if (bounds.Contains(formPoint))
                    {
                        return annotation;
                    }
                }
            }

            return null;
        }

        private bool IsPointNearArrow(Point pixelPoint, AnnotationShape annotation)
        {
            double distance = DistanceFromPointToSegment(pixelPoint, annotation.Start, annotation.End);
            double tolerance = Math.Max(4.0 / (double)ZoomLevel, 3.0);
            return distance <= tolerance;
        }

        private static double DistanceFromPointToSegment(Point point, Point start, Point end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;

            if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
            {
                dx = point.X - start.X;
                dy = point.Y - start.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            double t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));

            double projectionX = start.X + t * dx;
            double projectionY = start.Y + t * dy;
            double diffX = point.X - projectionX;
            double diffY = point.Y - projectionY;
            return Math.Sqrt(diffX * diffX + diffY * diffY);
        }

        private AnnotationHandle HitTestAnnotationHandle(Point formPoint)
        {
            if (selectedAnnotation == null)
            {
                return AnnotationHandle.None;
            }

            const int handleSize = 8;
            var handles = GetAnnotationHandleRects(selectedAnnotation, handleSize);
            foreach (var kvp in handles)
            {
                if (kvp.Value.Contains(formPoint))
                {
                    return kvp.Key;
                }
            }

            return AnnotationHandle.None;
        }

        private bool HandleAnnotationMouseDown(Point pixelPoint, Point formPoint)
        {
            if (!HasEditableImage)
            {
                return false;
            }

            // Selected-annotation handle (resize / move) takes priority — the user
            // is actively manipulating the selection.
            var handle = HitTestAnnotationHandle(formPoint);
            if (handle != AnnotationHandle.None)
            {
                annotationSnapshotBeforeEdit = CloneAnnotations();
                activeAnnotationHandle = handle;
                annotationDragOriginPixel = pixelPoint;
                annotationChangedDuringDrag = false;
                return true;
            }

            // Object selection wins over tool action: clicking on an existing annotation
            // selects it (and drops the active drawing tool) even when Arrow/Rectangle
            // is engaged. Without this the drawing tool would draw a new shape on top
            // of the click, making it impossible to re-select or move existing shapes
            // without first manually toggling the tool off.
            var hit = HitTestAnnotation(pixelPoint, formPoint);
            if (hit != null)
            {
                if (activeDrawingTool != DrawingTool.None)
                {
                    activeDrawingTool = DrawingTool.None;
                }
                bool addToSelection = IsMultiSelectModifierDown;
                if (addToSelection)
                {
                    SelectAnnotation(hit, add: true);
                }
                else if (!selectedShapes.Contains(hit))
                {
                    // Plain click on something not already in the selection → replace.
                    SelectAnnotation(hit, add: false);
                }
                // else: plain click on an already-selected item → preserve the multi-
                // selection so the user can drag the group as a whole. (Replacing would
                // collapse the selection to this single item and silently break multi-drag.)

                // Only arm a drag when the click ended with the hit shape still in the
                // selection — shift-deselecting (toggling a shape off) should not start
                // a translate gesture.
                if (selectedShapes.Contains(hit))
                {
                    annotationSnapshotBeforeEdit = CloneAnnotations();
                    // Snapshot text annotations too so a multi-drag including texts can
                    // be undone as a single step.
                    if (selectedTexts.Count > 0)
                    {
                        textAnnotationSnapshotBeforeEdit = CloneTextAnnotations();
                    }
                    activeAnnotationHandle = AnnotationHandle.Move;
                    annotationDragOriginPixel = pixelPoint;
                    annotationChangedDuringDrag = false;
                }
                return true;
            }

            if (activeDrawingTool != DrawingTool.None)
            {
                annotationSnapshotBeforeEdit = CloneAnnotations();
                var annotationType = activeDrawingTool == DrawingTool.Arrow ? AnnotationType.Arrow : AnnotationType.Rectangle;
                var clampedPoint = ClampPointToImage(pixelPoint);
                workingAnnotation = new AnnotationShape
                {
                    Type = annotationType,
                    Start = clampedPoint,
                    End = clampedPoint,
                    LineThickness = annotationLineThickness,
                    ArrowSize = annotationArrowSize,
                    Color = annotationColor,
                    Selected = true
                };
                annotationShapes.Add(workingAnnotation);
                SelectAnnotation(workingAnnotation);
                isDrawingAnnotation = true;
                annotationDraftAnchorPixel = clampedPoint;
                annotationTranslateModeActive = false;
                annotationChangedDuringDrag = false;
                return true;
            }

            if (selectedAnnotation != null)
            {
                SelectAnnotation(null);
            }

            return false;
        }

        private bool HandleAnnotationMouseMove(Point pixelPoint, Point formPoint, MouseButtons buttons)
        {
            if (!HasEditableImage)
            {
                return false;
            }

            if (buttons == MouseButtons.Left)
            {
                if (isDrawingAnnotation && workingAnnotation != null)
                {
                    if (annotationTranslateModeActive)
                    {
                        ApplyAnnotationTranslation(pixelPoint);
                    }
                    else
                    {
                        var targetPoint = pixelPoint;
                        if (workingAnnotation.Type == AnnotationType.Rectangle && ModifierKeys.HasFlag(Keys.Shift))
                        {
                            targetPoint = ConstrainPointToSquare(annotationDraftAnchorPixel, pixelPoint);
                        }

                        workingAnnotation.End = ClampPointToImage(targetPoint);
                    }

                    annotationChangedDuringDrag = true;
                    pictureBox1.Invalidate();
                    return true;
                }

                if (activeAnnotationHandle != AnnotationHandle.None)
                {
                    ApplyAnnotationHandleDrag(pixelPoint);
                    annotationChangedDuringDrag = true;
                    pictureBox1.Invalidate();
                    return true;
                }
            }
            else if (buttons == MouseButtons.None)
            {
                var handle = HitTestAnnotationHandle(formPoint);
                if (handle != AnnotationHandle.None)
                {
                    SetHoveredAnnotation(null);
                    Cursor = Cursors.Cross;
                    return true;
                }

                var hit = HitTestAnnotation(pixelPoint, formPoint);
                SetHoveredAnnotation(hit);
                if (hit != null)
                {
                    Cursor = Cursors.Hand;
                    return true;
                }
            }

            return false;
        }

        private void SetHoveredAnnotation(AnnotationShape? annotation)
        {
            if (hoveredAnnotation != annotation)
            {
                hoveredAnnotation = annotation;
                pictureBox1?.Invalidate();
            }
        }

        private bool HandleAnnotationMouseUp(MouseButtons button, Point releasePixel)
        {
            if (!HasEditableImage || button != MouseButtons.Left)
            {
                return false;
            }

            if (isDrawingAnnotation)
            {
                CompleteAnnotationDraft(releasePixel);
                return true;
            }

            if (activeAnnotationHandle != AnnotationHandle.None)
            {
                if (annotationChangedDuringDrag)
                {
                    CommitAnnotationUndo();
                }

                activeAnnotationHandle = AnnotationHandle.None;
                annotationSnapshotBeforeEdit = null;
                annotationChangedDuringDrag = false;
                return true;
            }

            return false;
        }

        private void ApplyAnnotationHandleDrag(Point currentPixel)
        {
            if (selectedAnnotation == null)
            {
                return;
            }

            var clamped = ClampPointToImage(currentPixel);
            var target = selectedAnnotation;

            switch (activeAnnotationHandle)
            {
                case AnnotationHandle.Move:
                    // Move applies to the WHOLE multi-selection so users can shift-click
                    // several items and drag them together. Resize handles below remain
                    // single-target — multi-resize would require bounding-box scaling that's
                    // out of scope for this slice.
                    var delta = clamped.Subtract(annotationDragOriginPixel);
                    if (delta.X == 0 && delta.Y == 0)
                    {
                        return;
                    }

                    delta = ClampMultiSelectionMoveDelta(delta);

                    if (delta.X == 0 && delta.Y == 0)
                    {
                        annotationDragOriginPixel = clamped;
                        return;
                    }

                    TranslateSelectionBy(delta);
                    annotationDragOriginPixel = clamped;
                    break;
                case AnnotationHandle.ArrowStart:
                    target.Start = clamped;
                    break;
                case AnnotationHandle.ArrowEnd:
                    target.End = clamped;
                    break;
                case AnnotationHandle.RectTopLeft:
                    target.Start = new Point(clamped.X, clamped.Y);
                    break;
                case AnnotationHandle.RectTopRight:
                    target.Start = new Point(target.Start.X, clamped.Y);
                    target.End = new Point(clamped.X, target.End.Y);
                    break;
                case AnnotationHandle.RectBottomLeft:
                    target.Start = new Point(clamped.X, target.Start.Y);
                    target.End = new Point(target.End.X, clamped.Y);
                    break;
                case AnnotationHandle.RectBottomRight:
                    target.End = new Point(clamped.X, clamped.Y);
                    break;
                default:
                    break;
            }

            if (target.Type == AnnotationType.Rectangle)
            {
                NormalizeRectangleAnnotation(target);
            }
        }

        /// <summary>
        /// Restrict a proposed move-delta so the bounding box of the entire multi-selection
        /// (shapes + texts) stays inside the image. Text positions are treated as single
        /// points — matches the existing single-text-drag clamping behaviour.
        /// </summary>
        private Point ClampMultiSelectionMoveDelta(Point delta)
        {
            if (pictureBox1?.Image == null) return delta;
            if (selectedShapes.Count == 0 && selectedTexts.Count == 0) return delta;

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var shape in selectedShapes)
            {
                minX = Math.Min(minX, Math.Min(shape.Start.X, shape.End.X));
                maxX = Math.Max(maxX, Math.Max(shape.Start.X, shape.End.X));
                minY = Math.Min(minY, Math.Min(shape.Start.Y, shape.End.Y));
                maxY = Math.Max(maxY, Math.Max(shape.Start.Y, shape.End.Y));
            }
            foreach (var text in selectedTexts)
            {
                minX = Math.Min(minX, text.Position.X);
                maxX = Math.Max(maxX, text.Position.X);
                minY = Math.Min(minY, text.Position.Y);
                maxY = Math.Max(maxY, text.Position.Y);
            }

            var bounds = GetImageBounds();
            delta.X = Math.Max(bounds.Left - minX, Math.Min(bounds.Right - maxX, delta.X));
            delta.Y = Math.Max(bounds.Top - minY, Math.Min(bounds.Bottom - maxY, delta.Y));
            return delta;
        }

        /// <summary>
        /// Translate every selected shape and text annotation by the given delta. Caller is
        /// responsible for clamping the delta and recording an undo snapshot before/after.
        /// </summary>
        private void TranslateSelectionBy(Point delta)
        {
            foreach (var shape in selectedShapes)
            {
                shape.Start = shape.Start.Add(delta);
                shape.End = shape.End.Add(delta);
            }
            foreach (var text in selectedTexts)
            {
                text.Position = text.Position.Add(delta);
            }
        }

        /// <summary>
        /// Remove every selected shape and text annotation in one combined undo step.
        /// Caller is responsible for ensuring this is the intended action (e.g. Delete
        /// pressed outside text-editing mode with multi-selection present).
        /// </summary>
        private void DeleteMultiSelection()
        {
            bool hadShapes = selectedShapes.Count > 0;
            bool hadTexts = selectedTexts.Count > 0;
            if (!hadShapes && !hadTexts) return;

            var shapesBefore = hadShapes ? CloneAnnotations() : null;
            var textsBefore = hadTexts ? CloneTextAnnotations() : null;

            foreach (var shape in selectedShapes.ToList())
            {
                annotationShapes.Remove(shape);
            }
            foreach (var text in selectedTexts.ToList())
            {
                textAnnotations.Remove(text);
            }

            SelectAnnotation(null);
            SelectTextAnnotation(null);
            activeTextAnnotation = null;

            var shapesAfter = hadShapes ? CloneAnnotations() : null;
            var textsAfter = hadTexts ? CloneTextAnnotations() : null;
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false,
                shapesBefore, shapesAfter,
                textsBefore, textsAfter);
            pictureBox1?.Invalidate();
        }

        private void CompleteAnnotationDraft(Point pixelPoint)
        {
            if (!isDrawingAnnotation || workingAnnotation == null)
            {
                return;
            }

            workingAnnotation.End = ClampPointToImage(pixelPoint);
            if (workingAnnotation.Type == AnnotationType.Rectangle)
            {
                NormalizeRectangleAnnotation(workingAnnotation);
            }

            if (!workingAnnotation.IsValid())
            {
                annotationShapes.Remove(workingAnnotation);
            }
            else
            {
                SelectAnnotation(workingAnnotation);
                CommitAnnotationUndo();
            }

            isDrawingAnnotation = false;
            workingAnnotation = null;
            annotationSnapshotBeforeEdit = null;
            annotationChangedDuringDrag = false;
            annotationTranslateModeActive = false;
            annotationDraftAnchorPixel = Point.Empty;
            pictureBox1.Invalidate();
        }

        private void NormalizeRectangleAnnotation(AnnotationShape annotation)
        {
            if (annotation.Type != AnnotationType.Rectangle)
            {
                return;
            }

            var rect = RectangleExt.fromPoints(annotation.Start, annotation.End);
            annotation.Start = new Point(rect.Left, rect.Top);
            annotation.End = new Point(rect.Right, rect.Bottom);
        }

        private bool BeginAnnotationTranslation()
        {
            if (!isDrawingAnnotation || workingAnnotation == null || annotationTranslateModeActive)
            {
                return false;
            }

            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                return false;
            }

            annotationTranslateModeActive = true;
            annotationTranslationOriginPixel = GetCursorPixelPosition();
            annotationTranslationStartSnapshot = workingAnnotation.Start;
            annotationTranslationEndSnapshot = workingAnnotation.End;
            annotationTranslationAnchorSnapshot = annotationDraftAnchorPixel;
            return true;
        }

        private void ApplyAnnotationTranslation(Point currentPixel)
        {
            if (!annotationTranslateModeActive || workingAnnotation == null)
            {
                return;
            }

            var delta = currentPixel.Subtract(annotationTranslationOriginPixel);
            if (delta.X == 0 && delta.Y == 0)
            {
                return;
            }

            var candidateStart = annotationTranslationStartSnapshot.Add(delta);
            var candidateEnd = annotationTranslationEndSnapshot.Add(delta);
            var candidateAnchor = annotationTranslationAnchorSnapshot.Add(delta);

            var clampOffset = CalculateShapeClampOffset(candidateStart, candidateEnd);
            if (clampOffset != Point.Empty)
            {
                candidateStart = candidateStart.Add(clampOffset);
                candidateEnd = candidateEnd.Add(clampOffset);
                candidateAnchor = candidateAnchor.Add(clampOffset);
            }

            workingAnnotation.Start = candidateStart;
            workingAnnotation.End = candidateEnd;
            annotationDraftAnchorPixel = candidateAnchor;

        }

        private Point CalculateShapeClampOffset(Point start, Point end)
        {
            if (pictureBox1?.Image == null)
            {
                return Point.Empty;
            }

            var bounds = GetImageBounds();
            int minX = Math.Min(start.X, end.X);
            int maxX = Math.Max(start.X, end.X);
            int minY = Math.Min(start.Y, end.Y);
            int maxY = Math.Max(start.Y, end.Y);

            int offsetX = 0;
            if (minX < bounds.Left)
            {
                offsetX = bounds.Left - minX;
            }
            else if (maxX > bounds.Right)
            {
                offsetX = bounds.Right - maxX;
            }

            int offsetY = 0;
            if (minY < bounds.Top)
            {
                offsetY = bounds.Top - minY;
            }
            else if (maxY > bounds.Bottom)
            {
                offsetY = bounds.Bottom - maxY;
            }

            if (offsetX == 0 && offsetY == 0)
            {
                return Point.Empty;
            }

            return new Point(offsetX, offsetY);
        }

        private static Point ConstrainPointToSquare(Point anchor, Point current)
        {
            int dx = current.X - anchor.X;
            int dy = current.Y - anchor.Y;

            if (dx == 0 && dy == 0)
            {
                return current;
            }

            int absDx = Math.Abs(dx);
            int absDy = Math.Abs(dy);
            int size = Math.Max(absDx, absDy);

            int signX = dx == 0 ? (dy >= 0 ? 1 : -1) : Math.Sign(dx);
            int signY = dy == 0 ? (dx >= 0 ? 1 : -1) : Math.Sign(dy);

            return new Point(anchor.X + size * signX, anchor.Y + size * signY);
        }

        private Point GetCursorPixelPosition()
        {
            if (pictureBox1 == null)
            {
                return Point.Empty;
            }

            var cursorClient = pictureBox1.PointToClient(Cursor.Position);
            return FormCoordToPixel(cursorClient);
        }

        private Point ClampPointToImage(Point point)
        {
            if (pictureBox1.Image == null)
            {
                return point;
            }

            var bounds = GetImageBounds();
            int clampedX = Math.Max(bounds.Left, Math.Min(bounds.Right, point.X));
            int clampedY = Math.Max(bounds.Top, Math.Min(bounds.Bottom, point.Y));
            return new Point(clampedX, clampedY);
        }

        private static Point ClampPointToBounds(Point point, Rectangle bounds)
        {
            int clampedX = Math.Max(bounds.Left, Math.Min(bounds.Right, point.X));
            int clampedY = Math.Max(bounds.Top, Math.Min(bounds.Bottom, point.Y));
            return new Point(clampedX, clampedY);
        }

        private void CommitAnnotationUndo()
        {
            if (annotationSnapshotBeforeEdit == null && textAnnotationSnapshotBeforeEdit == null)
            {
                return;
            }

            // Combine shape + text diffs into a single undo step so a multi-selection drag
            // is undone with one Ctrl+Z. Each half is captured only when we actually staged
            // a snapshot on the corresponding type at drag-start.
            var shapesAfter = annotationSnapshotBeforeEdit != null ? CloneAnnotations() : null;
            var textsAfter = textAnnotationSnapshotBeforeEdit != null ? CloneTextAnnotations() : null;
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false,
                annotationSnapshotBeforeEdit, shapesAfter,
                textAnnotationSnapshotBeforeEdit, textsAfter);
            annotationSnapshotBeforeEdit = null;
            textAnnotationSnapshotBeforeEdit = null;
        }

        private void ApplyAnnotationState(List<AnnotationShape>? source)
        {
            if (source == null)
            {
                return;
            }

            annotationShapes.Clear();
            foreach (var annotation in source)
            {
                annotationShapes.Add(annotation.Clone());
            }

            SyncSelectedAnnotation();
        }

        private void ApplyCropToAnnotations(Point cropOrigin, Size newSize)
        {
            if (annotationShapes.Count == 0)
            {
                return;
            }

            var newBounds = new Rectangle(Point.Empty, newSize);
            var updated = new List<AnnotationShape>();

            foreach (var shape in annotationShapes)
            {
                var clone = shape.Clone();
                clone.Start = clone.Start.Subtract(cropOrigin);
                clone.End = clone.End.Subtract(cropOrigin);

                if (clone.Type == AnnotationType.Rectangle)
                {
                    var rect = RectangleExt.fromPoints(clone.Start, clone.End);
                    var intersection = Rectangle.Intersect(newBounds, rect);
                    if (intersection.Width < 2 || intersection.Height < 2)
                    {
                        continue;
                    }

                    clone.Start = new Point(intersection.Left, intersection.Top);
                    clone.End = new Point(intersection.Right, intersection.Bottom);
                }
                else
                {
                    var rect = RectangleExt.fromPoints(clone.Start, clone.End);
                    if (!rect.IntersectsWith(newBounds))
                    {
                        continue;
                    }

                    clone.Start = ClampPointToBounds(clone.Start, newBounds);
                    clone.End = ClampPointToBounds(clone.End, newBounds);
                }

                updated.Add(clone);
            }

            annotationShapes.Clear();
            annotationShapes.AddRange(updated);
            SyncSelectedAnnotation();
        }

        private void InitializeAnnotationToolbar()
        {
            if (lineThicknessComboBox != null)
            {
                lineThicknessComboBox.Items.AddRange(new object[] { "1", "2", "3", "4", "5", "6", "8", "10" });
                int defaultIndex = lineThicknessComboBox.Items.IndexOf(annotationLineThickness.ToString());
                lineThicknessComboBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 1; // Default to "2"
            }

            if (arrowSizeComboBox != null)
            {
                arrowSizeComboBox.Items.AddRange(new object[] { "0.5", "0.75", "1", "1.25", "1.5", "2", "2.5", "3" });
                int defaultIndex = arrowSizeComboBox.Items.IndexOf(annotationArrowSize.ToString());
                arrowSizeComboBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 2; // Default to "1"
            }

            UpdateAnnotationColorButtonAppearance();
        }

        private void annotationColorButton_Click(object? sender, EventArgs e)
        {
            // Choose the dialog's starting color: use the unanimous selection color when
            // all selected items agree, fall back to the primary's color or tool default.
            using var dialog = new ColorDialog
            {
                Color = GetRepresentativeSelectionColor() ?? annotationColor,
                FullOpen = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            annotationColor = dialog.Color;
            ApplyColorToSelection(dialog.Color);
            UpdateAnnotationColorButtonAppearance();
        }

        /// <summary>
        /// Push one undo step that applies the colour to every selected shape and text
        /// annotation. Items in the selection that don't have an applicable colour slot
        /// are silently skipped.
        /// </summary>
        private void ApplyColorToSelection(Color color)
        {
            bool anyShapeChange = selectedShapes.Count > 0;
            bool anyTextChange = selectedTexts.Count > 0;
            if (!anyShapeChange && !anyTextChange)
            {
                return;
            }

            List<AnnotationShape>? shapesBefore = null;
            List<TextAnnotation>? textsBefore = null;
            if (anyShapeChange) shapesBefore = CloneAnnotations();
            if (anyTextChange) textsBefore = CloneTextAnnotations();

            foreach (var shape in selectedShapes)
            {
                shape.Color = color;
            }
            foreach (var text in selectedTexts)
            {
                text.TextColor = color;
            }

            // Push as a single combined undo step so a single Ctrl+Z reverts the whole
            // multi-target colour change.
            var shapesAfter = anyShapeChange ? CloneAnnotations() : null;
            var textsAfter = anyTextChange ? CloneTextAnnotations() : null;
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false,
                shapesBefore, shapesAfter, textsBefore, textsAfter);
            pictureBox1?.Invalidate();
        }

        /// <summary>
        /// Returns the colour shared by every selected item (shapes + texts), or null
        /// when the selection contains a mix of colours or is empty.
        /// </summary>
        private Color? GetRepresentativeSelectionColor()
        {
            Color? candidate = null;
            foreach (var shape in selectedShapes)
            {
                if (candidate == null) candidate = shape.Color;
                else if (candidate.Value.ToArgb() != shape.Color.ToArgb()) return null;
            }
            foreach (var text in selectedTexts)
            {
                if (candidate == null) candidate = text.TextColor;
                else if (candidate.Value.ToArgb() != text.TextColor.ToArgb()) return null;
            }
            return candidate;
        }

        private void UpdateAnnotationColorButtonAppearance()
        {
            if (annotationColorButton == null)
            {
                return;
            }

            int selectionCount = selectedShapes.Count + selectedTexts.Count;
            var representative = GetRepresentativeSelectionColor();

            if (selectionCount > 1 && representative == null)
            {
                // Mixed colours across the multi-selection — neutral swatch + "Mixed".
                annotationColorButton.BackColor = SystemColors.Control;
                annotationColorButton.ForeColor = SystemColors.ControlText;
                annotationColorButton.Text = "Mixed";
                return;
            }

            var swatch = representative ?? annotationColor;
            annotationColorButton.BackColor = swatch;
            annotationColorButton.ForeColor = GetContrastColor(swatch);
            annotationColorButton.Text = "Color";
        }

        private void lineThicknessComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (isSyncingAnnotationToolbarControls)
            {
                return;
            }
            if (lineThicknessComboBox?.SelectedItem is string thicknessStr &&
                float.TryParse(thicknessStr, out float thickness) && thickness > 0)
            {
                annotationLineThickness = thickness;
                ApplyLineThicknessToSelection(thickness);
            }
        }

        private void arrowSizeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (isSyncingAnnotationToolbarControls)
            {
                return;
            }
            if (arrowSizeComboBox?.SelectedItem is string sizeStr &&
                float.TryParse(sizeStr, out float size) && size > 0)
            {
                annotationArrowSize = size;
                ApplyArrowSizeToSelection(size);
            }
        }

        /// <summary>
        /// Write the line thickness to every selected shape (text annotations don't have
        /// a line-width slot and are silently skipped). Shapes that already match the
        /// requested value are excluded so the undo step records a real change.
        /// </summary>
        private void ApplyLineThicknessToSelection(float thickness)
        {
            var targets = selectedShapes.Where(s => s.LineThickness != thickness).ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var before = CloneAnnotations();
            foreach (var shape in targets)
            {
                shape.LineThickness = thickness;
            }
            var after = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false, before, after);
            pictureBox1?.Invalidate();
        }

        /// <summary>
        /// Write the arrow head size to every selected arrow shape (rects and texts are
        /// silently skipped). Arrows that already match the requested value are excluded.
        /// </summary>
        private void ApplyArrowSizeToSelection(float size)
        {
            var targets = selectedShapes
                .Where(s => s.Type == AnnotationType.Arrow && s.ArrowSize != size)
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var before = CloneAnnotations();
            foreach (var shape in targets)
            {
                shape.ArrowSize = size;
            }
            var after = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false, before, after);
            pictureBox1?.Invalidate();
        }

        private void UpdateAnnotationToolbarFromSelection()
        {
            // Programmatic pushes — the handlers must not interpret these as user edits
            // and write them back into the selection.
            isSyncingAnnotationToolbarControls = true;
            try
            {
                if (lineThicknessComboBox != null && selectedShapes.Count > 0)
                {
                    float? unanimous = GetUnanimousLineThickness();
                    if (unanimous.HasValue)
                    {
                        int index = lineThicknessComboBox.Items.IndexOf(unanimous.Value.ToString());
                        lineThicknessComboBox.SelectedIndex = index >= 0 ? index : -1;
                    }
                    else
                    {
                        // Mixed thicknesses across the selection — show the combobox blank
                        // (no value) as the indeterminate state. Picking a value from the
                        // dropdown will apply it to every selected shape.
                        lineThicknessComboBox.SelectedIndex = -1;
                    }
                }

                if (arrowSizeComboBox != null && AnyArrowInSelection())
                {
                    float? unanimous = GetUnanimousArrowSize();
                    if (unanimous.HasValue)
                    {
                        int index = arrowSizeComboBox.Items.IndexOf(unanimous.Value.ToString());
                        arrowSizeComboBox.SelectedIndex = index >= 0 ? index : -1;
                    }
                    else
                    {
                        arrowSizeComboBox.SelectedIndex = -1;
                    }
                }
            }
            finally
            {
                isSyncingAnnotationToolbarControls = false;
            }

            UpdateAnnotationColorButtonAppearance();
        }

        private float? GetUnanimousLineThickness()
        {
            float? candidate = null;
            foreach (var shape in selectedShapes)
            {
                if (candidate == null) candidate = shape.LineThickness;
                else if (candidate.Value != shape.LineThickness) return null;
            }
            return candidate;
        }

        private float? GetUnanimousArrowSize()
        {
            float? candidate = null;
            foreach (var shape in selectedShapes)
            {
                if (shape.Type != AnnotationType.Arrow) continue;
                if (candidate == null) candidate = shape.ArrowSize;
                else if (candidate.Value != shape.ArrowSize) return null;
            }
            return candidate;
        }

        private bool AnyArrowInSelection() =>
            selectedShapes.Any(s => s.Type == AnnotationType.Arrow);

    }
}
