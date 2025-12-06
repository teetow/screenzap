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
        public FontStyle FontStyle { get; set; } = FontStyle.Regular;
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
                FontStyle = FontStyle,
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

            using var font = new Font(FontFamily, FontSize, FontStyle, GraphicsUnit.Pixel);
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
        private TextAnnotation? hoveredTextAnnotation;
        private Point textDragOriginPixel;
        private bool isTextAnnotationDragging;
        private List<TextAnnotation>? textAnnotationSnapshotBeforeEdit;
        private bool textAnnotationChangedDuringDrag;

        // Text tool settings
        private string textToolFontFamily = "Segoe UI";
        private string? textToolFontVariant = null; // null means use base family
        private float textToolFontSize = 24f;
        private FontStyle textToolFontStyle = FontStyle.Regular;
        private Color textToolColor = Color.Red;

        // Font variant mapping: base name -> list of (display name, full font name)
        private Dictionary<string, List<(string DisplayName, string FullName)>>? fontVariantMap;

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
            if (textToolSeparator != null)
            {
                textToolSeparator.Visible = isTextToolActive;
            }
            if (fontComboBox != null)
            {
                fontComboBox.Visible = isTextToolActive;
            }
            if (fontVariantComboBox != null)
            {
                // Only show variant dropdown if there are variants for current font
                fontVariantComboBox.Visible = isTextToolActive && fontVariantComboBox.Items.Count > 1;
            }
            if (fontSizeComboBox != null)
            {
                fontSizeComboBox.Visible = isTextToolActive;
            }
            if (boldButton != null)
            {
                boldButton.Visible = isTextToolActive;
            }
            if (italicButton != null)
            {
                italicButton.Visible = isTextToolActive;
            }
            if (underlineButton != null)
            {
                underlineButton.Visible = isTextToolActive;
            }
            if (textColorButton != null)
            {
                textColorButton.Visible = isTextToolActive;
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
                    if (surface == AnnotationSurface.Screen)
                    {
                        // Draw hover hitbox for non-selected annotations
                        if (annotation == hoveredTextAnnotation && !annotation.Selected)
                        {
                            DrawTextAnnotationHoverHitbox(graphics, annotation);
                        }
                        if (annotation.Selected)
                        {
                            DrawTextAnnotationHandles(graphics, annotation);
                        }
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
            using var font = new Font(annotation.FontFamily, fontSize, annotation.FontStyle, GraphicsUnit.Pixel);
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

        private void DrawTextAnnotationHoverHitbox(Graphics graphics, TextAnnotation annotation)
        {
            using var tempBitmap = new Bitmap(1, 1);
            using var tempGraphics = Graphics.FromImage(tempBitmap);
            var bounds = annotation.GetBounds(tempGraphics);
            
            // Add padding to make the hitbox more visible and easier to click
            const int padding = 4;
            bounds.Inflate(padding, padding);
            
            var screenBounds = PixelToFormCoord(bounds);

            using var fillBrush = new SolidBrush(Color.FromArgb(30, Color.Cyan));
            graphics.FillRectangle(fillBrush, screenBounds);
            
            using var pen = new Pen(Color.FromArgb(128, Color.Cyan), 1f);
            pen.DashStyle = DashStyle.Dot;
            graphics.DrawRectangle(pen, screenBounds);
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
                UpdateStyleButtonsFromFontStyle(hit.FontStyle);
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
                FontFamily = GetEffectiveFontFamily(),
                FontSize = textToolFontSize,
                FontStyle = textToolFontStyle,
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
                SetHoveredTextAnnotation(hit);
                if (hit != null)
                {
                    Cursor = Cursors.IBeam;
                    return true;
                }
            }
            else if (buttons == MouseButtons.None)
            {
                SetHoveredTextAnnotation(null);
            }
            
            // Only set cross cursor when text tool is actively selected
            if (isTextToolActive && buttons == MouseButtons.None)
            {
                Cursor = Cursors.Cross;
            }

            return false;
        }

        private void SetHoveredTextAnnotation(TextAnnotation? annotation)
        {
            if (hoveredTextAnnotation != annotation)
            {
                hoveredTextAnnotation = annotation;
                pictureBox1?.Invalidate();
            }
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
                textToolFontVariant = null; // Reset variant when base family changes
                UpdateFontVariantDropdown();
                
                var effectiveFont = GetEffectiveFontFamily();
                if (activeTextAnnotation != null)
                {
                    activeTextAnnotation.FontFamily = effectiveFont;
                    pictureBox1?.Invalidate();
                }
            }
        }

        private void fontVariantComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (fontVariantComboBox?.SelectedItem is string displayName && fontVariantMap != null)
            {
                if (fontVariantMap.TryGetValue(textToolFontFamily, out var variants))
                {
                    var match = variants.Find(v => v.DisplayName == displayName);
                    if (!string.IsNullOrEmpty(match.FullName))
                    {
                        textToolFontVariant = match.FullName;
                        if (activeTextAnnotation != null)
                        {
                            activeTextAnnotation.FontFamily = match.FullName;
                            pictureBox1?.Invalidate();
                        }
                    }
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

        private void boldButton_CheckedChanged(object? sender, EventArgs e)
        {
            UpdateFontStyleFromButtons();
        }

        private void italicButton_CheckedChanged(object? sender, EventArgs e)
        {
            UpdateFontStyleFromButtons();
        }

        private void underlineButton_CheckedChanged(object? sender, EventArgs e)
        {
            UpdateFontStyleFromButtons();
        }

        private void UpdateFontStyleFromButtons()
        {
            FontStyle style = FontStyle.Regular;
            if (boldButton?.Checked == true)
            {
                style |= FontStyle.Bold;
            }
            if (italicButton?.Checked == true)
            {
                style |= FontStyle.Italic;
            }
            if (underlineButton?.Checked == true)
            {
                style |= FontStyle.Underline;
            }

            textToolFontStyle = style;
            if (activeTextAnnotation != null)
            {
                activeTextAnnotation.FontStyle = style;
                pictureBox1?.Invalidate();
            }
        }

        private void UpdateStyleButtonsFromFontStyle(FontStyle style)
        {
            if (boldButton != null)
            {
                boldButton.Checked = style.HasFlag(FontStyle.Bold);
            }
            if (italicButton != null)
            {
                italicButton.Checked = style.HasFlag(FontStyle.Italic);
            }
            if (underlineButton != null)
            {
                underlineButton.Checked = style.HasFlag(FontStyle.Underline);
            }
        }

        private void InitializeTextToolbar()
        {
            // Build font variant map
            fontVariantMap = BuildFontVariantMap();

            if (fontComboBox != null)
            {
                // Add only base font names (those that have variants grouped under them)
                // plus standalone fonts
                var allFonts = new InstalledFontCollection();
                var baseNames = new HashSet<string>(fontVariantMap.Keys, StringComparer.OrdinalIgnoreCase);
                
                foreach (var family in allFonts.Families)
                {
                    // Add if it's a base name, or if it's not a variant of another font
                    if (baseNames.Contains(family.Name) || !IsVariantOfAnotherFont(family.Name, baseNames))
                    {
                        fontComboBox.Items.Add(family.Name);
                    }
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

            UpdateFontVariantDropdown();
            UpdateTextColorButtonAppearance();
        }

        private Dictionary<string, List<(string DisplayName, string FullName)>> BuildFontVariantMap()
        {
            var map = new Dictionary<string, List<(string DisplayName, string FullName)>>(StringComparer.OrdinalIgnoreCase);
            var installedFonts = new InstalledFontCollection();
            var allFontNames = installedFonts.Families.Select(f => f.Name).ToList();

            // Common weight/style suffixes to detect variants
            string[] variantSuffixes = {
                " Thin", " Hairline", " ExtraLight", " Extra Light", " UltraLight", " Ultra Light",
                " Light", " SemiLight", " Semi Light", " DemiLight", " Demi Light",
                " Regular", " Normal", " Book", " Text", " Roman",
                " Medium", " SemiBold", " Semi Bold", " DemiBold", " Demi Bold",
                " Bold", " ExtraBold", " Extra Bold", " UltraBold", " Ultra Bold", " Heavy", " Black",
                " ExtraBlack", " Extra Black", " UltraBlack", " Ultra Black", " Fat",
                " Condensed", " Cond", " Narrow", " Compressed",
                " Extended", " Expanded", " Wide",
                " Italic", " Oblique", " Slanted"
            };

            foreach (var fontName in allFontNames)
            {
                // Try to find the base name by removing known suffixes
                string baseName = fontName;
                string variantPart = "";

                foreach (var suffix in variantSuffixes)
                {
                    if (fontName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        var potentialBase = fontName.Substring(0, fontName.Length - suffix.Length);
                        // Check if the base exists as a font or has other variants
                        if (allFontNames.Any(f => f.Equals(potentialBase, StringComparison.OrdinalIgnoreCase)) ||
                            allFontNames.Any(f => f.StartsWith(potentialBase + " ", StringComparison.OrdinalIgnoreCase) && f != fontName))
                        {
                            baseName = potentialBase;
                            variantPart = suffix.Trim();
                            break;
                        }
                    }
                }

                if (!map.ContainsKey(baseName))
                {
                    map[baseName] = new List<(string DisplayName, string FullName)>();
                }

                string displayName = string.IsNullOrEmpty(variantPart) ? "Regular" : variantPart;
                map[baseName].Add((displayName, fontName));
            }

            // Sort variants by typical weight order
            var weightOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Thin"] = 100, ["Hairline"] = 100,
                ["ExtraLight"] = 200, ["Extra Light"] = 200, ["UltraLight"] = 200, ["Ultra Light"] = 200,
                ["Light"] = 300,
                ["SemiLight"] = 350, ["Semi Light"] = 350, ["DemiLight"] = 350, ["Demi Light"] = 350,
                ["Regular"] = 400, ["Normal"] = 400, ["Book"] = 400, ["Text"] = 400, ["Roman"] = 400,
                ["Medium"] = 500,
                ["SemiBold"] = 600, ["Semi Bold"] = 600, ["DemiBold"] = 600, ["Demi Bold"] = 600,
                ["Bold"] = 700,
                ["ExtraBold"] = 800, ["Extra Bold"] = 800, ["UltraBold"] = 800, ["Ultra Bold"] = 800, ["Heavy"] = 800,
                ["Black"] = 900,
                ["ExtraBlack"] = 950, ["Extra Black"] = 950, ["UltraBlack"] = 950, ["Ultra Black"] = 950, ["Fat"] = 950
            };

            foreach (var kvp in map)
            {
                kvp.Value.Sort((a, b) =>
                {
                    int orderA = weightOrder.TryGetValue(a.DisplayName, out int wA) ? wA : 400;
                    int orderB = weightOrder.TryGetValue(b.DisplayName, out int wB) ? wB : 400;
                    return orderA.CompareTo(orderB);
                });
            }

            return map;
        }

        private bool IsVariantOfAnotherFont(string fontName, HashSet<string> baseNames)
        {
            // Check if this font name starts with any base name followed by a space
            foreach (var baseName in baseNames)
            {
                if (fontName.StartsWith(baseName + " ", StringComparison.OrdinalIgnoreCase) && fontName != baseName)
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateFontVariantDropdown()
        {
            if (fontVariantComboBox == null || fontVariantMap == null)
            {
                return;
            }

            fontVariantComboBox.Items.Clear();

            if (fontVariantMap.TryGetValue(textToolFontFamily, out var variants) && variants.Count > 1)
            {
                foreach (var (displayName, _) in variants)
                {
                    fontVariantComboBox.Items.Add(displayName);
                }

                // Select current variant or default to first
                int index = 0;
                if (textToolFontVariant != null)
                {
                    var match = variants.FindIndex(v => v.FullName.Equals(textToolFontVariant, StringComparison.OrdinalIgnoreCase));
                    if (match >= 0)
                    {
                        index = match;
                    }
                }
                fontVariantComboBox.SelectedIndex = index;
                fontVariantComboBox.Visible = isTextToolActive;
            }
            else
            {
                // No variants, hide the dropdown
                fontVariantComboBox.Visible = false;
                textToolFontVariant = null;
            }
        }

        private string GetEffectiveFontFamily()
        {
            // If a variant is selected, return that; otherwise return base family
            if (textToolFontVariant != null)
            {
                return textToolFontVariant;
            }
            return textToolFontFamily;
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
