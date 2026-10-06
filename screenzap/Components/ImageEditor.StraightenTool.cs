using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ImageEditor
    {
        // isStraightenToolActive lives on ImageEditor.Tool.cs as a computed accessor.
        // Clockwise in image coordinates: top-left, top-right, bottom-right, bottom-left.
        private Point[]? straightenCorners;
        private Point straightenDragOrigin;
        private int straightenDragCorner = -1;
        private bool isStraightenDragging;

        internal bool ActivateStraightenTool()
        {
            if (!HasEditableImage)
            {
                return false;
            }

            if (isStraightenToolActive)
            {
                // Already engaged: a second toolbar click / Ctrl+L must not discard the
                // corners the user has positioned.
                return true;
            }

            isStraightenToolActive = true;
            straightenCorners = null;
            straightenDragCorner = -1;
            isStraightenDragging = false;
            var selection = ClampToImage(Selection);
            if (selection.Width > 1 && selection.Height > 1)
            {
                SetStraightenRectangle(selection.Location, new Point(selection.Right - 1, selection.Bottom - 1));
            }

            Cursor = Cursors.Cross;

            if (straightenToolStripButton != null)
            {
                straightenToolStripButton.Checked = true;
            }

            if (straightenToolStrip != null)
            {
                straightenToolStrip.Visible = true;
                PositionOverlayToolStrips();
            }

            UpdateStraightenToolbarState();
            UpdateCommandUI();
            pictureBox1.Invalidate();
            return true;
        }

        internal void DeactivateStraightenTool(bool apply)
        {
            if (!isStraightenToolActive)
            {
                return;
            }

            if (apply)
            {
                // Invalid/crossed corners must not close the tool or mutate the image.
                if (isStraightenDragging || !lib.ImageStraightener.IsValidPerspectiveQuad(straightenCorners))
                {
                    return;
                }
                ApplyStraightenPerspective();
            }

            isStraightenToolActive = false;
            isStraightenDragging = false;
            straightenDragCorner = -1;
            straightenCorners = null;
            pictureBox1.Capture = false;
            Cursor = Cursors.Default;

            if (straightenToolStripButton != null)
            {
                straightenToolStripButton.Checked = false;
            }

            if (straightenToolStrip != null)
            {
                straightenToolStrip.Visible = false;
                PositionOverlayToolStrips();
            }

            UpdateCommandUI();
            pictureBox1.Invalidate();
        }

        private void ApplyStraightenPerspective()
        {
            if (!HasEditableImage || pictureBox1.Image == null || straightenCorners == null)
            {
                return;
            }

            // Perspective changes cannot be represented by the affine layer/annotation frames.
            // Warp the visible composite, keeping the editable originals in the same undo step.
            using var composite = BuildCompositeImage();
            using var corrected = lib.ImageStraightener.CorrectPerspective(composite, straightenCorners);
            CopyImageResolution(pictureBox1.Image, corrected);
            var before = new Bitmap(pictureBox1.Image);
            var after = new Bitmap(corrected);
            var selectionBefore = Selection;
            var shapesBefore = CloneAnnotations();
            var textsBefore = CloneTextAnnotations();
            var layersBefore = CloneLayers();

            pictureBox1.Image.Dispose();
            pictureBox1.Image = new Bitmap(corrected);
            ClearSelection();
            ApplyAnnotationState(new());
            ApplyTextAnnotationState(new());
            ApplyLayerState(new());
            PushUndoStep(Rectangle.Empty, before, after, selectionBefore, Rectangle.Empty,
                replacesImage: true,
                shapesBefore: shapesBefore, shapesAfter: CloneAnnotations(),
                textsBefore: textsBefore, textsAfter: CloneTextAnnotations(),
                layersBefore: layersBefore, layersAfter: CloneLayers());
            RecenterViewportAfterImageChange(resizeWindow: true);
            UpdateStatusBar();
        }

        private void SetStraightenRectangle(Point start, Point end)
        {
            int left = Math.Min(start.X, end.X);
            int top = Math.Min(start.Y, end.Y);
            int right = Math.Max(start.X, end.X);
            int bottom = Math.Max(start.Y, end.Y);
            straightenCorners = new[]
            {
                new Point(left, top), new Point(right, top),
                new Point(right, bottom), new Point(left, bottom)
            };
        }

        private Point ClampStraightenPoint(Point clientPoint)
        {
            var pixel = FormCoordToPixel(clientPoint);
            var size = pictureBox1.Image!.Size;
            return new Point(Math.Clamp(pixel.X, 0, size.Width - 1), Math.Clamp(pixel.Y, 0, size.Height - 1));
        }

        private int HitTestStraightenCorner(Point clientPoint)
        {
            if (straightenCorners == null) return -1;
            // Client-space tolerance keeps the handles usable at every zoom level.
            int closest = -1;
            double bestDistance = 10 * 10;
            for (int i = 0; i < 4; i++)
            {
                var corner = PixelToFormCoord(straightenCorners[i]);
                double dx = corner.X - clientPoint.X;
                double dy = corner.Y - clientPoint.Y;
                double distance = dx * dx + dy * dy;
                if (distance <= bestDistance)
                {
                    closest = i;
                    bestDistance = distance;
                }
            }
            return closest;
        }

        private void BeginStraightenDrag(Point clientPoint)
        {
            straightenDragCorner = HitTestStraightenCorner(clientPoint);
            straightenDragOrigin = ClampStraightenPoint(clientPoint);
            if (straightenDragCorner < 0)
            {
                SetStraightenRectangle(straightenDragOrigin, straightenDragOrigin);
            }
            isStraightenDragging = true;
            pictureBox1.Capture = true;
            UpdateStraightenToolbarState();
            pictureBox1.Invalidate();
        }

        private void UpdateStraightenDrag(Point clientPoint)
        {
            if (!isStraightenDragging) return;
            var point = ClampStraightenPoint(clientPoint);
            if (straightenDragCorner >= 0)
            {
                straightenCorners![straightenDragCorner] = point;
            }
            else
            {
                SetStraightenRectangle(straightenDragOrigin, point);
            }
            UpdateStraightenToolbarState();
            pictureBox1.Invalidate();
        }

        private void EndStraightenDrag(Point clientPoint)
        {
            UpdateStraightenDrag(clientPoint);
            isStraightenDragging = false;
            straightenDragCorner = -1;
            pictureBox1.Capture = false;
            UpdateStraightenToolbarState();
        }

        /// <summary>
        /// Rotates the current selection in place, or the whole image (expanding the canvas via
        /// <see cref="lib.ImageStraightener.RotateImage"/>) when no selection is active. Used by
        /// the free-rotate tool (drag/typed angle).
        /// </summary>
        private void RotateEditorContentBy(double angleDegrees)
        {
            if (Math.Abs(angleDegrees) < 0.01 || !HasEditableImage || pictureBox1.Image == null)
            {
                return;
            }

            if (!Selection.IsEmpty)
            {
                ApplyRotationToSelection(angleDegrees);
                return;
            }

            var selectionBefore = Selection;
            var beforeFullImage = new Bitmap(pictureBox1.Image);
            Bitmap? rotated = null;

            try
            {
                rotated = lib.ImageStraightener.RotateImage(beforeFullImage, angleDegrees);

                pictureBox1.Image?.Dispose();
                pictureBox1.Image = new Bitmap(rotated);

                PushUndoStep(Rectangle.Empty, beforeFullImage, new Bitmap(rotated), selectionBefore, Rectangle.Empty, true);

                rotated.Dispose();

                MarkDirtyAndNotify();
                RecenterViewportAfterImageChange(resizeWindow: true);
                UpdateStatusBar();
            }
            catch
            {
                beforeFullImage.Dispose();
                rotated?.Dispose();
                throw;
            }
        }

        private void ApplyRotationToSelection(double correctionAngle)
        {
            if (pictureBox1.Image == null)
            {
                return;
            }

            var clampedSelection = ClampToImage(Selection);
            if (clampedSelection.Width <= 0 || clampedSelection.Height <= 0)
            {
                return;
            }

            var selectionBefore = Selection;
            var before = CaptureRegion(clampedSelection);
            if (before == null)
            {
                return;
            }

            Bitmap? rotated = null;
            Bitmap? after = null;

            try
            {
                rotated = lib.ImageStraightener.RotateImage(before, correctionAngle);

                after = new Bitmap(before.Width, before.Height, PixelFormat.Format32bppArgb);
                using (var gAfter = Graphics.FromImage(after))
                {
                    gAfter.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    gAfter.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    gAfter.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                    gAfter.DrawImage(before, new Rectangle(0, 0, before.Width, before.Height));

                    int offsetX = (before.Width - rotated.Width) / 2;
                    int offsetY = (before.Height - rotated.Height) / 2;
                    gAfter.DrawImage(rotated, new Rectangle(offsetX, offsetY, rotated.Width, rotated.Height));
                }

                using (var g = Graphics.FromImage(pictureBox1.Image))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(after, clampedSelection);
                }

                PushUndoStep(clampedSelection, before, after, selectionBefore, Selection);
                MarkDirtyAndNotify();
                UpdateCommandUI();
                UpdateStatusBar();
                pictureBox1.Invalidate();
            }
            catch
            {
                before.Dispose();
                rotated?.Dispose();
                after?.Dispose();
                throw;
            }
            finally
            {
                rotated?.Dispose();
            }
        }

        private void UpdateStraightenToolbarState()
        {
            bool valid = lib.ImageStraightener.IsValidPerspectiveQuad(straightenCorners);
            if (straightenHintLabel != null)
            {
                straightenHintLabel.Text = straightenCorners == null
                    ? "Drag a rectangle around the area to straighten"
                    : valid
                        ? "Adjust the four corners, then Apply (Enter) to crop and straighten"
                        : "Corners must form a rectangle or convex quadrilateral";
            }
            if (straightenApplyButton != null)
            {
                straightenApplyButton.Enabled = valid && !isStraightenDragging;
            }
        }

        internal void DrawStraightenOverlay(Graphics g)
        {
            if (!isStraightenToolActive || straightenCorners == null) return;
            var points = Array.ConvertAll(straightenCorners, PixelToFormCoord);
            var color = lib.ImageStraightener.IsValidPerspectiveQuad(straightenCorners)
                ? Color.Yellow : Color.OrangeRed;
            using var shadowPen = new Pen(Color.FromArgb(160, Color.Black), 4f);
            using var linePen = new Pen(color, 2f);
            using var gridPen = new Pen(Color.FromArgb(140, color), 1f);
            using var dotBrush = new SolidBrush(color);
            using var shadowBrush = new SolidBrush(Color.FromArgb(160, Color.Black));

            g.DrawPolygon(shadowPen, points);
            g.DrawPolygon(linePen, points);
            for (int i = 1; i < 3; i++)
            {
                float t = i / 3f;
                g.DrawLine(gridPen, Interpolate(points[0], points[3], t), Interpolate(points[1], points[2], t));
                g.DrawLine(gridPen, Interpolate(points[0], points[1], t), Interpolate(points[3], points[2], t));
            }
            const int r = 5;
            foreach (var point in points)
            {
                g.FillEllipse(shadowBrush, point.X - r - 1, point.Y - r - 1, (r + 1) * 2, (r + 1) * 2);
                g.FillEllipse(dotBrush, point.X - r, point.Y - r, r * 2, r * 2);
            }

            static PointF Interpolate(Point a, Point b, float t)
                => new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        private void straightenApplyButton_Click(object sender, EventArgs e)
        {
            DeactivateStraightenTool(true);
            pictureBox1?.Focus();
        }

        private void straightenCancelButton_Click(object sender, EventArgs e)
        {
            DeactivateStraightenTool(false);
            pictureBox1?.Focus();
        }
    }
}
