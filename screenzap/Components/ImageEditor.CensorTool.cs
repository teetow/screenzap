using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TextDetection;
using screenzap.lib;

namespace screenzap
{
    public partial class ImageEditor
    {
        private readonly List<CensorRegion> censorRegions = new List<CensorRegion>();
        private bool isCensorToolActive;
        private bool suppressConfidenceEvents;
        private float currentConfidenceThreshold;
        private Bitmap? censorPreviewBuffer;
        private struct RowRange
        {
            public int Start;
            public int End;
        }

        private struct ColumnRange
        {
            public int Start;
            public int End;
        }

        private sealed class CensorRegion
        {
            public CensorRegion(Rectangle bounds, float confidence)
            {
                Bounds = bounds;
                Confidence = confidence;
            }

            public Rectangle Bounds { get; }

            public float Confidence { get; }

            public bool Selected { get; set; }
        }
        private void ReleaseCensorPreviewBuffer()
        {
            var existing = censorPreviewBuffer;
            censorPreviewBuffer = null;
            existing?.Dispose();
        }

        private void ShowCensorProgressIndicator()
        {
            UseWaitCursor = true;
            if (censorProgressBar != null)
            {
                censorProgressBar.Visible = true;
            }
            Application.DoEvents();
        }

        private void HideCensorProgressIndicator()
        {
            UseWaitCursor = false;
            if (censorProgressBar != null)
            {
                censorProgressBar.Visible = false;
            }
        }

        private bool BuildCensorPreviewBuffer()
        {
            ReleaseCensorPreviewBuffer();

            if (pictureBox1.Image == null || censorRegions.Count == 0)
            {
                return false;
            }

            var working = new Bitmap(pictureBox1.Image.Width, pictureBox1.Image.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(working))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(pictureBox1.Image, Point.Empty);
            }

            using (var bufferGraphics = Graphics.FromImage(working))
            {
                bufferGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                bufferGraphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                foreach (var region in censorRegions)
                {
                    var clamped = ClampToImage(region.Bounds);
                    if (clamped.Width <= 0 || clamped.Height <= 0)
                    {
                        continue;
                    }

                    var regionSnapshot = CaptureRegion(clamped);
                    if (regionSnapshot == null)
                    {
                        continue;
                    }

                    using (regionSnapshot)
                    using (var scrambled = GenerateCensoredBitmap(regionSnapshot, clamped))
                    {
                        bufferGraphics.DrawImage(scrambled, clamped);
                    }
                }
            }

            censorPreviewBuffer = working;
            return true;
        }

        private bool ActivateCensorTool()
        {
            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return false;
            }

