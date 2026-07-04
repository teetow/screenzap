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
        // isCensorToolActive lives on ImageEditor.Tool.cs as a computed accessor.
        private bool suppressConfidenceEvents;
        private bool suppressCensorParamEvents;
        private float currentConfidenceThreshold;
        private Bitmap? censorPreviewBuffer;

        private enum CensorDirection
        {
            X = 0,
            Y = 1,
            Both = 2,
        }

        private CensorDirection censorDirection = CensorDirection.X;
        private int censorIterations = 30;
        private int censorSmear = 20;

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

            var sourceImage = pictureBox1.Image;
            var imageSize = pictureBox1.GetImagePixelSize();
            if (sourceImage == null || imageSize.IsEmpty || censorRegions.Count == 0)
            {
                return false;
            }

            using var perf = PerfTrace.Scope(
                "ImageEditor.BuildCensorPreviewBuffer",
                () => $"size={imageSize.Width}x{imageSize.Height} regions={censorRegions.Count}",
                slowMs: 80);

            var working = new Bitmap(imageSize.Width, imageSize.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(working))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(sourceImage, Point.Empty);
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
            if (censorToolStripButton != null)
            {
                censorToolStripButton.Checked = true;
            }
            SyncCensorParamControlsFromState();
            currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar?.Maximum ?? 100);
            suppressConfidenceEvents = true;

            if (confidenceTrackBar != null)
            {
                confidenceTrackBar.Value = confidenceTrackBar.Maximum;
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
            }

            suppressConfidenceEvents = false;

            if (censorToolStrip != null)
            {
                censorToolStrip.Visible = true;
                PositionOverlayToolStrips();
            }

            if (confidenceTrackBar != null)
            {
                confidenceTrackBar_ValueChanged(confidenceTrackBar, EventArgs.Empty);
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
            if (censorToolStripButton != null)
            {
                censorToolStripButton.Checked = false;
            }
            censorRegions.Clear();
            ReleaseCensorPreviewBuffer();
            currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar?.Maximum ?? 100);
            Cursor = Cursors.Default;

            suppressConfidenceEvents = true;
            if (confidenceTrackBar != null)
            {
                confidenceTrackBar.Value = confidenceTrackBar.Maximum;
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
                confidenceTrackBar.Enabled = false;
            }
            suppressConfidenceEvents = false;

            if (censorToolStrip != null)
            {
                censorToolStrip.Visible = false;
                PositionOverlayToolStrips();
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

            if (iterationsValueLabel != null)
            {
                iterationsValueLabel.Text = censorIterations.ToString(CultureInfo.InvariantCulture);
            }

            if (smearValueLabel != null)
            {
                smearValueLabel.Text = censorSmear.ToString(CultureInfo.InvariantCulture);
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

            bool paramsEnabled = isCensorToolActive && anyRegions;
            if (directionToolStripCombo != null)
            {
                directionToolStripCombo.Enabled = paramsEnabled;
            }
            if (iterationsToolStripHost != null)
            {
                iterationsToolStripHost.Enabled = paramsEnabled;
            }
            if (iterationsTrackBar != null)
            {
                iterationsTrackBar.Enabled = paramsEnabled;
            }
            if (smearToolStripHost != null)
            {
                smearToolStripHost.Enabled = paramsEnabled;
            }
            if (smearTrackBar != null)
            {
                smearTrackBar.Enabled = paramsEnabled;
            }
        }

        private void SyncCensorParamControlsFromState()
        {
            suppressCensorParamEvents = true;
            try
            {
                if (directionToolStripCombo != null)
                {
                    directionToolStripCombo.SelectedIndex = (int)censorDirection;
                }
                if (iterationsTrackBar != null)
                {
                    int value = Math.Clamp(censorIterations, iterationsTrackBar.Minimum, iterationsTrackBar.Maximum);
                    iterationsTrackBar.Value = value;
                    censorIterations = value;
                }
                if (smearTrackBar != null)
                {
                    int value = Math.Clamp(censorSmear, smearTrackBar.Minimum, smearTrackBar.Maximum);
                    smearTrackBar.Value = value;
                    censorSmear = value;
                }
            }
            finally
            {
                suppressCensorParamEvents = false;
            }
        }

        private void RebuildCensorPreviewAfterParamChange()
        {
            if (!isCensorToolActive || censorRegions.Count == 0)
            {
                return;
            }

            ShowCensorProgressIndicator();
            try
            {
                BuildCensorPreviewBuffer();
            }
            finally
            {
                HideCensorProgressIndicator();
            }

            pictureBox1.Invalidate();
        }

        private void directionToolStripCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (directionToolStripCombo == null || suppressCensorParamEvents)
            {
                return;
            }

            int index = directionToolStripCombo.SelectedIndex;
            if (index < 0)
            {
                return;
            }

            var next = (CensorDirection)index;
            if (next == censorDirection)
            {
                return;
            }

            censorDirection = next;
            RebuildCensorPreviewAfterParamChange();
        }

        private void iterationsTrackBar_ValueChanged(object? sender, EventArgs e)
        {
            if (iterationsTrackBar == null)
            {
                return;
            }

            int value = iterationsTrackBar.Value;
            if (iterationsValueLabel != null)
            {
                iterationsValueLabel.Text = value.ToString(CultureInfo.InvariantCulture);
            }

            if (suppressCensorParamEvents || value == censorIterations)
            {
                censorIterations = value;
                return;
            }

            censorIterations = value;
            RebuildCensorPreviewAfterParamChange();
        }

        private void smearTrackBar_ValueChanged(object? sender, EventArgs e)
        {
            if (smearTrackBar == null)
            {
                return;
            }

            int value = smearTrackBar.Value;
            if (smearValueLabel != null)
            {
                smearValueLabel.Text = value.ToString(CultureInfo.InvariantCulture);
            }

            if (suppressCensorParamEvents || value == censorSmear)
            {
                censorSmear = value;
                return;
            }

            censorSmear = value;
            RebuildCensorPreviewAfterParamChange();
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

        private void PushUndoStep(Rectangle region, Bitmap? before, Bitmap? after, Rectangle selectionBefore, Rectangle selectionAfter, bool replacesImage = false, List<AnnotationShape>? shapesBefore = null, List<AnnotationShape>? shapesAfter = null, List<TextAnnotation>? textsBefore = null, List<TextAnnotation>? textsAfter = null, List<ImageLayer>? layersBefore = null, List<ImageLayer>? layersAfter = null)
        {
            bool hasBitmapChange = before != null && after != null;
            bool hasShapeChange = shapesBefore != null && shapesAfter != null;
            bool hasTextChange = textsBefore != null && textsAfter != null;
            bool hasLayerChange = layersBefore != null && layersAfter != null;

            if (!hasBitmapChange && !hasShapeChange && !hasTextChange && !hasLayerChange)
            {
                before?.Dispose();
                after?.Dispose();
                DisposeOrphanedLayers(layersBefore);
                DisposeOrphanedLayers(layersAfter);
                return;
            }

            if (hasBitmapChange && !replacesImage && (region.Width <= 0 || region.Height <= 0))
            {
                before?.Dispose();
                after?.Dispose();
                DisposeOrphanedLayers(layersBefore);
                DisposeOrphanedLayers(layersAfter);
                return;
            }

            undoStack.Push(new ImageUndoStep(region, before, after, selectionBefore, selectionAfter, replacesImage, shapesBefore, shapesAfter, textsBefore, textsAfter, layersBefore, layersAfter));
            MarkDirtyAndNotify();
        }

        private static void DisposeOrphanedLayers(List<ImageLayer>? layers)
        {
            if (layers == null) return;
            foreach (var layer in layers)
            {
                layer.Dispose();
            }
        }

        private void ApplyUndoStep(IUndoStep step, bool applyAfterState)
        {
            if (step == null)
            {
                return;
            }

            if (step is TextAnnotationUndoStep textStep)
            {
                var textState = applyAfterState ? textStep.After : textStep.Before;
                ApplyTextAnnotationState(textState);
                pictureBox1?.Invalidate();
                return;
            }

            if (step is ImageUndoStep imageStep)
            {
                ApplyImageUndoStep(imageStep, applyAfterState);
            }
        }

        private void ApplyImageUndoStep(ImageUndoStep step, bool applyAfterState)
        {
            if (step == null)
            {
                return;
            }

            var source = applyAfterState ? step.After : step.Before;
            var shapeState = applyAfterState ? step.ShapesAfter : step.ShapesBefore;
            var textState = applyAfterState ? step.TextsAfter : step.TextsBefore;

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
                    RecenterViewportAfterImageChange(resizeWindow: true);
                    UpdateStatusBar();
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
            ApplyTextAnnotationState(textState);

            var layerState = applyAfterState ? step.LayersAfter : step.LayersBefore;
            ApplyLayerState(layerState);

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
            using var perf = PerfTrace.Scope(
                "ImageEditor.GenerateCensoredBitmap",
                () => $"size={source.Width}x{source.Height} dir={censorDirection} iter={censorIterations} smear={censorSmear}",
                slowMs: 40);

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

                int[] pixels = new int[totalPixels];
                Marshal.Copy(sourceData.Scan0, pixels, 0, totalPixels);

                int rngSeed = HashCode.Combine(
                    selectionBounds.Left, selectionBounds.Top,
                    selectionBounds.Width, selectionBounds.Height,
                    (int)censorDirection, censorIterations, censorSmear);
                var rng = new Random(rngSeed);

                ScrambleByNeighborSwap(pixels, width, height, stride, censorDirection, censorIterations, rng);

                if (censorSmear > 0)
                {
                    ApplyDirectionalSmear(pixels, width, height, stride, censorDirection, censorSmear);
                }

                Marshal.Copy(pixels, 0, targetData.Scan0, totalPixels);
            }
            finally
            {
                source.UnlockBits(sourceData);
                target.UnlockBits(targetData);
            }

            return target;
        }

        private static int ComputeScrambleRadius(int sliderValue, int dim)
        {
            if (sliderValue <= 0 || dim <= 1)
            {
                return 0;
            }

            float t = sliderValue / 100f;
            int radius = (int)Math.Round(t * t * dim);
            radius = Math.Max(1, radius);
            return Math.Min(radius, dim - 1);
        }

        private static int ComputeSmearRadius(int sliderValue, int dim)
        {
            if (sliderValue <= 0 || dim <= 1)
            {
                return 0;
            }

            float t = sliderValue / 100f;
            int radius = (int)Math.Round(t * t * (dim - 1));
            return Math.Min(radius, dim - 1);
        }

        private static void ScrambleByNeighborSwap(int[] pixels, int width, int height, int stride, CensorDirection direction, int sliderValue, Random rng)
        {
            if (sliderValue <= 0 || width <= 0 || height <= 0)
            {
                return;
            }

            int dim = direction switch
            {
                CensorDirection.X => width,
                CensorDirection.Y => height,
                _ => Math.Max(width, height),
            };

            int radius = ComputeScrambleRadius(sliderValue, dim);
            if (radius <= 0)
            {
                return;
            }

            const int passes = 2;
            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int dx = 0;
                        int dy = 0;

                        switch (direction)
                        {
                            case CensorDirection.X:
                                dx = rng.Next(-radius, radius + 1);
                                break;
                            case CensorDirection.Y:
                                dy = rng.Next(-radius, radius + 1);
                                break;
                            case CensorDirection.Both:
                                dx = rng.Next(-radius, radius + 1);
                                dy = rng.Next(-radius, radius + 1);
                                break;
                        }

                        int nx = Math.Clamp(x + dx, 0, width - 1);
                        int ny = Math.Clamp(y + dy, 0, height - 1);
                        int idx = rowOffset + x;
                        int neighborIdx = ny * stride + nx;

                        int tmp = pixels[idx];
                        pixels[idx] = pixels[neighborIdx];
                        pixels[neighborIdx] = tmp;
                    }
                }
            }
        }

        private static void ApplyDirectionalSmear(int[] pixels, int width, int height, int stride, CensorDirection direction, int smearPercent)
        {
            if (smearPercent <= 0 || width <= 0 || height <= 0)
            {
                return;
            }

            if (direction != CensorDirection.Y)
            {
                int rx = ComputeSmearRadius(smearPercent, width);
                if (rx > 0)
                {
                    BoxBlurHorizontal(pixels, width, height, stride, rx);
                }
            }

            if (direction != CensorDirection.X)
            {
                int ry = ComputeSmearRadius(smearPercent, height);
                if (ry > 0)
                {
                    BoxBlurVertical(pixels, width, height, stride, ry);
                }
            }
        }

        private static void BoxBlurHorizontal(int[] pixels, int width, int height, int stride, int radius)
        {
            if (radius >= width - 1)
            {
                int[] rowAverage = new int[height];
                for (int y = 0; y < height; y++)
                {
                    rowAverage[y] = ComputeRangeAverage(pixels, y * stride, width, 1);
                }
                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    int avg = rowAverage[y];
                    for (int x = 0; x < width; x++)
                    {
                        pixels[rowStart + x] = avg;
                    }
                }
                return;
            }

            int windowSize = 2 * radius + 1;
            int[] rowBuffer = new int[width];

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * stride;
                Array.Copy(pixels, rowStart, rowBuffer, 0, width);

                long sumA = 0, sumR = 0, sumG = 0, sumB = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = Math.Clamp(k, 0, width - 1);
                    int p = rowBuffer[sx];
                    sumA += (p >> 24) & 0xFF;
                    sumR += (p >> 16) & 0xFF;
                    sumG += (p >> 8) & 0xFF;
                    sumB += p & 0xFF;
                }

                for (int x = 0; x < width; x++)
                {
                    int a = (int)(sumA / windowSize);
                    int r = (int)(sumR / windowSize);
                    int g = (int)(sumG / windowSize);
                    int b = (int)(sumB / windowSize);
                    pixels[rowStart + x] = (a << 24) | (r << 16) | (g << 8) | b;

                    int leavingX = Math.Clamp(x - radius, 0, width - 1);
                    int enteringX = Math.Clamp(x + radius + 1, 0, width - 1);
                    int leaving = rowBuffer[leavingX];
                    int entering = rowBuffer[enteringX];

                    sumA += ((entering >> 24) & 0xFF) - ((leaving >> 24) & 0xFF);
                    sumR += ((entering >> 16) & 0xFF) - ((leaving >> 16) & 0xFF);
                    sumG += ((entering >> 8) & 0xFF) - ((leaving >> 8) & 0xFF);
                    sumB += (entering & 0xFF) - (leaving & 0xFF);
                }
            }
        }

        private static void BoxBlurVertical(int[] pixels, int width, int height, int stride, int radius)
        {
            if (radius >= height - 1)
            {
                int[] colAverage = new int[width];
                for (int x = 0; x < width; x++)
                {
                    colAverage[x] = ComputeRangeAverage(pixels, x, height, stride);
                }
                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        pixels[rowStart + x] = colAverage[x];
                    }
                }
                return;
            }

            int windowSize = 2 * radius + 1;
            int[] colBuffer = new int[height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    colBuffer[y] = pixels[y * stride + x];
                }

                long sumA = 0, sumR = 0, sumG = 0, sumB = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sy = Math.Clamp(k, 0, height - 1);
                    int p = colBuffer[sy];
                    sumA += (p >> 24) & 0xFF;
                    sumR += (p >> 16) & 0xFF;
                    sumG += (p >> 8) & 0xFF;
                    sumB += p & 0xFF;
                }

                for (int y = 0; y < height; y++)
                {
                    int a = (int)(sumA / windowSize);
                    int r = (int)(sumR / windowSize);
                    int g = (int)(sumG / windowSize);
                    int b = (int)(sumB / windowSize);
                    pixels[y * stride + x] = (a << 24) | (r << 16) | (g << 8) | b;

                    int leavingY = Math.Clamp(y - radius, 0, height - 1);
                    int enteringY = Math.Clamp(y + radius + 1, 0, height - 1);
                    int leaving = colBuffer[leavingY];
                    int entering = colBuffer[enteringY];

                    sumA += ((entering >> 24) & 0xFF) - ((leaving >> 24) & 0xFF);
                    sumR += ((entering >> 16) & 0xFF) - ((leaving >> 16) & 0xFF);
                    sumG += ((entering >> 8) & 0xFF) - ((leaving >> 8) & 0xFF);
                    sumB += (entering & 0xFF) - (leaving & 0xFF);
                }
            }
        }

        private static int ComputeRangeAverage(int[] pixels, int startIndex, int count, int step)
        {
            if (count <= 0)
            {
                return 0;
            }

            long sumA = 0, sumR = 0, sumG = 0, sumB = 0;
            for (int i = 0; i < count; i++)
            {
                int p = pixels[startIndex + i * step];
                sumA += (p >> 24) & 0xFF;
                sumR += (p >> 16) & 0xFF;
                sumG += (p >> 8) & 0xFF;
                sumB += p & 0xFF;
            }

            int a = (int)(sumA / count);
            int r = (int)(sumR / count);
            int g = (int)(sumG / count);
            int b = (int)(sumB / count);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }


    }
}
