using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using screenzap.lib;

namespace screenzap
{
    internal sealed class TextAnnotation
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public Point Position { get; set; }
        public string Text { get; set; } = string.Empty;
        public string FontFamily { get; set; } = "Segoe UI";
        public float FontSize { get; set; } = 16f;
        public Color TextColor { get; set; } = Color.Red;
        public bool Selected { get; set; }
        public bool IsEditing { get; set; }

        public TextAnnotation Clone()
        {
            return new TextAnnotation
            {
                Id = Id,
                Position = Position,
                Text = Text,
                FontFamily = FontFamily,
                FontSize = FontSize,
                TextColor = TextColor,
                Selected = Selected,
                IsEditing = false
            };
        }

        public Rectangle GetBounds(Graphics graphics)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return new Rectangle(Position, new Size(100, (int)(FontSize * 1.5f)));
            }

            using var font = new Font(FontFamily, FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            var size = graphics.MeasureString(Text, font);
            return new Rectangle(Position, new Size((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)));
        }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Text);
        }
    }

    public partial class ImageEditor
    {
        private readonly List<TextAnnotation> textAnnotations = new List<TextAnnotation>();
        private bool isTextToolActive;
        private TextAnnotation? activeTextAnnotation;
        private TextAnnotation? selectedTextAnnotation;
        private Point textDragOriginPixel;
        private bool isTextAnnotationDragging;
        private List<TextAnnotation>? textAnnotationSnapshotBeforeEdit;
        private bool textAnnotationChangedDuringDrag;

        // Text tool settings
        private string textToolFontFamily = "Segoe UI";
        private float textToolFontSize = 24f;
        private Color textToolColor = Color.Red;

        private List<TextAnnotation> CloneTextAnnotations()
        {
            return textAnnotations.Select(t => t.Clone()).ToList();
        }

        private void SelectTextAnnotation(TextAnnotation? target)
        {
            foreach (var annotation in textAnnotations)
            {
                annotation.Selected = annotation == target;
                if (annotation != target)
                {
                    annotation.IsEditing = false;
                }
            }

            selectedTextAnnotation = target;
            pictureBox1?.Invalidate();
        }

        private void SyncSelectedTextAnnotation()
        {
            selectedTextAnnotation = textAnnotations.FirstOrDefault(t => t.Selected);
        }

        private void UpdateTextToolButtons()
        {
            bool enable = HasEditableImage;

            if (!enable)
            {
                isTextToolActive = false;
                CancelTextEditing();
            }

            if (textToolStripButton != null)
            {
                textToolStripButton.Enabled = enable;
                textToolStripButton.Checked = enable && isTextToolActive;
            }
        }

        private void ToggleTextTool()
        {
            if (!HasEditableImage)
            {
                return;
            }

            // Deactivate other tools
            if (activeDrawingTool != DrawingTool.None)
            {
                activeDrawingTool = DrawingTool.None;
                CancelAnnotationPreview();
                UpdateDrawingToolButtons();
            }

            isTextToolActive = !isTextToolActive;

            if (!isTextToolActive)
            {
                FinalizeActiveTextAnnotation();
            }

            UpdateTextToolButtons();
            UpdateTextToolbarVisibility();
        }

        private void UpdateTextToolbarVisibility()
        {
            if (textToolStrip != null)
            {
                textToolStrip.Visible = isTextToolActive;
            }
        }

        private void CancelTextEditing()
        {
            if (activeTextAnnotation != null)
            {
                if (!activeTextAnnotation.IsValid())
                {
                    textAnnotations.Remove(activeTextAnnotation);
                }
                activeTextAnnotation.IsEditing = false;
            }

            activeTextAnnotation = null;
            isTextAnnotationDragging = false;
            textAnnotationSnapshotBeforeEdit = null;
            textAnnotationChangedDuringDrag = false;
            pictureBox1?.Invalidate();
        }

        private void FinalizeActiveTextAnnotation()
        {
            if (activeTextAnnotation != null)
            {
                activeTextAnnotation.IsEditing = false;
                if (!activeTextAnnotation.IsValid())
                {
                    textAnnotations.Remove(activeTextAnnotation);
                }
                else
                {
                    CommitTextAnnotationUndo();
                }
            }

            activeTextAnnotation = null;
            pictureBox1?.Invalidate();
        }

        private void DrawTextAnnotations(Graphics graphics, AnnotationSurface surface)
        {
            if (textAnnotations.Count == 0)
            {
                return;
            }

            float scale = surface == AnnotationSurface.Screen ? (float)ZoomLevel : 1f;

            var previousTextRenderingHint = graphics.TextRenderingHint;
            var previousSmoothingMode = graphics.SmoothingMode;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            try
            {
                foreach (var annotation in textAnnotations)
                {
                    DrawTextAnnotation(graphics, annotation, surface, scale);
                    if (surface == AnnotationSurface.Screen && annotation.Selected)
                    {
                        DrawTextAnnotationHandles(graphics, annotation);
                    }
                }
            }
            finally
            {
                graphics.TextRenderingHint = previousTextRenderingHint;
                graphics.SmoothingMode = previousSmoothingMode;
            }
        }

        private void DrawTextAnnotation(Graphics graphics, TextAnnotation annotation, AnnotationSurface surface, float scale)
        {
            var text = annotation.Text;
            if (string.IsNullOrEmpty(text) && annotation.IsEditing)
            {
                text = "|"; // Show cursor
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            float fontSize = surface == AnnotationSurface.Screen ? annotation.FontSize * scale : annotation.FontSize;
            using var font = new Font(annotation.FontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(annotation.TextColor);

            PointF position;
            if (surface == AnnotationSurface.Screen)
            {
                position = PixelToFormCoordF(annotation.Position);
            }
            else
            {
                position = new PointF(annotation.Position.X, annotation.Position.Y);
            }

            // Draw outline for visibility
            if (surface == AnnotationSurface.Screen || true)
            {
                float outlineOffset = surface == AnnotationSurface.Screen ? 1f * scale : 1f;
                using var outlineBrush = new SolidBrush(GetContrastColor(annotation.TextColor));
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx != 0 || dy != 0)
                        {
                            graphics.DrawString(text, font, outlineBrush,
                                position.X + dx * outlineOffset,
                                position.Y + dy * outlineOffset);
                        }
                    }
                }
            }

            graphics.DrawString(text, font, brush, position);

            // Draw editing cursor
            if (annotation.IsEditing && surface == AnnotationSurface.Screen)
            {
                var textSize = graphics.MeasureString(annotation.Text, font);
                using var cursorPen = new Pen(annotation.TextColor, 2f);
                float cursorX = position.X + textSize.Width;
                float cursorHeight = fontSize;
                graphics.DrawLine(cursorPen, cursorX, position.Y, cursorX, position.Y + cursorHeight);
            }
        }

        private static Color GetContrastColor(Color color)
        {
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return luminance > 0.5 ? Color.Black : Color.White;
        }

        private void DrawTextAnnotationHandles(Graphics graphics, TextAnnotation annotation)
        {
            using var tempBitmap = new Bitmap(1, 1);
            using var tempGraphics = Graphics.FromImage(tempBitmap);
            var bounds = annotation.GetBounds(tempGraphics);
            var screenBounds = PixelToFormCoord(bounds);

            using var pen = new Pen(Color.Cyan, 1f);
            pen.DashStyle = DashStyle.Dash;
            graphics.DrawRectangle(pen, screenBounds);

            const int handleSize = 6;
            int half = handleSize / 2;

            // Move handle at top-left
            var handleRect = new Rectangle(screenBounds.X - half, screenBounds.Y - half, handleSize, handleSize);
            graphics.FillRectangle(Brushes.White, handleRect);
            graphics.DrawRectangle(Pens.Black, handleRect);
        }

        private TextAnnotation? HitTestTextAnnotation(Point pixelPoint, Point formPoint)
        {
            using var tempBitmap = new Bitmap(1, 1);
            using var tempGraphics = Graphics.FromImage(tempBitmap);

            for (int i = textAnnotations.Count - 1; i >= 0; i--)
            {
                var annotation = textAnnotations[i];
                var bounds = annotation.GetBounds(tempGraphics);
                var screenBounds = PixelToFormCoord(bounds);
                if (screenBounds.Contains(formPoint))
                {
                    return annotation;
                }
            }

            return null;
        }

        private bool HandleTextToolMouseDown(Point pixelPoint, Point formPoint)
        {
            if (!HasEditableImage)
            {
                return false;
            }

            // Check if clicking on existing text annotation (works even when tool isn't active)
            var hit = HitTestTextAnnotation(pixelPoint, formPoint);

            if (hit != null)
            {
                // Finalize previous annotation if different
                if (activeTextAnnotation != null && activeTextAnnotation != hit)
                {
                    FinalizeActiveTextAnnotation();
                }

                // Activate text tool if clicking on existing text
                if (!isTextToolActive)
                {
                    isTextToolActive = true;
                    UpdateTextToolButtons();
                    UpdateTextToolbarVisibility();
                    
                    // Deactivate other drawing tools
                    if (activeDrawingTool != DrawingTool.None)
                    {
                        activeDrawingTool = DrawingTool.None;
                        CancelAnnotationPreview();
                        UpdateDrawingToolButtons();
                    }
                }

                textAnnotationSnapshotBeforeEdit = CloneTextAnnotations();
                SelectTextAnnotation(hit);
                hit.IsEditing = true;
                activeTextAnnotation = hit;
                textDragOriginPixel = pixelPoint;
                isTextAnnotationDragging = true;
                textAnnotationChangedDuringDrag = false;
                pictureBox1?.Invalidate();
                return true;
            }

            // For creating new text, require tool to be active
            if (!isTextToolActive)
            {
                return false;
            }

            // Finalize previous annotation
            FinalizeActiveTextAnnotation();

            // Create new text annotation
            textAnnotationSnapshotBeforeEdit = CloneTextAnnotations();
            var clampedPoint = ClampPointToImage(pixelPoint);
            var newAnnotation = new TextAnnotation
            {
                Position = clampedPoint,
                FontFamily = textToolFontFamily,
                FontSize = textToolFontSize,
                TextColor = textToolColor,
                Text = string.Empty,
                Selected = true,
                IsEditing = true
            };

            textAnnotations.Add(newAnnotation);
            SelectTextAnnotation(newAnnotation);
            activeTextAnnotation = newAnnotation;
            textAnnotationChangedDuringDrag = false;
            pictureBox1?.Invalidate();
            return true;
        }

        private bool HandleTextToolMouseMove(Point pixelPoint, Point formPoint, MouseButtons buttons)
        {
            if (!HasEditableImage)
            {
                return false;
            }

            // Handle dragging when actively editing
            if (isTextToolActive && buttons == MouseButtons.Left && isTextAnnotationDragging && activeTextAnnotation != null)
            {
                var delta = pixelPoint.Subtract(textDragOriginPixel);
                if (delta.X != 0 || delta.Y != 0)
                {
                    var newPos = activeTextAnnotation.Position.Add(delta);
                    activeTextAnnotation.Position = ClampPointToImage(newPos);
                    textDragOriginPixel = pixelPoint;
                    textAnnotationChangedDuringDrag = true;
                    pictureBox1?.Invalidate();
                }
                return true;
            }

            // Show cursor when hovering over text (even when tool isn't active)
            if (buttons == MouseButtons.None && textAnnotations.Count > 0)
            {
                var hit = HitTestTextAnnotation(pixelPoint, formPoint);
                if (hit != null)
                {
                    Cursor = Cursors.IBeam;
                    return true;
                }
            }
            
            // Only set cross cursor when text tool is actively selected
            if (isTextToolActive && buttons == MouseButtons.None)
            {
                Cursor = Cursors.Cross;
            }

            return false;
        }

        private bool HandleTextToolMouseUp(MouseButtons button, Point releasePixel)
        {
            if (!HasEditableImage || button != MouseButtons.Left)
            {
                return false;
            }

            if (isTextAnnotationDragging)
            {
                isTextAnnotationDragging = false;
                if (textAnnotationChangedDuringDrag && activeTextAnnotation != null)
                {
                    // Movement will be committed when text is finalized
                }
                return true;
            }

            return false;
        }

        private bool HandleTextToolKeyDown(KeyEventArgs e)
        {
            if (!isTextToolActive || activeTextAnnotation == null || !activeTextAnnotation.IsEditing)
            {
                return false;
            }

            if (e.KeyCode == Keys.Escape)
            {
                // Cancel text entry - remove if empty
                if (!activeTextAnnotation.IsValid())
                {
                    textAnnotations.Remove(activeTextAnnotation);
                    textAnnotationSnapshotBeforeEdit = null;
                }
                else
                {
                    CommitTextAnnotationUndo();
                }

                activeTextAnnotation.IsEditing = false;
                activeTextAnnotation = null;
                pictureBox1?.Invalidate();
                e.Handled = true;
                return true;
            }

            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                FinalizeActiveTextAnnotation();
                e.Handled = true;
                return true;
            }

            if (e.KeyCode == Keys.Back)
            {
                if (activeTextAnnotation.Text.Length > 0)
                {
                    activeTextAnnotation.Text = activeTextAnnotation.Text.Substring(0, activeTextAnnotation.Text.Length - 1);
                    textAnnotationChangedDuringDrag = true;
                    pictureBox1?.Invalidate();
                }
                e.Handled = true;
                return true;
            }

            if (e.KeyCode == Keys.Delete)
            {
                // Delete entire text annotation
                textAnnotations.Remove(activeTextAnnotation);
                CommitTextAnnotationUndo();
                activeTextAnnotation = null;
                selectedTextAnnotation = null;
                pictureBox1?.Invalidate();
                e.Handled = true;
                return true;
            }

            return false;
        }

        private bool HandleTextToolKeyPress(KeyPressEventArgs e)
        {
            if (!isTextToolActive || activeTextAnnotation == null || !activeTextAnnotation.IsEditing)
            {
                return false;
            }

            // Filter control characters except for Enter/newline
            if (char.IsControl(e.KeyChar) && e.KeyChar != '\r' && e.KeyChar != '\n')
            {
                return false;
            }

            // Handle Enter for newline
            if (e.KeyChar == '\r' || e.KeyChar == '\n')
            {
                if (Control.ModifierKeys.HasFlag(Keys.Shift))
                {
                    activeTextAnnotation.Text += Environment.NewLine;
                    textAnnotationChangedDuringDrag = true;
                    pictureBox1?.Invalidate();
                    e.Handled = true;
                    return true;
                }
                return false;
            }

            activeTextAnnotation.Text += e.KeyChar;
            textAnnotationChangedDuringDrag = true;
            pictureBox1?.Invalidate();
            e.Handled = true;
            return true;
        }

        private void CommitTextAnnotationUndo()
        {
            if (textAnnotationSnapshotBeforeEdit == null)
            {
                return;
            }

            var afterState = CloneTextAnnotations();
            PushTextUndoStep(textAnnotationSnapshotBeforeEdit, afterState);
            textAnnotationSnapshotBeforeEdit = null;
        }

        private void PushTextUndoStep(List<TextAnnotation>? before, List<TextAnnotation>? after)
        {
            if (before == null || after == null)
            {
                return;
            }

            undoStack.Push(new TextAnnotationUndoStep(before, after));
            hasUnsavedChanges = true;
        }

        private void ApplyTextAnnotationState(List<TextAnnotation>? source)
        {
            if (source == null)
            {
                return;
            }

            textAnnotations.Clear();
            foreach (var annotation in source)
            {
                textAnnotations.Add(annotation.Clone());
            }

            SyncSelectedTextAnnotation();
        }

        private void ApplyCropToTextAnnotations(Point cropOrigin, Size newSize)
        {
            if (textAnnotations.Count == 0)
            {
                return;
            }

            var newBounds = new Rectangle(Point.Empty, newSize);
            var updated = new List<TextAnnotation>();

            foreach (var annotation in textAnnotations)
            {
                var clone = annotation.Clone();
                clone.Position = clone.Position.Subtract(cropOrigin);

                // Keep if position is within new bounds
                if (newBounds.Contains(clone.Position))
                {
                    updated.Add(clone);
                }
            }

            textAnnotations.Clear();
            textAnnotations.AddRange(updated);
            SyncSelectedTextAnnotation();
        }

        private void textToolStripButton_Click(object? sender, EventArgs e)
        {
            ToggleTextTool();
        }

        private void fontComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (fontComboBox?.SelectedItem is string fontName)
            {
                textToolFontFamily = fontName;
                if (activeTextAnnotation != null)
                {
                    activeTextAnnotation.FontFamily = fontName;
                    pictureBox1?.Invalidate();
                }
            }
        }

        private void fontSizeComboBox_TextChanged(object? sender, EventArgs e)
        {
            if (fontSizeComboBox != null && float.TryParse(fontSizeComboBox.Text, out float size) && size > 0 && size <= 200)
            {
                textToolFontSize = size;
                if (activeTextAnnotation != null)
                {
                    activeTextAnnotation.FontSize = size;
                    pictureBox1?.Invalidate();
                }
            }
        }

        private void textColorButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new ColorDialog();
            dialog.Color = textToolColor;
            dialog.FullOpen = true;

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                textToolColor = dialog.Color;
                UpdateTextColorButtonAppearance();
                if (activeTextAnnotation != null)
                {
                    activeTextAnnotation.TextColor = textToolColor;
                    pictureBox1?.Invalidate();
                }
            }
        }

        private void UpdateTextColorButtonAppearance()
        {
            if (textColorButton != null)
            {
                textColorButton.BackColor = textToolColor;
                textColorButton.ForeColor = GetContrastColor(textToolColor);
            }
        }

        private void InitializeTextToolbar()
        {
            if (fontComboBox != null)
            {
                var installedFonts = new InstalledFontCollection();
                foreach (var family in installedFonts.Families)
                {
                    fontComboBox.Items.Add(family.Name);
                }

                int defaultIndex = fontComboBox.Items.IndexOf(textToolFontFamily);
                if (defaultIndex >= 0)
                {
                    fontComboBox.SelectedIndex = defaultIndex;
                }
                else if (fontComboBox.Items.Count > 0)
                {
                    fontComboBox.SelectedIndex = 0;
                    textToolFontFamily = fontComboBox.Items[0]?.ToString() ?? "Segoe UI";
                }
            }

            if (fontSizeComboBox != null)
            {
                fontSizeComboBox.Items.AddRange(new object[] { "8", "10", "12", "14", "16", "18", "20", "24", "28", "32", "36", "48", "72" });
                fontSizeComboBox.Text = textToolFontSize.ToString();
            }

            UpdateTextColorButtonAppearance();
        }
    }

    internal sealed class TextAnnotationUndoStep : IUndoStep
    {
        public List<TextAnnotation> Before { get; }
        public List<TextAnnotation> After { get; }

        public TextAnnotationUndoStep(List<TextAnnotation> before, List<TextAnnotation> after)
        {
            Before = before.Select(t => t.Clone()).ToList();
            After = after.Select(t => t.Clone()).ToList();
        }

        public void Dispose()
        {
            // No unmanaged resources
        }
    }
}
