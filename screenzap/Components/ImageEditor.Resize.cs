using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;
using FontAwesome.Sharp;

namespace screenzap
{
    public partial class ImageEditor
    {
        private IconToolStripButton? resizeImageToolStripButton;

        internal bool ResizeImageCommandAvailableForTests => resizeImageToolStripButton != null;

        internal bool ExecuteResizeImageForDiagnostics(
            Size targetSize,
            InterpolationMode interpolationMode = InterpolationMode.HighQualityBicubic)
        {
            return ExecuteResizeImage(targetSize, interpolationMode);
        }

        private void InitializeResizeImageCommand()
        {
            if (mainToolStrip == null || resizeImageToolStripButton != null)
            {
                return;
            }

            resizeImageToolStripButton = new IconToolStripButton
            {
                Name = "resizeImageToolStripButton",
                Text = "Resize Image…",
                ToolTipText = "Set the image size by scaling the full canvas",
                Enabled = false
            };
            ConfigureIconButton(resizeImageToolStripButton, IconChar.Expand);
            resizeImageToolStripButton.Click += (_, _) => ShowResizeImageDialog();

            int insertIndex = mainToolStrip.Items.IndexOf(expandCanvasToolStripButton);
            mainToolStrip.Items.Insert(
                insertIndex >= 0 ? insertIndex : mainToolStrip.Items.Count,
                resizeImageToolStripButton);
        }

        private void UpdateResizeImageCommandState(bool enabled)
        {
            if (resizeImageToolStripButton != null)
            {
                resizeImageToolStripButton.Enabled = enabled;
            }
        }

        private void ShowResizeImageDialog()
        {
            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return;
            }

            using var dialog = new ResizeImageDialog(pictureBox1.Image.Size);
            if (dialog.ShowDialog(this) == DialogResult.OK
                && ExecuteResizeImage(dialog.TargetSize, dialog.SelectedInterpolationMode))
            {
                pictureBox1.Focus();
            }
        }

        private bool ExecuteResizeImage(Size targetSize, InterpolationMode interpolationMode)
        {
            if (!HasEditableImage
                || pictureBox1.Image == null
                || targetSize.Width < 1
                || targetSize.Height < 1
                || targetSize == pictureBox1.Image.Size)
            {
                return false;
            }

            FinalizeActiveTextAnnotation();

            var sourceImage = pictureBox1.Image;
            var sourceSize = sourceImage.Size;
            float scaleX = (float)targetSize.Width / sourceSize.Width;
            float scaleY = (float)targetSize.Height / sourceSize.Height;
            float contentScale = (float)Math.Sqrt(scaleX * scaleY);

            var beforeImage = new Bitmap(sourceImage);
            var selectionBefore = Selection;
            var shapesBefore = CloneAnnotations();
            var textsBefore = CloneTextAnnotations();
            var layersBefore = CloneLayers();

            var afterImage = new Bitmap(
                targetSize.Width,
                targetSize.Height,
                PixelFormat.Format32bppArgb);
            CopyImageResolution(sourceImage, afterImage);

            using (var graphics = Graphics.FromImage(afterImage))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = interpolationMode;
                graphics.PixelOffsetMode = interpolationMode == InterpolationMode.NearestNeighbor
                    ? PixelOffsetMode.Half
                    : PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(
                    sourceImage,
                    new Rectangle(Point.Empty, targetSize),
                    0,
                    0,
                    sourceSize.Width,
                    sourceSize.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }

            pictureBox1.Image?.Dispose();
            pictureBox1.Image = new Bitmap(afterImage);
            ScaleEditableContent(scaleX, scaleY, contentScale, targetSize);
            isPlaceholderImage = false;

            var selectionAfter = Selection;
            var shapesAfter = CloneAnnotations();
            var textsAfter = CloneTextAnnotations();
            var layersAfter = CloneLayers();

            PushUndoStep(
                Rectangle.Empty,
                beforeImage,
                afterImage,
                selectionBefore,
                selectionAfter,
                replacesImage: true,
                shapesBefore: shapesBefore,
                shapesAfter: shapesAfter,
                textsBefore: textsBefore,
                textsAfter: textsAfter,
                layersBefore: layersBefore,
                layersAfter: layersAfter);

            RecenterViewportAfterImageChange(resizeWindow: true);
            UpdateLayerToolbarState();
            UpdateAnnotationToolbarFromSelection();
            if (selectedTextAnnotation != null)
            {
                SyncTextToolbarFromAnnotation(selectedTextAnnotation);
            }
            UpdateCommandUI();
            UpdateStatusBar();
            pictureBox1.Invalidate();
            return true;
        }

