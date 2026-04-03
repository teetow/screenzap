using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkiaSharp;
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
        // OutlineThickness == 0 means no outline
        public float OutlineThickness { get; set; } = 1f;
        public Color OutlineColor { get; set; } = Color.Black;
        public bool Selected { get; set; }
        public bool IsEditing { get; set; }
        public int CaretPosition { get; set; }
        public int? SelectionAnchor { get; set; }

        // Returns (start, end) of the selected character range.
        // start == end means no selection (caret only).
        public (int start, int end) GetSelectionRange()
        {
            if (!SelectionAnchor.HasValue) return (CaretPosition, CaretPosition);
            int a = Math.Min(CaretPosition, SelectionAnchor.Value);
            int b = Math.Max(CaretPosition, SelectionAnchor.Value);
            return (a, b);
        }

        public void ClampCaret()
        {
            CaretPosition = Math.Clamp(CaretPosition, 0, Text.Length);
            if (SelectionAnchor.HasValue)
                SelectionAnchor = Math.Clamp(SelectionAnchor.Value, 0, Text.Length);
        }

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
                OutlineThickness = OutlineThickness,
                OutlineColor = OutlineColor,
                Selected = Selected,
                IsEditing = false,
                CaretPosition = CaretPosition,
                SelectionAnchor = null
            };
        }

        public Rectangle GetBounds(Graphics graphics)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return new Rectangle(Position, new Size(100, (int)(FontSize * 1.5f)));
            }

            var size = EmojiTextRenderer.MeasureText(graphics, Text, FontFamily, FontSize, FontStyle);
            return new Rectangle(Position, new Size((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)));
        }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Text);
        }
    }

    internal static class EmojiTextRenderer
    {
        private const string EmojiFontFamily = "Segoe UI Emoji";

        private readonly struct TextRun
        {
            public string Text { get; }
            public bool UseEmojiFont { get; }

            public TextRun(string text, bool useEmojiFont)
            {
                Text = text;
                UseEmojiFont = useEmojiFont;
            }
        }

        public static SizeF MeasureText(Graphics graphics, string text, string baseFontFamily, float fontSize, FontStyle fontStyle)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new SizeF(0f, Math.Max(1f, fontSize));
            }

            if (TryMeasureWithSkia(text, baseFontFamily, fontSize, fontStyle, out var skiaSize))
            {
                return skiaSize;
            }

            using var fallbackFont = CreateGdiFont(baseFontFamily, fontSize, fontStyle);
            return graphics.MeasureString(text, fallbackFont, PointF.Empty, StringFormat.GenericTypographic);
        }

        public static void DrawText(Graphics graphics, string text, PointF position, Color color, string baseFontFamily, float fontSize, FontStyle fontStyle)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (TryDrawWithSkia(graphics, text, position, color, baseFontFamily, fontSize, fontStyle))
            {
                return;
            }

            using var brush = new SolidBrush(color);
            using var fallbackFont = CreateGdiFont(baseFontFamily, fontSize, fontStyle);
            graphics.DrawString(text, fallbackFont, brush, position, StringFormat.GenericTypographic);
        }

        private static bool TryMeasureWithSkia(string text, string baseFontFamily, float fontSize, FontStyle fontStyle, out SizeF size)
        {
            try
            {
                using var baseTypeface = CreateSkTypeface(baseFontFamily, fontStyle);
                using var emojiTypeface = CreateSkTypeface(EmojiFontFamily, FontStyle.Regular);

                if (baseTypeface == null && emojiTypeface == null)
                {
                    size = SizeF.Empty;
                    return false;
                }

                float maxWidth = 0f;
                float totalHeight = 0f;

                foreach (var line in SplitLines(text))
                {
                    float lineWidth = 0f;
                    float lineHeight = Math.Max(fontSize, 1f);

                    foreach (var run in BuildRuns(line))
                    {
                        using var paint = CreateSkPaint(run.UseEmojiFont ? emojiTypeface ?? baseTypeface! : baseTypeface ?? emojiTypeface!, fontSize, SKColors.White);
                        var metrics = paint.FontMetrics;
                        var runHeight = Math.Max(1f, metrics.Descent - metrics.Ascent + metrics.Leading);
                        lineHeight = Math.Max(lineHeight, runHeight);
                        lineWidth += paint.MeasureText(run.Text);
                    }

                    maxWidth = Math.Max(maxWidth, lineWidth);
                    totalHeight += lineHeight;
                }

                size = new SizeF((float)Math.Ceiling(maxWidth), (float)Math.Ceiling(totalHeight));
                return true;
            }
            catch
            {
                size = SizeF.Empty;
                return false;
            }
        }

        private static bool TryDrawWithSkia(Graphics graphics, string text, PointF position, Color color, string baseFontFamily, float fontSize, FontStyle fontStyle)
        {
            try
            {
                using var baseTypeface = CreateSkTypeface(baseFontFamily, fontStyle);
                using var emojiTypeface = CreateSkTypeface(EmojiFontFamily, FontStyle.Regular);

                if (baseTypeface == null && emojiTypeface == null)
                {
                    return false;
                }

                if (!TryMeasureWithSkia(text, baseFontFamily, fontSize, fontStyle, out var measuredSize))
                {
                    return false;
                }

                int width = Math.Max(1, (int)measuredSize.Width);
                int height = Math.Max(1, (int)measuredSize.Height);

                using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(bitmap))
                {
                    canvas.Clear(SKColors.Transparent);

                    float y = 0f;
                    var skColor = new SKColor(color.R, color.G, color.B, color.A);

                    foreach (var line in SplitLines(text))
                    {
                        float x = 0f;
                        float lineHeight = Math.Max(fontSize, 1f);
                        float baseline = y + fontSize;

                        foreach (var run in BuildRuns(line))
                        {
                            using var paint = CreateSkPaint(run.UseEmojiFont ? emojiTypeface ?? baseTypeface! : baseTypeface ?? emojiTypeface!, fontSize, skColor);
                            var metrics = paint.FontMetrics;
                            var runHeight = Math.Max(1f, metrics.Descent - metrics.Ascent + metrics.Leading);
                            lineHeight = Math.Max(lineHeight, runHeight);
                            baseline = y - metrics.Ascent;

                            canvas.DrawText(run.Text, x, baseline, paint);

                            if (fontStyle.HasFlag(FontStyle.Underline) && !run.UseEmojiFont)
                            {
                                float underlineY = baseline + Math.Max(1f, fontSize * 0.08f);
                                canvas.DrawLine(x, underlineY, x + paint.MeasureText(run.Text), underlineY, paint);
                            }

                            x += paint.MeasureText(run.Text);
                        }

                        y += lineHeight;
                    }
                }

                using var output = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                var lockRect = new Rectangle(0, 0, width, height);
                var bitmapData = output.LockBits(lockRect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    var bytes = new byte[bitmap.ByteCount];
                    Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
                    Marshal.Copy(bytes, 0, bitmapData.Scan0, bytes.Length);
                }
                finally
                {
                    output.UnlockBits(bitmapData);
                }

                graphics.DrawImage(output, position.X, position.Y, width, height);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return normalized.Split('\n');
        }

        private static IEnumerable<TextRun> BuildRuns(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                yield break;
            }

            var current = new StringBuilder();
            bool? currentEmoji = null;

            foreach (var element in EnumerateTextElements(line))
            {
                bool isEmoji = IsEmojiTextElement(element);
                if (currentEmoji.HasValue && currentEmoji.Value != isEmoji)
                {
                    yield return new TextRun(current.ToString(), currentEmoji.Value);
                    current.Clear();
                }

                current.Append(element);
                currentEmoji = isEmoji;
            }

            if (current.Length > 0 && currentEmoji.HasValue)
            {
                yield return new TextRun(current.ToString(), currentEmoji.Value);
            }
        }

        private static IEnumerable<string> EnumerateTextElements(string text)
        {
            var enumerator = StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                if (enumerator.GetTextElement() is string element)
                {
                    yield return element;
                }
            }
        }

        private static bool IsEmojiTextElement(string textElement)
        {
            if (string.IsNullOrEmpty(textElement))
            {
                return false;
            }

            foreach (var rune in textElement.EnumerateRunes())
            {
                if (rune.Value == 0xFE0F || rune.Value == 0x200D || rune.Value == 0x20E3)
                {
                    return true;
                }

                if ((rune.Value >= 0x1F1E6 && rune.Value <= 0x1F1FF) ||
                    (rune.Value >= 0x1F300 && rune.Value <= 0x1FAFF) ||
                    (rune.Value >= 0x2600 && rune.Value <= 0x27BF))
                {
                    return true;
                }
            }

            return false;
        }

        private static SKTypeface? CreateSkTypeface(string familyName, FontStyle style)
        {
            var weight = style.HasFlag(FontStyle.Bold) ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var slant = style.HasFlag(FontStyle.Italic) ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            var fontStyle = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
            return SKTypeface.FromFamilyName(familyName, fontStyle) ?? SKTypeface.FromFamilyName(familyName);
        }

        private static SKPaint CreateSkPaint(SKTypeface typeface, float fontSize, SKColor color)
        {
            return new SKPaint
            {
                Typeface = typeface,
                TextSize = fontSize,
                Color = color,
                IsAntialias = true,
                LcdRenderText = true,
                SubpixelText = true,
                IsStroke = false
            };
        }

        private static Font CreateGdiFont(string familyName, float fontSize, FontStyle requestedStyle)
        {
            try
            {
                var family = new FontFamily(familyName);
                var style = family.IsStyleAvailable(requestedStyle)
                    ? requestedStyle
                    : family.IsStyleAvailable(FontStyle.Regular)
                        ? FontStyle.Regular
                        : FontStyle.Bold;

                return new Font(family, fontSize, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(SystemFonts.DefaultFont.FontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            }
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
        private float textToolOutlineThickness = 1f;  // 0 = none
        private Color textToolOutlineColor = Color.Black;

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
            if (outlineColorButton != null)
            {
                outlineColorButton.Visible = isTextToolActive;
            }
            if (outlineThicknessComboBox != null)
            {
                outlineThicknessComboBox.Visible = isTextToolActive;
            }

            if (textOptionsToolStrip != null)
            {
                textOptionsToolStrip.Visible = isTextToolActive;
                PositionOverlayToolStrips();
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

        // Measure the width of text[0..charIndex] on a single line.
        private SizeF MeasureTextPrefix(Graphics graphics, string text, int charIndex, string fontFamily, float fontSize, FontStyle fontStyle)
        {
            if (charIndex <= 0) return SizeF.Empty;
            var prefix = text.Substring(0, charIndex);
            return EmojiTextRenderer.MeasureText(graphics, prefix, fontFamily, fontSize, fontStyle);
        }

        // For a flat (single-line) caret position, return (lineIndex, column) and the
        // pixel (x, lineTop) offset from the annotation origin.
        private (float x, float y, float lineH) CaretPixelOffset(
            Graphics graphics, string text, int caretPos,
            string fontFamily, float fontSize, FontStyle fontStyle)
        {
            // Normalize line endings
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            int lineStart = 0;
            float lineTop = 0f;
            string[] lines = normalized.Split('\n');

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                int lineEnd = lineStart + line.Length;
                bool lastLine = li == lines.Length - 1;

                // caretPos is inside this line or at its end
                if (caretPos <= lineEnd || lastLine)
                {
                    int col = Math.Min(caretPos - lineStart, line.Length);
                    col = Math.Max(col, 0);
                    float x = MeasureTextPrefix(graphics, line, col, fontFamily, fontSize, fontStyle).Width;
                    float lineH = Math.Max(fontSize, 1f);
                    if (line.Length > 0)
                    {
                        var lsz = EmojiTextRenderer.MeasureText(graphics, line, fontFamily, fontSize, fontStyle);
                        lineH = Math.Max(lineH, lsz.Height);
                    }
                    else
                    {
                        var refSz = EmojiTextRenderer.MeasureText(graphics, "M", fontFamily, fontSize, fontStyle);
                        lineH = Math.Max(lineH, refSz.Height);
                    }
                    return (x, lineTop, lineH);
                }

                // advance past this line + newline character
                lineStart = lineEnd + 1;
                var sz = EmojiTextRenderer.MeasureText(graphics, line.Length > 0 ? line : "M", fontFamily, fontSize, fontStyle);
                lineTop += Math.Max(fontSize, sz.Height);
            }

            return (0f, lineTop, fontSize);
        }

        private void DrawTextAnnotation(Graphics graphics, TextAnnotation annotation, AnnotationSurface surface, float scale)
        {
            var text = annotation.Text;
            bool isEmpty = string.IsNullOrEmpty(text);

            if (isEmpty && !annotation.IsEditing)
                return;

            float fontSize = surface == AnnotationSurface.Screen ? annotation.FontSize * scale : annotation.FontSize;

            PointF position = surface == AnnotationSurface.Screen
                ? PixelToFormCoordF(annotation.Position)
                : new PointF(annotation.Position.X, annotation.Position.Y);

            // ── draw text with outline ────────────────────────────────────────
            if (!isEmpty)
            {
                float baseOutline = annotation.OutlineThickness;
                if (baseOutline > 0f)
                {
                    // Render a filled disc of echoes so every pixel within the outline
                    // radius is covered, regardless of thickness.
                    float radius = surface == AnnotationSurface.Screen ? baseOutline * scale : baseOutline;
                    int steps = (int)Math.Ceiling(radius);
                    for (int dx = -steps; dx <= steps; dx++)
                    {
                        for (int dy = -steps; dy <= steps; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (MathF.Sqrt(dx * dx + dy * dy) > radius) continue;
                            EmojiTextRenderer.DrawText(graphics, text,
                                new PointF(position.X + dx, position.Y + dy),
                                annotation.OutlineColor,
                                annotation.FontFamily, fontSize, annotation.FontStyle);
                        }
                    }
                }

                EmojiTextRenderer.DrawText(graphics, text, position,
                    annotation.TextColor, annotation.FontFamily, fontSize, annotation.FontStyle);
            }

            // ── caret + selection (screen only, editing mode) ─────────────────
            if (annotation.IsEditing && surface == AnnotationSurface.Screen)
            {
                annotation.ClampCaret();
                var (selStart, selEnd) = annotation.GetSelectionRange();

                // Draw selection highlight
                if (selStart < selEnd)
                {
                    DrawTextSelectionHighlight(graphics, annotation, position, fontSize, selStart, selEnd);
                }

                // Draw caret
                var (cx, cy, ch) = CaretPixelOffset(graphics, annotation.Text,
                    annotation.CaretPosition, annotation.FontFamily, fontSize, annotation.FontStyle);
                using var caretPen = new Pen(annotation.TextColor, 2f);
                graphics.DrawLine(caretPen,
                    position.X + cx, position.Y + cy,
                    position.X + cx, position.Y + cy + ch);
            }
            else if (annotation.IsEditing && isEmpty)
            {
                // brand-new annotation with no text yet – draw a placeholder bar
                float lineH = Math.Max(fontSize, 4f);
                using var caretPen = new Pen(annotation.TextColor, 2f);
                graphics.DrawLine(caretPen, position.X, position.Y, position.X, position.Y + lineH);
            }
        }

        private void DrawTextSelectionHighlight(
            Graphics graphics, TextAnnotation annotation,
            PointF origin, float fontSize, int selStart, int selEnd)
        {
            // Handle multi-line selections by iterating line by line
            var normalized = annotation.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            int lineCharStart = 0;
            float lineTop = 0f;

            using var hlBrush = new SolidBrush(Color.FromArgb(120, 51, 153, 255));

            foreach (var line in lines)
            {
                int lineEnd = lineCharStart + line.Length;

                int overlapStart = Math.Max(selStart, lineCharStart);
                int overlapEnd   = Math.Min(selEnd,   lineEnd);

                if (overlapStart < overlapEnd)
                {
                    float x0 = MeasureTextPrefix(graphics, line,
                        overlapStart - lineCharStart, annotation.FontFamily, fontSize, annotation.FontStyle).Width;
                    float x1 = MeasureTextPrefix(graphics, line,
                        overlapEnd   - lineCharStart, annotation.FontFamily, fontSize, annotation.FontStyle).Width;

                    float lineH = line.Length > 0
                        ? EmojiTextRenderer.MeasureText(graphics, line, annotation.FontFamily, fontSize, annotation.FontStyle).Height
                        : EmojiTextRenderer.MeasureText(graphics, "M", annotation.FontFamily, fontSize, annotation.FontStyle).Height;
                    lineH = Math.Max(lineH, fontSize);

                    graphics.FillRectangle(hlBrush,
                        origin.X + x0, origin.Y + lineTop,
                        Math.Max(1f, x1 - x0), lineH);
                }

                // advance: +1 for the \n character
                float advanceH = line.Length > 0
                    ? EmojiTextRenderer.MeasureText(graphics, line, annotation.FontFamily, fontSize, annotation.FontStyle).Height
                    : EmojiTextRenderer.MeasureText(graphics, "M", annotation.FontFamily, fontSize, annotation.FontStyle).Height;
                lineTop += Math.Max(fontSize, advanceH);
                lineCharStart = lineEnd + 1;

                if (lineCharStart > selEnd) break;
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
                // Single click → selection mode only. Enter or double-click to edit.
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

            // Create new text annotation — go straight to edit mode
            textAnnotationSnapshotBeforeEdit = CloneTextAnnotations();
            var clampedPoint = ClampPointToImage(pixelPoint);
            var newAnnotation = new TextAnnotation
            {
                Position = clampedPoint,
                FontFamily = GetEffectiveFontFamily(),
                FontSize = textToolFontSize,
                FontStyle = textToolFontStyle,
                TextColor = textToolColor,
                OutlineThickness = textToolOutlineThickness,
                OutlineColor = textToolOutlineColor,
                Text = string.Empty,
                Selected = true,
                IsEditing = true,
                CaretPosition = 0
            };

            textAnnotations.Add(newAnnotation);
            SelectTextAnnotation(newAnnotation);
            activeTextAnnotation = newAnnotation;
            textAnnotationChangedDuringDrag = false;
            pictureBox1?.Invalidate();
            return true;
        }

        private void HandleTextToolDoubleClick(Point pixelPoint, Point formPoint)
        {
            if (!isTextToolActive && !textAnnotations.Any())
            {
                return;
            }

            var hit = HitTestTextAnnotation(pixelPoint, formPoint);
            if (hit == null)
            {
                return;
            }

            // Enter edit mode for the hit annotation
            if (activeTextAnnotation != null && activeTextAnnotation != hit)
            {
                FinalizeActiveTextAnnotation();
            }

            SelectTextAnnotation(hit);
            activeTextAnnotation = hit;
            EnterTextEditMode(hit);
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

        // ── caret helpers ─────────────────────────────────────────────────────

        private void EnterTextEditMode(TextAnnotation annotation)
        {
            if (textAnnotationSnapshotBeforeEdit == null)
                textAnnotationSnapshotBeforeEdit = CloneTextAnnotations();

            annotation.IsEditing = true;
            annotation.CaretPosition = annotation.Text.Length;
            annotation.SelectionAnchor = null;
            UpdateStyleButtonsFromFontStyle(annotation.FontStyle);
            SyncOutlineToolbarFromAnnotation(annotation);
            textAnnotationChangedDuringDrag = false;
            pictureBox1?.Invalidate();
        }

        private void SyncOutlineToolbarFromAnnotation(TextAnnotation annotation)
        {
            textToolOutlineThickness = annotation.OutlineThickness;
            textToolOutlineColor = annotation.OutlineColor;
            UpdateOutlineColorButtonAppearance();
            if (outlineThicknessComboBox != null)
            {
                var target = annotation.OutlineThickness <= 0f ? "None" : ((int)annotation.OutlineThickness).ToString();
                int idx = outlineThicknessComboBox.Items.IndexOf(target);
                if (idx >= 0) outlineThicknessComboBox.SelectedIndex = idx;
            }
        }

        // Returns the caret index at the left edge of grapheme cluster that contains charIndex.
        // We treat text as a flat string and navigate by StringInfo elements so surrogate pairs
        // and combining characters move as one unit.
        private static int PrevGrapheme(string text, int pos)
        {
            if (pos <= 0) return 0;
            var info = new System.Globalization.StringInfo(text);
            int len = info.LengthInTextElements;
            // find which element starts at or before pos
            int elem = 0;
            for (int i = 0; i < len; i++)
            {
                int next = elem + System.Globalization.StringInfo.GetNextTextElement(text, elem).Length;
                if (next >= pos) break;
                elem = next;
            }
            return elem;
        }

        private static int NextGrapheme(string text, int pos)
        {
            if (pos >= text.Length) return text.Length;
            return pos + System.Globalization.StringInfo.GetNextTextElement(text, pos).Length;
        }

        // Move caret within current line. Returns new position.
        private static int MoveCaretLineStart(string text, int pos)
        {
            int lineStart = pos;
            while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;
            return lineStart;
        }

        private static int MoveCaretLineEnd(string text, int pos)
        {
            int lineEnd = pos;
            while (lineEnd < text.Length && text[lineEnd] != '\n') lineEnd++;
            return lineEnd;
        }

        private void MoveCaret(TextAnnotation ta, int newPos, bool extend)
        {
            newPos = Math.Clamp(newPos, 0, ta.Text.Length);
            if (extend)
            {
                if (!ta.SelectionAnchor.HasValue) ta.SelectionAnchor = ta.CaretPosition;
            }
            else
            {
                ta.SelectionAnchor = null;
            }
            ta.CaretPosition = newPos;
            pictureBox1?.Invalidate();
        }

        // Delete selected text (or nothing if no selection).  Returns true if anything deleted.
        private bool DeleteSelectedText(TextAnnotation ta)
        {
            var (start, end) = ta.GetSelectionRange();
            if (start == end) return false;
            ta.Text = ta.Text.Remove(start, end - start);
            ta.CaretPosition = start;
            ta.SelectionAnchor = null;
            textAnnotationChangedDuringDrag = true;
            return true;
        }

        private void InsertTextAtCaret(TextAnnotation ta, string insert)
        {
            // Normalize line endings so caret indices always match the stored text
            var normalized = insert.Replace("\r\n", "\n").Replace('\r', '\n');
            DeleteSelectedText(ta); // delete selection first
            ta.Text = ta.Text.Insert(ta.CaretPosition, normalized);
            ta.CaretPosition += normalized.Length;
            ta.SelectionAnchor = null;
            textAnnotationChangedDuringDrag = true;
            pictureBox1?.Invalidate();
        }

        // ── selection-mode (object selected, not editing) key handler ─────────

        private bool HandleTextToolKeyDown(KeyEventArgs e)
        {
            if (!isTextToolActive)
                return false;

            // ── object-selection mode (annotation selected but not editing text) ──
            if (activeTextAnnotation == null || !activeTextAnnotation.IsEditing)
            {
                if (selectedTextAnnotation == null)
                    return false;

                if (e.KeyCode == Keys.Escape)
                {
                    SelectTextAnnotation(null);
                    activeTextAnnotation = null;
                    e.Handled = true;
                    return true;
                }

                if (e.KeyCode == Keys.Delete)
                {
                    var before = CloneTextAnnotations();
                    textAnnotations.Remove(selectedTextAnnotation);
                    selectedTextAnnotation = null;
                    activeTextAnnotation = null;
                    var after = CloneTextAnnotations();
                    PushTextUndoStep(before, after);
                    pictureBox1?.Invalidate();
                    e.Handled = true;
                    return true;
                }

                // Enter → enter text-edit mode
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F2)
                {
                    if (textAnnotationSnapshotBeforeEdit == null)
                        textAnnotationSnapshotBeforeEdit = CloneTextAnnotations();
                    activeTextAnnotation = selectedTextAnnotation;
                    EnterTextEditMode(activeTextAnnotation);
                    e.Handled = true;
                    return true;
                }

                // Any printable key → enter edit mode then let KeyPress insert it
                return false;
            }

            // ── text-editing mode ──────────────────────────────────────────────
            var ta = activeTextAnnotation;
            bool shift = e.Shift;
            bool ctrl  = e.Control;

            switch (e.KeyCode)
            {
                // ── exit editing ──────────────────────────────────────────────
                case Keys.Escape:
                case Keys.Enter when !shift:
                {
                    // Escape → discard empty new annotations; Enter → confirm
                    if (e.KeyCode == Keys.Escape && !ta.IsValid())
                    {
                        textAnnotations.Remove(ta);
                        textAnnotationSnapshotBeforeEdit = null;
                    }
                    else
                    {
                        CommitTextAnnotationUndo();
                    }
                    ta.IsEditing = false;
                    ta.SelectionAnchor = null;
                    // Keep annotation selected (object-selection mode)
                    activeTextAnnotation = null;
                    pictureBox1?.Invalidate();
                    e.Handled = true;
                    return true;
                }

                // ── navigation ────────────────────────────────────────────────
                case Keys.Left:
                {
                    int newPos = ctrl
                        ? MovePrevWord(ta.Text, ta.CaretPosition)
                        : (shift || ta.SelectionAnchor == null || ta.CaretPosition == ta.SelectionAnchor.Value)
                            ? PrevGrapheme(ta.Text, ta.CaretPosition)
                            : ta.GetSelectionRange().start;
                    if (!shift && ta.SelectionAnchor.HasValue)
                        newPos = ta.GetSelectionRange().start;
                    MoveCaret(ta, newPos, shift);
                    e.Handled = true; return true;
                }
                case Keys.Right:
                {
                    int newPos = ctrl
                        ? MoveNextWord(ta.Text, ta.CaretPosition)
                        : (shift || ta.SelectionAnchor == null || ta.CaretPosition == ta.SelectionAnchor.Value)
                            ? NextGrapheme(ta.Text, ta.CaretPosition)
                            : ta.GetSelectionRange().end;
                    if (!shift && ta.SelectionAnchor.HasValue)
                        newPos = ta.GetSelectionRange().end;
                    MoveCaret(ta, newPos, shift);
                    e.Handled = true; return true;
                }
                case Keys.Home:
                    MoveCaret(ta, ctrl ? 0 : MoveCaretLineStart(ta.Text, ta.CaretPosition), shift);
                    e.Handled = true; return true;
                case Keys.End:
                    MoveCaret(ta, ctrl ? ta.Text.Length : MoveCaretLineEnd(ta.Text, ta.CaretPosition), shift);
                    e.Handled = true; return true;

                // ── deletion ──────────────────────────────────────────────────
                case Keys.Back:
                {
                    if (!DeleteSelectedText(ta))
                    {
                        int prev = ctrl
                            ? MovePrevWord(ta.Text, ta.CaretPosition)
                            : PrevGrapheme(ta.Text, ta.CaretPosition);
                        if (prev < ta.CaretPosition)
                        {
                            ta.Text = ta.Text.Remove(prev, ta.CaretPosition - prev);
                            ta.CaretPosition = prev;
                            ta.SelectionAnchor = null;
                            textAnnotationChangedDuringDrag = true;
                        }
                    }
                    pictureBox1?.Invalidate();
                    e.Handled = true; return true;
                }
                case Keys.Delete:
                {
                    if (!DeleteSelectedText(ta))
                    {
                        int next = ctrl
                            ? MoveNextWord(ta.Text, ta.CaretPosition)
                            : NextGrapheme(ta.Text, ta.CaretPosition);
                        if (next > ta.CaretPosition)
                        {
                            ta.Text = ta.Text.Remove(ta.CaretPosition, next - ta.CaretPosition);
                            ta.SelectionAnchor = null;
                            textAnnotationChangedDuringDrag = true;
                        }
                    }
                    pictureBox1?.Invalidate();
                    e.Handled = true; return true;
                }

                // ── clipboard ─────────────────────────────────────────────────
                case Keys.A when ctrl:
                    ta.SelectionAnchor = 0;
                    ta.CaretPosition = ta.Text.Length;
                    pictureBox1?.Invalidate();
                    e.Handled = true; return true;

                case Keys.C when ctrl:
                case Keys.X when ctrl:
                {
                    var (selStart, selEnd) = ta.GetSelectionRange();
                    if (selStart < selEnd)
                    {
                        Clipboard.SetText(ta.Text.Substring(selStart, selEnd - selStart));
                        if (e.KeyCode == Keys.X)
                        {
                            DeleteSelectedText(ta);
                            pictureBox1?.Invalidate();
                        }
                    }
                    e.SuppressKeyPress = true;
                    e.Handled = true; return true;
                }

                case Keys.V when ctrl:
                {
                    if (Clipboard.ContainsText())
                    {
                        var pasted = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(pasted))
                            InsertTextAtCaret(ta, pasted);
                    }
                    e.SuppressKeyPress = true;
                    e.Handled = true; return true;
                }
            }

            return false;
        }

        private static int MovePrevWord(string text, int pos)
        {
            if (pos <= 0) return 0;
            int p = pos - 1;
            while (p > 0 && char.IsWhiteSpace(text[p])) p--;
            while (p > 0 && !char.IsWhiteSpace(text[p - 1])) p--;
            return p;
        }

        private static int MoveNextWord(string text, int pos)
        {
            if (pos >= text.Length) return text.Length;
            int p = pos;
            while (p < text.Length && !char.IsWhiteSpace(text[p])) p++;
            while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
            return p;
        }

        private bool HandleTextToolKeyPress(KeyPressEventArgs e)
        {
            if (!isTextToolActive)
                return false;

            // Selection mode: any printable key enters edit mode first
            if ((activeTextAnnotation == null || !activeTextAnnotation.IsEditing) && selectedTextAnnotation != null)
            {
                if (char.IsControl(e.KeyChar)) return false;
                if (textAnnotationSnapshotBeforeEdit == null)
                    textAnnotationSnapshotBeforeEdit = CloneTextAnnotations();
                activeTextAnnotation = selectedTextAnnotation;
                EnterTextEditMode(activeTextAnnotation);
                // CaretPosition is now at end; the char will be inserted below
            }

            if (activeTextAnnotation == null || !activeTextAnnotation.IsEditing)
                return false;

            if (char.IsControl(e.KeyChar) && e.KeyChar != '\r' && e.KeyChar != '\n')
                return false;

            // Shift+Enter → newline
            if (e.KeyChar == '\r' || e.KeyChar == '\n')
            {
                if (Control.ModifierKeys.HasFlag(Keys.Shift))
                {
                    InsertTextAtCaret(activeTextAnnotation, "\n");
                    e.Handled = true;
                    return true;
                }
                return false; // plain Enter handled in KeyDown
            }

            InsertTextAtCaret(activeTextAnnotation, e.KeyChar.ToString());
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
                ApplyFontFamily(fontName);
            }
        }

        private void fontComboBox_Leave(object? sender, EventArgs e)
        {
            // Validate free-typed text: accept only if it matches a known item
            if (fontComboBox != null)
            {
                var typed = fontComboBox.Text;
                int idx = fontComboBox.Items.IndexOf(typed);
                if (idx >= 0)
                {
                    ApplyFontFamily(typed);
                }
                else
                {
                    // Revert to last valid family
                    fontComboBox.Text = textToolFontFamily;
                }
            }
            ReturnFocusToCanvas();
        }

        private void ApplyFontFamily(string fontName)
        {
            textToolFontFamily = fontName;
            textToolFontVariant = null;
            UpdateFontVariantDropdown();

            var effectiveFont = GetEffectiveFontFamily();
            if (activeTextAnnotation != null)
            {
                activeTextAnnotation.FontFamily = effectiveFont;
                pictureBox1?.Invalidate();
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

        private void ReturnFocusToCanvas()
        {
            pictureBox1?.Focus();
        }

        private void UpdateTextColorButtonAppearance()
        {
            if (textColorButton != null)
            {
                textColorButton.BackColor = textToolColor;
                textColorButton.ForeColor = GetContrastColor(textToolColor);
            }
        }

        private void outlineColorButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new ColorDialog();
            dialog.Color = textToolOutlineColor;
            dialog.FullOpen = true;

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                textToolOutlineColor = dialog.Color;
                UpdateOutlineColorButtonAppearance();
                if (activeTextAnnotation != null)
                {
                    activeTextAnnotation.OutlineColor = textToolOutlineColor;
                    pictureBox1?.Invalidate();
                }
            }
        }

        private void UpdateOutlineColorButtonAppearance()
        {
            if (outlineColorButton != null)
            {
                outlineColorButton.BackColor = textToolOutlineColor;
                outlineColorButton.ForeColor = GetContrastColor(textToolOutlineColor);
            }
        }

        private void outlineThicknessComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (outlineThicknessComboBox?.SelectedItem is string selected)
            {
                textToolOutlineThickness = selected == "None" ? 0f
                    : float.TryParse(selected, out float t) ? t : 1f;

                if (activeTextAnnotation != null)
                {
                    activeTextAnnotation.OutlineThickness = textToolOutlineThickness;
                    pictureBox1?.Invalidate();
                }
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

                // Wire Leave so free-typed names are validated and focus returns to canvas
                var innerCombo = fontComboBox.Control as ComboBox;
                if (innerCombo != null)
                {
                    innerCombo.Leave += (s, e) => fontComboBox_Leave(s, e);
                    innerCombo.AutoCompleteMode   = AutoCompleteMode.SuggestAppend;
                    innerCombo.AutoCompleteSource = AutoCompleteSource.ListItems;
                }
            }

            if (fontSizeComboBox != null)
            {
                fontSizeComboBox.Items.AddRange(new object[] { "8", "10", "12", "14", "16", "18", "20", "24", "28", "32", "36", "48", "72" });
                fontSizeComboBox.Text = textToolFontSize.ToString();
                var sizeInner = fontSizeComboBox.Control as ComboBox;
                if (sizeInner != null)
                    sizeInner.Leave += (s, e) => ReturnFocusToCanvas();
            }

            if (outlineThicknessComboBox != null)
            {
                outlineThicknessComboBox.Items.AddRange(new object[] { "None", "1", "2", "3", "4", "6", "8" });
                // Select the item matching current default thickness
                var defaultItem = textToolOutlineThickness <= 0f ? "None" : ((int)textToolOutlineThickness).ToString();
                int idx = outlineThicknessComboBox.Items.IndexOf(defaultItem);
                outlineThicknessComboBox.SelectedIndex = idx >= 0 ? idx : 0;
                var thickInner = outlineThicknessComboBox.Control as ComboBox;
                if (thickInner != null)
                    thickInner.Leave += (s, e) => ReturnFocusToCanvas();
            }

            UpdateFontVariantDropdown();
            UpdateTextColorButtonAppearance();
            UpdateOutlineColorButtonAppearance();
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
