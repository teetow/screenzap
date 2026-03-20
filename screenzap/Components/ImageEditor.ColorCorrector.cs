using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using FontAwesome.Sharp;

namespace screenzap
{
    public partial class ImageEditor
    {
        private IconToolStripButton? colorCorrectToolStripButton;
        private Form? colorCorrectorForm;
        private Bitmap? originalColorCorrectImage;
        private Bitmap? livePreviewBuffer;

        private TrackBar? tbExposure;
        private TrackBar? tbContrast;
        private TrackBar? tbSaturation;
        private TrackBar? tbGamma;

        private void InitColorCorrector()
        {
            colorCorrectToolStripButton = new IconToolStripButton
            {
                Name = "colorCorrectToolStripButton",
                Text = "Color Correct",
                ToolTipText = "Adjust exposure, contrast, saturation, gamma",
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            colorCorrectToolStripButton.Click += (s, e) => ShowColorCorrector();
            ConfigureIconButton(colorCorrectToolStripButton, IconChar.Palette);

            mainToolStrip.Items.Add(colorCorrectToolStripButton);
        }

        private void ShowColorCorrector()
        {
            if (pictureBox1.Image == null) return;

            if (colorCorrectorForm != null && !colorCorrectorForm.IsDisposed)
            {
                colorCorrectorForm.BringToFront();
                return;
            }

            var selectionBefore = Selection;
            originalColorCorrectImage = (Bitmap)pictureBox1.Image.Clone();
            livePreviewBuffer = new Bitmap(originalColorCorrectImage.Width, originalColorCorrectImage.Height);

            colorCorrectorForm = new Form
            {
                Text = "Color Correction",
                Size = new Size(350, 360),
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                StartPosition = FormStartPosition.CenterParent,
                TopMost = true,
                ShowInTaskbar = false,
                MaximizeBox = false,
                MinimizeBox = false
            };

            int y = 10;
            
            // Exposure
            colorCorrectorForm.Controls.Add(new Label { Text = "Exposure", Location = new Point(10, y + 5), AutoSize = true });
            tbExposure = new TrackBar { Location = new Point(80, y), Width = 230, Minimum = -30, Maximum = 30, Value = 0, TickFrequency = 10 };
            tbExposure.Scroll += (s, e) => UpdateColorCorrectionPreview();
            colorCorrectorForm.Controls.Add(tbExposure);
            y += 45;

            // Contrast
            colorCorrectorForm.Controls.Add(new Label { Text = "Contrast", Location = new Point(10, y + 5), AutoSize = true });
            tbContrast = new TrackBar { Location = new Point(80, y), Width = 230, Minimum = -100, Maximum = 100, Value = 0, TickFrequency = 25 };
            tbContrast.Scroll += (s, e) => UpdateColorCorrectionPreview();
            colorCorrectorForm.Controls.Add(tbContrast);
            y += 45;

            // Saturation
            colorCorrectorForm.Controls.Add(new Label { Text = "Saturation", Location = new Point(10, y + 5), AutoSize = true });
            tbSaturation = new TrackBar { Location = new Point(80, y), Width = 230, Minimum = 0, Maximum = 200, Value = 100, TickFrequency = 25 };
            tbSaturation.Scroll += (s, e) => UpdateColorCorrectionPreview();
            colorCorrectorForm.Controls.Add(tbSaturation);
            y += 45;

            // Gamma
            colorCorrectorForm.Controls.Add(new Label { Text = "Gamma", Location = new Point(10, y + 5), AutoSize = true });
            tbGamma = new TrackBar { Location = new Point(80, y), Width = 230, Minimum = 10, Maximum = 300, Value = 100, TickFrequency = 50 };
            tbGamma.Scroll += (s, e) => UpdateColorCorrectionPreview();
            colorCorrectorForm.Controls.Add(tbGamma);
            y += 40;

            // Buttons
            var btnApply = new Button { Text = "Apply", Location = new Point(150, y), Width = 75 };
            var btnCancel = new Button { Text = "Cancel", Location = new Point(235, y), Width = 75 };

            btnApply.Click += (s, e) =>
            {
                ApplyFinalColorCorrection();
                PushUndoStep(Rectangle.Empty, originalColorCorrectImage, (Bitmap)pictureBox1.Image.Clone(), selectionBefore, Selection, true);
                
                colorCorrectorForm?.Close();
            };

            btnCancel.Click += (s, e) =>
            {
                pictureBox1.Image = originalColorCorrectImage;
                pictureBox1.Invalidate();
                colorCorrectorForm?.Close();
            };

            colorCorrectorForm.Controls.Add(btnApply);
            colorCorrectorForm.Controls.Add(btnCancel);

            colorCorrectorForm.FormClosed += (s, e) =>
            {
                if (pictureBox1.Image == livePreviewBuffer)
                {
                   // Restoring on abrupt close
                   pictureBox1.Image = originalColorCorrectImage;
                   pictureBox1.Invalidate();
                }
                livePreviewBuffer?.Dispose();
                livePreviewBuffer = null;
                colorCorrectorForm = null;
            };

            colorCorrectorForm.Show(this);
        }

        private void UpdateColorCorrectionPreview()
        {
            if (originalColorCorrectImage == null || livePreviewBuffer == null) return;

            RenderColorCorrectionToBuffer();
            pictureBox1.Image = livePreviewBuffer;
            pictureBox1.Invalidate();
        }

        private void ApplyFinalColorCorrection()
        {
            if (livePreviewBuffer == null) return;
            RenderColorCorrectionToBuffer();
            pictureBox1.Image = (Bitmap)livePreviewBuffer.Clone();
            pictureBox1.Invalidate();
            hasUnsavedChanges = true;
            UpdateCommandUI();
        }

        private float[][] MultiplyMatrix(float[][] a, float[][] b)
        {
            float[][] result = new float[5][];
            for (int i = 0; i < 5; i++)
            {
                result[i] = new float[5];
                for (int j = 0; j < 5; j++)
                {
                    float sum = 0;
                    for (int k = 0; k < 5; k++)
                    {
                        sum += a[i][k] * b[k][j];
                    }
                    result[i][j] = sum;
                }
            }
            return result;
        }

        private void RenderColorCorrectionToBuffer()
        {
            if (tbExposure == null || tbContrast == null || tbSaturation == null || tbGamma == null || originalColorCorrectImage == null || livePreviewBuffer == null) return;

            float exposure = (float)Math.Pow(2, tbExposure.Value / 10.0f);
            float contrast = tbContrast.Value / 100.0f;
            float saturation = tbSaturation.Value / 100.0f;
            float gamma = 100.0f / tbGamma.Value;

            if (gamma <= 0) gamma = 0.01f;

            float[][] finalMat = new float[][] {
                new float[] { 1, 0, 0, 0, 0 },
                new float[] { 0, 1, 0, 0, 0 },
                new float[] { 0, 0, 1, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            };

            float s = saturation;
            float rw = 0.3086f, gw = 0.6094f, bw = 0.0820f;
            float[][] satMat = new float[][] {
                new float[] { (1 - s) * rw + s, (1 - s) * rw,     (1 - s) * rw,     0, 0 },
                new float[] { (1 - s) * gw,     (1 - s) * gw + s, (1 - s) * gw,     0, 0 },
                new float[] { (1 - s) * bw,     (1 - s) * bw,     (1 - s) * bw + s, 0, 0 },
                new float[] { 0,                0,                0,                1, 0 },
                new float[] { 0,                0,                0,                0, 1 }
            };
            finalMat = MultiplyMatrix(finalMat, satMat);

            float c = contrast + 1.0f;
            float t = 0.5f * (1.0f - c);
            float[][] conMat = new float[][] {
                new float[] { c, 0, 0, 0, 0 },
                new float[] { 0, c, 0, 0, 0 },
                new float[] { 0, 0, c, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { t, t, t, 0, 1 }
            };
            finalMat = MultiplyMatrix(finalMat, conMat);

            float[][] expMat = new float[][] {
                new float[] { exposure, 0,        0,        0, 0 },
                new float[] { 0,        exposure, 0,        0, 0 },
                new float[] { 0,        0,        exposure, 0, 0 },
                new float[] { 0,        0,        0,        1, 0 },
                new float[] { 0,        0,        0,        0, 1 }
            };
            finalMat = MultiplyMatrix(finalMat, expMat);

            using (var g = Graphics.FromImage(livePreviewBuffer))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(new ColorMatrix(finalMat), ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                attributes.SetGamma(gamma, ColorAdjustType.Bitmap);

                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;

                // Always start from the original image and then apply correction only to the target area.
                g.DrawImage(
                    originalColorCorrectImage,
                    new Rectangle(0, 0, originalColorCorrectImage.Width, originalColorCorrectImage.Height),
                    0, 0, originalColorCorrectImage.Width, originalColorCorrectImage.Height,
                    GraphicsUnit.Pixel
                );

                var targetRegion = Selection.IsEmpty
                    ? new Rectangle(0, 0, originalColorCorrectImage.Width, originalColorCorrectImage.Height)
                    : ClampToImage(Selection);

                if (targetRegion.Width <= 0 || targetRegion.Height <= 0)
                {
                    return;
                }

                g.DrawImage(
                    originalColorCorrectImage,
                    targetRegion,
                    targetRegion.X,
                    targetRegion.Y,
                    targetRegion.Width,
                    targetRegion.Height,
                    GraphicsUnit.Pixel,
                    attributes
                );
            }
        }
    }
}