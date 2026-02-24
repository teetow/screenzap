using System;
using System.Drawing;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ImageEditor
    {
        private bool isStraightenToolActive;
        private Point? straightenLineStartPixel;
        private Point? straightenLineEndPixel;
        private bool isStraightenLineDragging;

        internal bool ActivateStraightenTool()
        {
            if (!HasEditableImage)
            {
                return false;
            }

            isStraightenToolActive = true;
            straightenLineStartPixel = null;
            straightenLineEndPixel = null;
            isStraightenLineDragging = false;

            Cursor = Cursors.Cross;

            if (straightenToolStrip != null)
            {
                straightenToolStrip.Visible = true;
            }

            ClearSelection();
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
                ApplyStraightenLine();
            }

            isStraightenToolActive = false;
            isStraightenLineDragging = false;
            straightenLineStartPixel = null;
            straightenLineEndPixel = null;
            Cursor = Cursors.Default;

            if (straightenToolStrip != null)
            {
                straightenToolStrip.Visible = false;
            }

            UpdateCommandUI();
            pictureBox1.Invalidate();
        }

        private void ApplyStraightenLine()
        {
            if (straightenLineStartPixel == null || straightenLineEndPixel == null)
            {
                return;
            }

            double correctionAngle = GetStraightenCorrectionAngle();
            if (Math.Abs(correctionAngle) < 0.01)
            {
                return;
            }

            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return;
            }

            var selectionBefore = Selection;
            var beforeFullImage = new Bitmap(pictureBox1.Image);
            Bitmap? rotated = null;

            try
            {
                rotated = lib.ImageStraightener.RotateImage(beforeFullImage, correctionAngle);

                pictureBox1.Image?.Dispose();
                pictureBox1.Image = new Bitmap(rotated);

                PushUndoStep(Rectangle.Empty, beforeFullImage, new Bitmap(rotated), selectionBefore, Rectangle.Empty, true);

                rotated.Dispose();

                hasUnsavedChanges = true;
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

        /// <summary>
        /// Returns the rotation angle (in degrees) needed to align the drawn reference line
        /// with its nearest axis — horizontal if |angle| ≤ 45°, vertical otherwise.
        /// Convention matches atan2(dy, dx) in image coords, matching OpenCV's WarpAffine.
        /// </summary>
        private double GetStraightenCorrectionAngle()
        {
            if (straightenLineStartPixel == null || straightenLineEndPixel == null)
            {
                return 0.0;
            }

            var p1 = straightenLineStartPixel.Value;
            var p2 = straightenLineEndPixel.Value;
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;

            if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
            {
                return 0.0;
            }

            double lineAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

            // Snap to nearest axis
            double targetAngle = Math.Abs(lineAngle) <= 45.0
                ? 0.0
                : Math.Sign(lineAngle) * 90.0;

            return lineAngle - targetAngle;
        }

        private void UpdateStraightenToolbarState()
        {
            bool hasLine = straightenLineStartPixel != null && straightenLineEndPixel != null;
            double correctionAngle = hasLine ? GetStraightenCorrectionAngle() : 0.0;
            bool canApply = hasLine && Math.Abs(correctionAngle) >= 0.05;

            if (straightenHintLabel != null)
            {
                if (!hasLine)
                {
                    straightenHintLabel.Text = "Draw a reference line that should be horizontal or vertical, then Apply";
                }
                else if (!canApply)
                {
                    straightenHintLabel.Text = "Already aligned — no correction needed";
                }
                else
                {
                    var p1 = straightenLineStartPixel!.Value;
                    var p2 = straightenLineEndPixel!.Value;
                    double lineAngle = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X) * 180.0 / Math.PI;
                    string axis = Math.Abs(lineAngle) <= 45.0 ? "horizontal" : "vertical";
                    straightenHintLabel.Text = $"{Math.Abs(correctionAngle):F1}° to {axis}";
                }
            }

            if (straightenApplyButton != null)
            {
                straightenApplyButton.Enabled = canApply;
            }
        }

        /// <summary>
        /// Draws the interactive reference line overlay when in straighten-tool mode.
        /// Call this from pictureBox1_Paint.
        /// </summary>
        internal void DrawStraightenOverlay(Graphics g)
        {
            if (!isStraightenToolActive || straightenLineStartPixel == null || straightenLineEndPixel == null)
            {
                return;
            }

            var p1 = PixelToFormCoord(straightenLineStartPixel.Value);
            var p2 = PixelToFormCoord(straightenLineEndPixel.Value);

            using var shadowPen = new Pen(System.Drawing.Color.FromArgb(160, System.Drawing.Color.Black), 4f);
            using var linePen = new Pen(System.Drawing.Color.Yellow, 2f);
            using var dotBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Yellow);
            using var shadowBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(160, System.Drawing.Color.Black));

            g.DrawLine(shadowPen, p1, p2);
            g.DrawLine(linePen, p1, p2);

            const int r = 5;
            foreach (var pt in new[] { p1, p2 })
            {
                g.FillEllipse(shadowBrush, pt.X - r - 1, pt.Y - r - 1, (r + 1) * 2, (r + 1) * 2);
                g.FillEllipse(dotBrush, pt.X - r, pt.Y - r, r * 2, r * 2);
            }
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