        private void ScaleEditableContent(
            float scaleX,
            float scaleY,
            float contentScale,
            Size targetSize)
        {
            Selection = ScaleRectangle(Selection, scaleX, scaleY);
            Selection = Rectangle.Intersect(
                new Rectangle(Point.Empty, targetSize),
                Selection);

            foreach (var shape in annotationShapes)
            {
                shape.Start = ScalePoint(shape.Start, scaleX, scaleY);
                shape.End = ScalePoint(shape.End, scaleX, scaleY);
                shape.LineThickness = Math.Max(0.1f, shape.LineThickness * contentScale);

                if (shape.Points != null)
                {
                    for (int index = 0; index < shape.Points.Count; index++)
                    {
                        shape.Points[index] = ScalePoint(shape.Points[index], scaleX, scaleY);
                    }

                    if (shape.Points.Count > 0)
                    {
                        shape.Start = shape.Points[0];
                        shape.End = shape.Points[shape.Points.Count - 1];
                    }
                }
            }

            foreach (var annotation in textAnnotations)
            {
                annotation.Position = ScalePoint(annotation.Position, scaleX, scaleY);
                annotation.FontSize = Math.Max(0.1f, annotation.FontSize * contentScale);
                annotation.OutlineThickness = Math.Max(
                    0f,
                    annotation.OutlineThickness * contentScale);
            }

            foreach (var layer in imageLayers)
            {
                var frame = layer.Frame;
                layer.Frame = new RectangleF(
                    frame.X * scaleX,
                    frame.Y * scaleY,
                    frame.Width * scaleX,
                    frame.Height * scaleY);
            }

            SyncSelectedAnnotation();
            SyncSelectedTextAnnotation();
        }

        private static Point ScalePoint(Point point, float scaleX, float scaleY)
        {
            return new Point(
                (int)Math.Round(point.X * scaleX, MidpointRounding.AwayFromZero),
                (int)Math.Round(point.Y * scaleY, MidpointRounding.AwayFromZero));
        }

        private static Rectangle ScaleRectangle(
            Rectangle rectangle,
            float scaleX,
            float scaleY)
        {
            if (rectangle.IsEmpty)
            {
                return Rectangle.Empty;
            }

            int left = (int)Math.Round(rectangle.Left * scaleX, MidpointRounding.AwayFromZero);
            int top = (int)Math.Round(rectangle.Top * scaleY, MidpointRounding.AwayFromZero);
            int right = (int)Math.Round(rectangle.Right * scaleX, MidpointRounding.AwayFromZero);
            int bottom = (int)Math.Round(rectangle.Bottom * scaleY, MidpointRounding.AwayFromZero);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static void CopyImageResolution(Image source, Bitmap target)
        {
            if (source.HorizontalResolution <= 0f || source.VerticalResolution <= 0f)
            {
                return;
            }

            try
            {
                target.SetResolution(source.HorizontalResolution, source.VerticalResolution);
            }
            catch (ArgumentException)
            {
                // Some clipboard providers publish invalid DPI metadata. Pixel dimensions
                // are authoritative for this operation, so keep the bitmap default DPI.
            }
        }
    }

    internal sealed class ResizeImageDialog : Form
    {
        private const decimal MaximumDimension = 65535m;

        private readonly NumericUpDown widthInput;
        private readonly NumericUpDown heightInput;
        private readonly CheckBox constrainProportions;
        private readonly ComboBox interpolationInput;
        private readonly decimal aspectRatio;
        private bool syncingDimensions;
        private bool widthWasEditedLast = true;

        public ResizeImageDialog(Size currentSize)
        {
            Text = "Resize Image";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(12);

            aspectRatio = currentSize.Height > 0
                ? (decimal)currentSize.Width / currentSize.Height
                : 1m;

            widthInput = CreateDimensionInput(currentSize.Width);
            heightInput = CreateDimensionInput(currentSize.Height);
            constrainProportions = new CheckBox
            {
                Text = "Constrain proportions",
                Checked = true,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            interpolationInput = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 180,
                Anchor = AnchorStyles.Left
            };
            interpolationInput.Items.AddRange(new object[]
            {
                "Nearest neighbor",
                "Bilinear",
                "Bicubic"
            });
            interpolationInput.SelectedIndex = 2;

            widthInput.Enter += (_, _) => widthWasEditedLast = true;
            heightInput.Enter += (_, _) => widthWasEditedLast = false;
            widthInput.TextChanged += (_, _) => HandleDimensionEdited(widthChanged: true);
            heightInput.TextChanged += (_, _) => HandleDimensionEdited(widthChanged: false);
            widthInput.ValueChanged += (_, _) => HandleDimensionEdited(widthChanged: true);
            heightInput.ValueChanged += (_, _) => HandleDimensionEdited(widthChanged: false);
            constrainProportions.CheckedChanged += (_, _) =>
            {
                if (constrainProportions.Checked)
                {
                    ReconcileConstrainedDimensions();
                }
            };

            var okButton = new Button
            {
                Text = "Resize",
                DialogResult = DialogResult.OK,
                AutoSize = true
            };
            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true
            };
            AcceptButton = okButton;
            CancelButton = cancelButton;