            ShowCensorProgressIndicator();
            var previousCursor = Cursor.Current;
            var hasSelection = !Selection.IsEmpty;
            var detectionZone = hasSelection ? ClampToImage(Selection) : GetImageBounds();
            if (hasSelection && (detectionZone.Width <= 0 || detectionZone.Height <= 0))
            {
                HideCensorProgressIndicator();
                return false;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;

                if (hasSelection)
                {
                    using var selectionBitmap = CaptureRegion(detectionZone);
                    if (selectionBitmap == null)
                    {
                        MessageBox.Show(this, "Failed to capture the selected region.", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        censorRegions.Clear();
                        ReleaseCensorPreviewBuffer();
                        UpdateCensorToolbarState();
                        return false;
                    }

                    var detected = TextRegionDetector.FindTextRegionsDetailed(selectionBitmap);
                    Rectangle refinedBounds;
                    float combinedConfidence = 0f;

                    if (detected.Count > 0)
                    {
                        refinedBounds = detected[0].Bounds;
                        combinedConfidence = detected[0].Confidence;

                        for (int i = 1; i < detected.Count; i++)
                        {
                            refinedBounds = Rectangle.Union(refinedBounds, detected[i].Bounds);
                            combinedConfidence = Math.Max(combinedConfidence, detected[i].Confidence);
                        }
                    }
                    else
                    {
                        refinedBounds = new Rectangle(Point.Empty, selectionBitmap.Size);
                    }

                    if (refinedBounds.Width <= 0 || refinedBounds.Height <= 0)
                    {
                        MessageBox.Show(this, "No text regions were detected inside the selection.", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        censorRegions.Clear();
                        ReleaseCensorPreviewBuffer();
                        UpdateCensorToolbarState();
                        return false;
                    }

                    var translated = new Rectangle(
                        detectionZone.Left + refinedBounds.Left,
                        detectionZone.Top + refinedBounds.Top,
                        refinedBounds.Width,
                        refinedBounds.Height);

                    translated = ClampToImage(translated);
                    if (translated.Width <= 0 || translated.Height <= 0)
                    {
                        MessageBox.Show(this, "No text regions were detected inside the selection.", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        censorRegions.Clear();
                        ReleaseCensorPreviewBuffer();
                        UpdateCensorToolbarState();
                        return false;
                    }

                    censorRegions.Clear();
                    censorRegions.Add(new CensorRegion(translated, combinedConfidence));
                }
                else
                {
                    using (var detectionSource = new Bitmap(pictureBox1.Image))
                    {
                        var detected = TextRegionDetector.FindTextRegionsDetailed(detectionSource);
                        if (detected.Count == 0)
                        {
                            MessageBox.Show(this, "No text regions were detected.", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            censorRegions.Clear();
                            ReleaseCensorPreviewBuffer();
                            UpdateCensorToolbarState();
                            return false;
                        }

                        censorRegions.Clear();
                        foreach (var region in detected)
                        {
                            float confidence = float.IsNaN(region.Confidence) ? 0f : Math.Max(0f, region.Confidence);
                            censorRegions.Add(new CensorRegion(region.Bounds, confidence));
                        }
                    }
                }

                BuildCensorPreviewBuffer();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to detect text regions.\n{ex.Message}", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                censorRegions.Clear();
                ReleaseCensorPreviewBuffer();
                UpdateCensorToolbarState();
                return false;
            }
            finally
            {
                HideCensorProgressIndicator();
                Cursor.Current = previousCursor;
            }

            foreach (var region in censorRegions)
            {
                region.Selected = false;
            }

            isCensorToolActive = true;
            currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar?.Value ?? 0);
            suppressConfidenceEvents = true;

            if (confidenceTrackBar != null)
            {
                confidenceTrackBar.Value = confidenceTrackBar.Minimum;
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
            }

            suppressConfidenceEvents = false;

            if (censorToolStrip != null)
            {
                censorToolStrip.Visible = true;
            }

            UpdateCensorToolbarState();
            ClearSelection();
            pictureBox1.Invalidate();
            return true;
        }

        private void DeactivateCensorTool(bool applySelections)
        {
            HideCensorProgressIndicator();

            if (applySelections && isCensorToolActive)
            {
                ApplyCensorRegions();
            }

            isCensorToolActive = false;
            censorRegions.Clear();
            ReleaseCensorPreviewBuffer();
            currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar?.Value ?? 0);
            Cursor = Cursors.Default;

            suppressConfidenceEvents = true;
            if (confidenceTrackBar != null)
            {
                confidenceTrackBar.Value = confidenceTrackBar.Minimum;
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
                confidenceTrackBar.Enabled = false;
            }
            suppressConfidenceEvents = false;

            if (censorToolStrip != null)
            {
                censorToolStrip.Visible = false;
            }

            UpdateCensorToolbarState();
            ClearSelection();
            pictureBox1.Invalidate();
            UpdateCommandUI();
        }

        private void ApplyCensorRegions()
        {
            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return;
            }

            var selectedRegions = new List<Rectangle>();
            foreach (var region in censorRegions.Where(r => r.Selected))
            {
                var clamped = ClampToImage(region.Bounds);
                if (clamped.Width > 0 && clamped.Height > 0)
                {
                    selectedRegions.Add(clamped);
                }
            }

            if (selectedRegions.Count == 0)
            {
                return;
            }

            var previousSelection = Selection;

            Rectangle combinedRegion = selectedRegions[0];
            for (int i = 1; i < selectedRegions.Count; i++)
            {
                combinedRegion = Rectangle.Union(combinedRegion, selectedRegions[i]);
            }

            combinedRegion = ClampToImage(combinedRegion);
            var beforeSnapshot = CaptureRegion(combinedRegion);
            if (beforeSnapshot == null)
            {
                return;
            }

            Bitmap? afterSnapshot = null;

            try
            {
                var canvas = pictureBox1.Image;
                if (canvas == null)
                {
                    beforeSnapshot.Dispose();
                    return;
                }

                using (var gImg = Graphics.FromImage(canvas))
                {
                    gImg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    gImg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    gImg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;

                    if (censorPreviewBuffer != null)
                    {
                        foreach (var regionBounds in selectedRegions)
                        {
                            gImg.DrawImage(censorPreviewBuffer, regionBounds, regionBounds, GraphicsUnit.Pixel);
                        }
                    }
                    else
                    {
                        foreach (var regionBounds in selectedRegions)
                        {
                            using var beforeRegion = CaptureRegion(regionBounds);
                            if (beforeRegion == null)
                            {
                                continue;
                            }

                            using var afterRegion = GenerateCensoredBitmap(beforeRegion, regionBounds);
                            gImg.DrawImage(afterRegion, regionBounds);
                        }
                    }
                }

                afterSnapshot = CaptureRegion(combinedRegion);
                if (afterSnapshot == null)
                {
                    return;
                }

                Selection = previousSelection;
                PushUndoStep(combinedRegion, beforeSnapshot, afterSnapshot, previousSelection, Selection);
                pictureBox1.Invalidate();
                UpdateCommandUI();
                beforeSnapshot = null;
                afterSnapshot = null;
            }
            finally
            {
                Selection = previousSelection;
                beforeSnapshot?.Dispose();
                afterSnapshot?.Dispose();
            }
        }

