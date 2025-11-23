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
        public bool Selected { get; set; }

        public AnnotationShape Clone()
        {
            return new AnnotationShape
            {
                Id = Id,
                Type = Type,
                Start = Start,
                End = End,
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
        private DrawingTool activeDrawingTool = DrawingTool.None;
        private bool isDrawingAnnotation;
        private AnnotationShape? workingAnnotation;
        private AnnotationShape? selectedAnnotation;
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
        private List<AnnotationShape> CloneAnnotations()
        {
            return annotationShapes.Select(a => a.Clone()).ToList();
        }

        private void SelectAnnotation(AnnotationShape? target)
        {
            foreach (var annotation in annotationShapes)
            {
                annotation.Selected = annotation == target;
            }

            selectedAnnotation = target;
            pictureBox1?.Invalidate();
        }

        private void SyncSelectedAnnotation()
        {
            selectedAnnotation = annotationShapes.FirstOrDefault(a => a.Selected);
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
        }

        private void ToggleDrawingTool(DrawingTool tool)
        {
            if (!HasEditableImage)
            {
                return;
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

            float scale = surface == AnnotationSurface.Screen ? (float)ZoomLevel : 1f;
            float strokeWidth = Math.Max(1f, 2f * scale);

            var previousSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            try
            {
                foreach (var annotation in annotationShapes)
                {
                    DrawAnnotationShape(graphics, annotation, surface, strokeWidth);
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

        private void DrawAnnotationShape(Graphics graphics, AnnotationShape annotation, AnnotationSurface surface, float strokeWidth)
        {
            var penColor = annotation.Selected ? Color.OrangeRed : Color.Red;
            using var pen = new Pen(penColor, strokeWidth)
            {
                Alignment = System.Drawing.Drawing2D.PenAlignment.Center
            };

            if (annotation.Type == AnnotationType.Arrow)
            {
                float arrowScale = surface == AnnotationSurface.Screen ? (float)ZoomLevel : 1f;
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

            var handle = HitTestAnnotationHandle(formPoint);
            if (handle != AnnotationHandle.None)
            {
                annotationSnapshotBeforeEdit = CloneAnnotations();
                activeAnnotationHandle = handle;
                annotationDragOriginPixel = pixelPoint;
                annotationChangedDuringDrag = false;
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

            var hit = HitTestAnnotation(pixelPoint, formPoint);
            if (hit != null)
            {
                SelectAnnotation(hit);
                annotationSnapshotBeforeEdit = CloneAnnotations();
                activeAnnotationHandle = AnnotationHandle.Move;
                annotationDragOriginPixel = pixelPoint;
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
                    Cursor = Cursors.Cross;
                    return true;
                }

                var hit = HitTestAnnotation(pixelPoint, formPoint);
                if (hit != null)
                {
                    Cursor = Cursors.Hand;
                    return true;
                }
            }

            return false;
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
                    var delta = clamped.Subtract(annotationDragOriginPixel);
                    if (delta.X == 0 && delta.Y == 0)
                    {
                        return;
                    }

                    if (pictureBox1.Image != null)
                    {
                        var bounds = GetImageBounds();
                        int minX = Math.Min(target.Start.X, target.End.X);
                        int maxX = Math.Max(target.Start.X, target.End.X);
                        int minY = Math.Min(target.Start.Y, target.End.Y);
                        int maxY = Math.Max(target.Start.Y, target.End.Y);

                        delta.X = Math.Max(bounds.Left - minX, Math.Min(bounds.Right - maxX, delta.X));
                        delta.Y = Math.Max(bounds.Top - minY, Math.Min(bounds.Bottom - maxY, delta.Y));
                    }

                    if (delta.X == 0 && delta.Y == 0)
                    {
                        annotationDragOriginPixel = clamped;
                        return;
                    }

                    target.Start = target.Start.Add(delta);
                    target.End = target.End.Add(delta);
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
            if (annotationSnapshotBeforeEdit == null)
            {
                return;
            }

            var afterState = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false, annotationSnapshotBeforeEdit, afterState);
            annotationSnapshotBeforeEdit = null;
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


    }
}