            var buttonRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = new Padding(0, 10, 0, 0)
            };
            buttonRow.Controls.Add(cancelButton);
            buttonRow.Controls.Add(okButton);

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 6,
                Dock = DockStyle.Fill
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var currentSizeLabel = new Label
            {
                Text = $"Current size: {currentSize.Width.ToString("N0", CultureInfo.CurrentCulture)} × {currentSize.Height.ToString("N0", CultureInfo.CurrentCulture)} px",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            layout.Controls.Add(currentSizeLabel, 0, 0);
            layout.SetColumnSpan(currentSizeLabel, 2);
            AddLabeledControl(layout, "Width:", widthInput, 1);
            AddLabeledControl(layout, "Height:", heightInput, 2);
            layout.Controls.Add(constrainProportions, 1, 3);
            AddLabeledControl(layout, "Resampling:", interpolationInput, 4);
            layout.Controls.Add(buttonRow, 0, 5);
            layout.SetColumnSpan(buttonRow, 2);
            Controls.Add(layout);

            Shown += (_, _) =>
            {
                widthInput.Select(0, widthInput.Text.Length);
                widthInput.Focus();
            };
        }

        public Size TargetSize => new Size(
            decimal.ToInt32(ReadDisplayedDimension(widthInput)),
            decimal.ToInt32(ReadDisplayedDimension(heightInput)));

        internal decimal WidthValueForTests => ReadDisplayedDimension(widthInput);

        internal decimal HeightValueForTests => ReadDisplayedDimension(heightInput);

        internal bool ConstrainProportionsForTests
        {
            get => constrainProportions.Checked;
            set => constrainProportions.Checked = value;
        }

        internal void SetWidthTextForTests(string text) => widthInput.Text = text;

        internal void SetHeightTextForTests(string text) => heightInput.Text = text;

        public InterpolationMode SelectedInterpolationMode =>
            interpolationInput.SelectedIndex switch
            {
                0 => InterpolationMode.NearestNeighbor,
                1 => InterpolationMode.HighQualityBilinear,
                _ => InterpolationMode.HighQualityBicubic
            };

        private static NumericUpDown CreateDimensionInput(int value)
        {
            return new NumericUpDown
            {
                Minimum = 1m,
                Maximum = MaximumDimension,
                Value = Math.Clamp(value, 1, decimal.ToInt32(MaximumDimension)),
                DecimalPlaces = 0,
                ThousandsSeparator = true,
                Width = 120,
                Anchor = AnchorStyles.Left
            };
        }

        private void SetDimension(NumericUpDown input, decimal value)
        {
            syncingDimensions = true;
            try
            {
                input.Value = Math.Clamp(
                    decimal.Round(value, 0, MidpointRounding.AwayFromZero),
                    input.Minimum,
                    input.Maximum);
            }
            finally
            {
                syncingDimensions = false;
            }
        }

        private void HandleDimensionEdited(bool widthChanged)
        {
            if (syncingDimensions)
            {
                return;
            }

            widthWasEditedLast = widthChanged;
            if (!constrainProportions.Checked)
            {
                return;
            }

            var source = widthChanged ? widthInput : heightInput;
            if (!TryReadDisplayedDimension(source, out decimal value))
            {
                return;
            }

            if (widthChanged)
            {
                SetDimension(heightInput, value / aspectRatio);
            }
            else
            {
                SetDimension(widthInput, value * aspectRatio);
            }
        }

        private void ReconcileConstrainedDimensions()
        {
            var source = widthWasEditedLast ? widthInput : heightInput;
            if (!TryReadDisplayedDimension(source, out decimal value))
            {
                return;
            }

            if (widthWasEditedLast)
            {
                SetDimension(heightInput, value / aspectRatio);
            }
            else
            {
                SetDimension(widthInput, value * aspectRatio);
            }
        }

        private static decimal ReadDisplayedDimension(NumericUpDown input)
        {
            return TryReadDisplayedDimension(input, out decimal value)
                ? value
                : input.Value;
        }

        private static bool TryReadDisplayedDimension(
            NumericUpDown input,
            out decimal value)
        {
            return decimal.TryParse(
                    input.Text,
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out value)
                && value >= input.Minimum
                && value <= input.Maximum;
        }

        private static void AddLabeledControl(
            TableLayoutPanel layout,
            string labelText,
            Control control,
            int row)
        {
            layout.Controls.Add(
                new Label
                {
                    Text = labelText,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 4, 10, 4)
                },
                0,
                row);
            control.Margin = new Padding(0, 4, 0, 4);
            layout.Controls.Add(control, 1, row);
        }
    }
}
