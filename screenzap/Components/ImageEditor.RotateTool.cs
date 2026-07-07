using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ImageEditor
    {
        // isFreeRotateToolActive lives on ImageEditor.Tool.cs as a computed accessor.
        private float freeRotateAngleDeg;
        private bool isFreeRotateDragging;
        private Point freeRotateDragOriginPixel;
        private float freeRotateDragStartAngleDeg;
        private Rectangle freeRotateTargetBounds;

        // Screen-space dimensions of the drag handle, constant regardless of zoom — mirrors
        // the image layer rotate handle's LayerHandleScreenSize/LayerRotationHandleScreenOffset.
        private const float FreeRotateHandleScreenOffset = 32f;
        private const float FreeRotateHandleScreenSize = 10f;

        internal bool ActivateFreeRotateTool()
        {
            if (!HasEditableImage)
            {
                return false;
            }

            if (isFreeRotateToolActive)
            {
                // Already engaged: a second toolbar click must not discard the in-progress angle.
                return true;
            }

            isFreeRotateToolActive = true;
            freeRotateAngleDeg = 0f;
            isFreeRotateDragging = false;
            freeRotateTargetBounds = Selection.IsEmpty ? GetImageBounds() : ClampToImage(Selection);

            Cursor = Cursors.Default;

            if (freeRotateToolStripButton != null)
            {
                freeRotateToolStripButton.Checked = true;
            }

            if (rotateToolStrip != null)
            {
                rotateToolStrip.Visible = true;
                PositionOverlayToolStrips();
            }

            UpdateRotateToolbarState();
            UpdateCommandUI();
            pictureBox1.Invalidate();
            return true;
        }

        internal void DeactivateFreeRotateTool(bool apply)
        {
            if (!isFreeRotateToolActive)
            {
                return;
            }

            if (apply)
            {
                ApplyFreeRotate();
            }

            isFreeRotateToolActive = false;
            isFreeRotateDragging = false;
            freeRotateAngleDeg = 0f;
            Cursor = Cursors.Default;

            if (freeRotateToolStripButton != null)
            {
                freeRotateToolStripButton.Checked = false;
            }

            if (rotateToolStrip != null)
            {
                rotateToolStrip.Visible = false;
                PositionOverlayToolStrips();
            }

            UpdateCommandUI();
            pictureBox1.Invalidate();
        }

        private void ApplyFreeRotate() => RotateEditorContentBy(freeRotateAngleDeg);

        private Rectangle FreeRotateBounds => freeRotateTargetBounds;

        private PointF FreeRotateCenterPixel => new PointF(
            freeRotateTargetBounds.X + freeRotateTargetBounds.Width / 2f,
            freeRotateTargetBounds.Y + freeRotateTargetBounds.Height / 2f);

        /// <summary>
        /// The handle's current image-pixel position: it sits above the target's top-center at
        /// rest and spins with the target, just like the image layer rotation handle.
        /// </summary>
        private Point GetFreeRotateHandleImagePoint()
        {
            float zoom = pictureBox1 != null ? (float)pictureBox1.ZoomLevel : 1f;
            if (zoom <= 0f) zoom = 1f;

            var bounds = freeRotateTargetBounds;
            var restPoint = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top - FreeRotateHandleScreenOffset / zoom);
            return Point.Round(RotatePointAroundCenter(restPoint, bounds, freeRotateAngleDeg));
        }

        private bool HitTestFreeRotateHandle(Point pixelPoint)
        {
            if (freeRotateTargetBounds.IsEmpty)
            {
                return false;
            }

            float zoom = pictureBox1 != null ? (float)pictureBox1.ZoomLevel : 1f;
            if (zoom <= 0f) zoom = 1f;

            float tol = (FreeRotateHandleScreenSize / 2f) * 1.5f / zoom;
            return IsNearF(pixelPoint, GetFreeRotateHandleImagePoint(), tol);
        }

        private void BeginFreeRotateDrag(Point pixelPoint)
        {
            isFreeRotateDragging = true;
            freeRotateDragOriginPixel = pixelPoint;
            freeRotateDragStartAngleDeg = freeRotateAngleDeg;
            Cursor = Cursors.Cross;
        }

        private void UpdateFreeRotateDrag(Point pixelPoint)
        {
            var center = FreeRotateCenterPixel;
            double startAngle = Math.Atan2(freeRotateDragOriginPixel.Y - center.Y, freeRotateDragOriginPixel.X - center.X) * 180.0 / Math.PI;
            double currentAngle = Math.Atan2(pixelPoint.Y - center.Y, pixelPoint.X - center.X) * 180.0 / Math.PI;
            float newAngle = NormalizeLayerAngle(freeRotateDragStartAngleDeg + (float)(currentAngle - startAngle));

            if (IsShiftModifierDown())
            {
                newAngle = (float)(Math.Round(newAngle / 15.0) * 15.0);
            }

            if (newAngle != freeRotateAngleDeg)
            {
                freeRotateAngleDeg = newAngle;
                UpdateRotateToolbarState();
                pictureBox1?.Invalidate();
            }
        }

        private void EndFreeRotateDrag()
        {
            isFreeRotateDragging = false;
            Cursor = Cursors.Default;
        }

        private void UpdateRotateToolbarState()
        {
            if (rotateHintLabel != null)
            {
                rotateHintLabel.Text = Math.Abs(freeRotateAngleDeg) < 0.05f
                    ? "Drag the handle to rotate (hold Shift for 15° steps), then Apply"
                    : $"{freeRotateAngleDeg:F1}°";
            }

            if (rotateApplyButton != null)
            {
                rotateApplyButton.Enabled = Math.Abs(freeRotateAngleDeg) >= 0.05f;
            }
        }

        /// <summary>
        /// Draws the interactive rotate handle + wireframe bounds, plus a live raster preview of
        /// the rotated content, when the free-rotate tool is engaged. Call from pictureBox1_Paint.
        /// </summary>
        internal void DrawFreeRotateOverlay(Graphics g)
        {
            if (!isFreeRotateToolActive || freeRotateTargetBounds.IsEmpty || pictureBox1?.Image == null)
            {
                return;
            }

            float zoom = (float)pictureBox1.ZoomLevel;
            if (zoom <= 0f) zoom = 1f;
            PointF pan = pictureBox1.Metrics.PanOffset;

            var bounds = freeRotateTargetBounds;
            float cx = pan.X + (bounds.X + bounds.Width / 2f) * zoom;
            float cy = pan.Y + (bounds.Y + bounds.Height / 2f) * zoom;
            float hw = bounds.Width * zoom / 2f;
            float hh = bounds.Height * zoom / 2f;

            DrawFreeRotatePreviewContent(g, cx, cy, hw, hh);

            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(freeRotateAngleDeg);

            using (var pen = new Pen(Color.DodgerBlue, 1.25f) { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(pen, -hw, -hh, hw * 2f, hh * 2f);
            }

            float stemEndY = -hh - FreeRotateHandleScreenOffset;
            using (var stemPen = new Pen(Color.DodgerBlue, 1f))
            {
                g.DrawLine(stemPen, 0f, -hh, 0f, stemEndY);
            }

            DrawRotationHandle(g, 0f, stemEndY);

            g.Restore(state);
        }

        /// <summary>
        /// Fast GDI+ preview of the rotated pixels (not the final quality bake — that happens on
        /// Apply via <see cref="lib.ImageStraightener.RotateImage"/>). Whole-image rotate erases
        /// the pre-rotation footprint so the corners the rotated image no longer covers show the
        /// canvas backdrop; in-place selection rotate clips to the original marquee footprint so
        /// the untouched image shows through the corners instead — exactly what Apply bakes.
        /// </summary>
        private void DrawFreeRotatePreviewContent(Graphics g, float cx, float cy, float hw, float hh)
        {
            var footprint = RectangleF.FromLTRB(cx - hw, cy - hh, cx + hw, cy + hh);

            Region? savedClip = null;
            if (Selection.IsEmpty)
            {
                using var backdrop = new SolidBrush(pictureBox1!.BackColor);
                g.FillRectangle(backdrop, footprint);
            }
            else
            {
                savedClip = g.Clip;
                g.SetClip(footprint);
            }

            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(freeRotateAngleDeg);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(pictureBox1!.Image!, new RectangleF(-hw, -hh, hw * 2f, hh * 2f), freeRotateTargetBounds, GraphicsUnit.Pixel);
            g.Restore(state);

            if (savedClip != null)
            {
                g.Clip = savedClip;
                savedClip.Dispose();
            }
        }

        private void freeRotateToolStripButton_Click(object sender, EventArgs e)
        {
            ActivateFreeRotateTool();
            pictureBox1?.Focus();
        }

        private void rotateApplyButton_Click(object sender, EventArgs e)
        {
            DeactivateFreeRotateTool(true);
            pictureBox1?.Focus();
        }

        private void rotateCancelButton_Click(object sender, EventArgs e)
        {
            DeactivateFreeRotateTool(false);
            pictureBox1?.Focus();
        }

        internal bool TestIsFreeRotateToolActive => isFreeRotateToolActive;
        internal float TestFreeRotateAngleDeg => freeRotateAngleDeg;
        internal bool TestFreeRotateButtonChecked => freeRotateToolStripButton?.Checked == true;
        internal Point TestFreeRotateHandleImagePoint => GetFreeRotateHandleImagePoint();
        internal void TestClickFreeRotateToolButton() => freeRotateToolStripButton_Click(this, EventArgs.Empty);
    }
}