        private float CalculateConfidenceThreshold(int sliderValue)
        {
            var normalized = Math.Clamp(sliderValue / 100f, 0f, 1f);
            return 1f - normalized;
        }

        private void UpdateCensorToolbarState()
        {
            int sliderValue = confidenceTrackBar?.Value ?? 0;
            var threshold = CalculateConfidenceThreshold(sliderValue);
            if (confidenceValueLabel != null)
            {
                int thresholdPercent = (int)Math.Round(threshold * 100f, MidpointRounding.AwayFromZero);
                confidenceValueLabel.Text = "≥ " + thresholdPercent.ToString(CultureInfo.InvariantCulture) + "%";
            }

            bool anyRegions = censorRegions.Count > 0;
            bool anySelected = censorRegions.Any(r => r.Selected);

            if (selectAllToolStripButton != null)
            {
                selectAllToolStripButton.Enabled = anyRegions;
            }

            if (selectNoneToolStripButton != null)
            {
                selectNoneToolStripButton.Enabled = anyRegions;
            }

            if (applyCensorToolStripButton != null)
            {
                applyCensorToolStripButton.Enabled = anySelected;
            }

            if (confidenceTrackBar != null)
            {
                confidenceTrackBar.Enabled = isCensorToolActive && anyRegions;
            }

            if (confidenceToolStripHost != null)
            {
                confidenceToolStripHost.Enabled = isCensorToolActive && anyRegions;
            }
        }

        private CensorRegion? FindRegionAtPixel(Point pixel)
        {
            for (int i = censorRegions.Count - 1; i >= 0; i--)
            {
                if (censorRegions[i].Bounds.Contains(pixel))
                {
                    return censorRegions[i];
                }
            }

            return null;
        }

        private void confidenceTrackBar_ValueChanged(object? sender, EventArgs e)
        {
            if (confidenceTrackBar == null)
            {
                return;
            }

            if (suppressConfidenceEvents)
            {
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
                UpdateCensorToolbarState();
                return;
            }

            if (!isCensorToolActive)
            {
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
                UpdateCensorToolbarState();
                return;
            }

            currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);

            foreach (var region in censorRegions)
            {
                float confidence = float.IsNaN(region.Confidence) ? 0f : region.Confidence;
                bool meetsThreshold = confidence >= currentConfidenceThreshold;

                if (confidence <= 0f && currentConfidenceThreshold <= 0f)
                {
                    meetsThreshold = true;
                }

                region.Selected = meetsThreshold;
            }

            UpdateCensorToolbarState();
            pictureBox1.Invalidate();
        }

        private void selectAllToolStripButton_Click(object? sender, EventArgs e)
        {
            if (!isCensorToolActive)
            {
                return;
            }

            foreach (var region in censorRegions)
            {
                region.Selected = true;
            }

            UpdateCensorToolbarState();
            pictureBox1.Invalidate();
        }

        private void selectNoneToolStripButton_Click(object? sender, EventArgs e)
        {
            if (!isCensorToolActive)
            {
                return;
            }

            foreach (var region in censorRegions)
            {
                region.Selected = false;
            }

            UpdateCensorToolbarState();
            pictureBox1.Invalidate();
        }

        private void applyCensorToolStripButton_Click(object? sender, EventArgs e)
        {
            DeactivateCensorTool(true);
        }

        private void cancelCensorToolStripButton_Click(object? sender, EventArgs e)
        {
            DeactivateCensorTool(false);
        }

        private Bitmap? CaptureRegion(Rectangle region)
        {
            if (pictureBox1.Image == null || region.Width <= 0 || region.Height <= 0)
            {
                return null;
            }

            var snapshot = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(snapshot))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(pictureBox1.Image, new Rectangle(Point.Empty, region.Size), region, GraphicsUnit.Pixel);
            }

            return snapshot;
        }

        private void PushUndoStep(Rectangle region, Bitmap? before, Bitmap? after, Rectangle selectionBefore, Rectangle selectionAfter, bool replacesImage = false, List<AnnotationShape>? shapesBefore = null, List<AnnotationShape>? shapesAfter = null)
        {
            bool hasBitmapChange = before != null && after != null;
            bool hasShapeChange = shapesBefore != null && shapesAfter != null;

            if (!hasBitmapChange && !hasShapeChange)
            {
                before?.Dispose();
                after?.Dispose();
                return;
            }

            if (hasBitmapChange && !replacesImage && (region.Width <= 0 || region.Height <= 0))
            {
                before?.Dispose();
                after?.Dispose();
                return;
            }

            undoStack.Push(new ImageUndoStep(region, before, after, selectionBefore, selectionAfter, replacesImage, shapesBefore, shapesAfter));
            hasUnsavedChanges = true;
        }

        private void ApplyUndoStep(ImageUndoStep step, bool applyAfterState)
        {
            if (step == null)
            {
                return;
            }

            var source = applyAfterState ? step.After : step.Before;
            var shapeState = applyAfterState ? step.ShapesAfter : step.ShapesBefore;

            if (source != null)
            {
                if (pictureBox1.Image == null)
                {
                    return;
                }

                if (step.ReplacesImage)
                {
                    var replacement = new Bitmap(source);
                    var currentZoom = ZoomLevel;
                    pictureBox1.Image?.Dispose();
                    pictureBox1.Image = replacement;
                    ZoomLevel = currentZoom;
                    pictureBox1.ClampPan();
                    ResizeWindowToImage(pictureBox1.Image.Size);
                    HandleResize();
                }
                else
                {
                    var region = ClampToImage(step.Region);
                    if (region.Width <= 0 || region.Height <= 0)
                    {
                        return;
                    }

                    var canvas = pictureBox1.Image;
                    if (canvas == null)
                    {
                        return;
                    }

                    using (var g = Graphics.FromImage(canvas))
                    {
                        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        g.DrawImage(source, region);
                    }
                }
            }

            Selection = applyAfterState ? step.SelectionAfter : step.SelectionBefore;
            ApplyAnnotationState(shapeState);
            UpdateCommandUI();
            pictureBox1.Invalidate();
        }


        private bool CensorSelection()
        {
            if (!HasEditableImage || Selection.IsEmpty)
            {
                return false;
            }

            var clampedSelection = ClampToImage(Selection);
            if (clampedSelection.Width <= 0 || clampedSelection.Height <= 0)
            {
                return false;
            }

            var selectionBefore = Selection;
            var before = CaptureRegion(clampedSelection);
            if (before == null)
            {
                return false;
            }

            Bitmap? after = null;

            try
            {
                after = GenerateCensoredBitmap(before, clampedSelection);

                var canvas = pictureBox1.Image;
                if (canvas == null)
                {
                    before.Dispose();
                    after?.Dispose();
                    return false;
                }

                using (var gImg = Graphics.FromImage(canvas))
                {
                    gImg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    gImg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    gImg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    gImg.DrawImage(after, clampedSelection);
                }

                PushUndoStep(clampedSelection, before, after, selectionBefore, Selection);
                pictureBox1.Invalidate();
                UpdateCommandUI();
                return true;
            }
            catch
            {
                before.Dispose();
                after?.Dispose();
                throw;
            }
        }

        private Bitmap GenerateCensoredBitmap(Bitmap source, Rectangle selectionBounds)
        {
            var target = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            var lockRect = new Rectangle(0, 0, source.Width, source.Height);

            var sourceData = source.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var targetData = target.LockBits(lockRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = sourceData.Stride / 4;
                int width = source.Width;
                int height = source.Height;
                int totalPixels = stride * height;

                int[] sourcePixels = new int[totalPixels];
                Marshal.Copy(sourceData.Scan0, sourcePixels, 0, totalPixels);

                int[] resultPixels = new int[totalPixels];
                Array.Copy(sourcePixels, resultPixels, totalPixels);

                var lines = IdentifyTextLines(sourcePixels, width, height, stride);
                if (lines.Count == 0)
                {
                    lines.Add(new RowRange { Start = 0, End = height });
                }

                int rngSeed = HashCode.Combine(selectionBounds.Left, selectionBounds.Top, selectionBounds.Width, selectionBounds.Height);
                var rng = new Random(rngSeed);

                foreach (var line in lines)
                {
                    ScrambleLineColumns(sourcePixels, resultPixels, width, stride, line, rng);
                }

                Marshal.Copy(resultPixels, 0, targetData.Scan0, totalPixels);
            }
            finally
            {
                source.UnlockBits(sourceData);
                target.UnlockBits(targetData);
            }

            return target;
        }

        private List<RowRange> IdentifyTextLines(int[] pixels, int width, int height, int stride)
        {
            var lines = new List<RowRange>();
            const int separatorHeight = 5;
            int activityThreshold = Math.Max(2, width / 40);

            bool inLine = false;
            int lineStart = 0;
            int whitespaceRun = 0;

            for (int y = 0; y < height; y++)
            {
                int activity = MeasureRowActivity(pixels, y, width, stride);
                bool isWhitespace = activity <= activityThreshold;

                if (!isWhitespace)
                {
                    if (!inLine)
                    {
                        inLine = true;
                        lineStart = y;
                    }

                    whitespaceRun = 0;
                }
                else if (inLine)
                {
                    whitespaceRun++;
                    if (whitespaceRun >= separatorHeight)
                    {
                        int lineEnd = y - whitespaceRun + 1;
                        if (lineEnd > lineStart)
                        {
                            lines.Add(new RowRange { Start = lineStart, End = lineEnd });
                        }

                        inLine = false;
                    }
                }
            }

            if (inLine)
            {
                lines.Add(new RowRange { Start = lineStart, End = height });
            }

            return lines;
        }

        private List<ColumnRange> IdentifyTextColumns(int[] pixels, int width, int stride, RowRange lineRange)
        {
            var columns = new List<ColumnRange>();
            const int separatorWidth = 3;
            int startRow = Math.Max(0, lineRange.Start);
            int endRow = Math.Max(startRow + 1, lineRange.End);
            int lineHeight = Math.Max(1, endRow - startRow);
            int activityThreshold = Math.Max(2, lineHeight / 6);

            bool inRegion = false;
            int regionStart = 0;
            int whitespaceRun = 0;

            for (int x = 0; x < width; x++)
            {
                int activity = MeasureColumnActivity(pixels, x, stride, startRow, endRow);
                bool isWhitespace = activity <= activityThreshold;

                if (!isWhitespace)
                {
                    if (!inRegion)
                    {
                        inRegion = true;
                        regionStart = x;
                    }

                    whitespaceRun = 0;
                }
                else if (inRegion)
                {
                    whitespaceRun++;
                    if (whitespaceRun >= separatorWidth)
                    {
                        int regionEnd = x - whitespaceRun + 1;
                        if (regionEnd > regionStart)
                        {
                            columns.Add(new ColumnRange { Start = regionStart, End = regionEnd });
                        }

                        inRegion = false;
                    }
                }
            }

            if (inRegion)
            {
                columns.Add(new ColumnRange { Start = regionStart, End = width });
            }

            return columns;
        }

        private int MeasureRowActivity(int[] pixels, int row, int width, int stride)
        {
            int rowOffset = row * stride;
            int ink = 0;
            int transitions = 0;

            int previousLuma = GetLuminance(pixels[rowOffset]);

            for (int x = 0; x < width; x++)
            {
                int argb = pixels[rowOffset + x];
                int alpha = (argb >> 24) & 0xFF;
                if (alpha < 16)
                {
                    continue;
                }

                int luminance = GetLuminance(argb);
                if (luminance < 200)
                {
                    ink++;
                }

                if (x > 0 && Math.Abs(luminance - previousLuma) > 24)
                {
                    transitions++;
                }

                previousLuma = luminance;
            }

            return Math.Max(ink, transitions);
        }

        private int MeasureColumnActivity(int[] pixels, int column, int stride, int startRow, int endRow)
        {
            int ink = 0;
            int transitions = 0;
            bool hasPrevious = false;
            int previousLuma = 0;

            for (int y = startRow; y < endRow; y++)
            {
                int idx = y * stride + column;
                int argb = pixels[idx];
                int alpha = (argb >> 24) & 0xFF;
                if (alpha < 16)
                {
                    continue;
                }

                int luminance = GetLuminance(argb);
                if (luminance < 200)
                {
                    ink++;
                }

                if (hasPrevious && Math.Abs(luminance - previousLuma) > 24)
                {
                    transitions++;
                }

                previousLuma = luminance;
                hasPrevious = true;
            }

            return Math.Max(ink, transitions);
        }

        private static int GetLuminance(int argb)
        {
            int r = (argb >> 16) & 0xFF;
            int g = (argb >> 8) & 0xFF;
            int b = argb & 0xFF;
            return (r * 299 + g * 587 + b * 114) / 1000;
        }

        private void ScrambleLineColumns(int[] sourcePixels, int[] targetPixels, int width, int stride, RowRange line, Random rng)
        {
            int lineHeight = Math.Max(1, line.End - line.Start);

            var columnRegions = IdentifyTextColumns(sourcePixels, width, stride, line);
            if (columnRegions.Count == 0)
            {
                columnRegions.Add(new ColumnRange { Start = 0, End = width });
            }

            foreach (var range in columnRegions)
            {
                int regionStartX = Math.Max(0, range.Start);
                int regionEndX = Math.Min(width, range.End);
                int regionWidth = regionEndX - regionStartX;

                if (regionWidth <= 1)
                {
                    continue;
                }

                int blockSize = Math.Max(2, regionWidth / 24);
                blockSize = Math.Min(blockSize, 16);
                if (blockSize > regionWidth)
                {
                    blockSize = Math.Max(1, regionWidth);
                }

                int blockCount = (regionWidth + blockSize - 1) / blockSize;
                int[] order = new int[blockCount];
                for (int i = 0; i < blockCount; i++)
                {
                    order[i] = i;
                }

                for (int i = blockCount - 1; i > 0; i--)
                {
                    int swapIndex = rng.Next(i + 1);
                    int tmp = order[i];
                    order[i] = order[swapIndex];
                    order[swapIndex] = tmp;
                }

                int[] segmentBuffer = new int[lineHeight * regionWidth];
                for (int row = 0; row < lineHeight; row++)
                {
                    int sourceIndex = (line.Start + row) * stride + regionStartX;
                    Array.Copy(sourcePixels, sourceIndex, segmentBuffer, row * regionWidth, regionWidth);
                }

                int[] scrambled = new int[segmentBuffer.Length];

                for (int destBlock = 0; destBlock < blockCount; destBlock++)
                {
                    int srcBlock = order[destBlock];
                    int destStartX = destBlock * blockSize;
                    int srcStartX = srcBlock * blockSize;

                    int destWidth = Math.Min(blockSize, regionWidth - destStartX);
                    int srcWidth = Math.Min(blockSize, regionWidth - srcStartX);
                    int copyWidth = Math.Min(destWidth, srcWidth);

                    for (int row = 0; row < lineHeight; row++)
                    {
                        int destRowOffset = row * regionWidth + destStartX;
                        int srcRowOffset = row * regionWidth + srcStartX;

                        if (copyWidth > 0)
                        {
                            Array.Copy(segmentBuffer, srcRowOffset, scrambled, destRowOffset, copyWidth);
                        }

                        if (destWidth > copyWidth && srcWidth > 0)
                        {
                            int fillPixel = segmentBuffer[srcRowOffset + srcWidth - 1];
                            for (int x = copyWidth; x < destWidth; x++)
                            {
                                scrambled[destRowOffset + x] = fillPixel;
                            }
                        }
                    }
                }

                int maxJitter = regionWidth > 10 ? 2 : 0;
                if (maxJitter > 0)
                {
                    int[] tempRow = new int[regionWidth];
                    for (int row = 0; row < lineHeight; row++)
                    {
                        Array.Copy(scrambled, row * regionWidth, tempRow, 0, regionWidth);
                        int jitter = rng.Next(-maxJitter, maxJitter + 1);
                        if (jitter == 0)
                        {
                            continue;
                        }

                        for (int x = 0; x < regionWidth; x++)
                        {
                            int srcX = x + jitter;
                            if (srcX < 0)
                            {
                                srcX = 0;
                            }
                            else if (srcX >= regionWidth)
                            {
                                srcX = regionWidth - 1;
                            }

                            scrambled[row * regionWidth + x] = tempRow[srcX];
                        }
                    }
                }

                for (int row = 0; row < lineHeight; row++)
                {
                    int targetIndex = (line.Start + row) * stride + regionStartX;
                    Array.Copy(scrambled, row * regionWidth, targetPixels, targetIndex, regionWidth);
                }
            }
        }


    }
}
