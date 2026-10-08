using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using TextDetection;
using screenzap.Components;
using screenzap.Components.Shared;
using screenzap.lib;

namespace screenzap
{
    enum ResizeMode
    {
        None,
        Move,
        ResizeTL,
        ResizeT,
        ResizeTR,
        ResizeL,
        ResizeR,
        ResizeBL,
        ResizeB,
        ResizeBR
    }

    public partial class ImageDocumentEditor : IClipboardDocumentPresenter
    {
        private readonly ImageViewport viewport = new();
        private EditorCursor Cursor { get; set; } = EditorCursor.Default;
        public bool IsDisposed { get; private set; }

        private bool isTracing;
        private Keys inputModifiers;
        private Keys ModifierKeys => inputModifiers;

        private MouseButtons pointerButtons;
        private MouseButtons MouseButtons => pointerButtons;

        private EditorHostServices? hostServices;
        private readonly UndoRedo undoStack = new UndoRedo();
        private const string WindowTitleBase = "Screenzap Image Editor";
        private const int OptimizeTextBlurRadius = 100;
        private const int ExpandCanvasPaddingPixels = 8;
        private DateTime? bufferTimestamp;
        private string? currentSavePath;
        private bool isPlaceholderImage;
        private bool hasUnsavedChanges;
        private Guid committedDocumentRevision;
        private ClipboardHistoryItem? synchronizedHistoryItem;
        internal bool DocumentIsDirty { get; private set; }

        /// <summary>Invoked when the editor's content is dirtied (e.g. by an edit, tool apply or undo push).</summary>
        internal Action? ContentEditedCallback;
        private void MarkDirtyAndNotify()
        {
            deJpegRevision++;
            hasUnsavedChanges = true;
            DocumentIsDirty = true;
            NotifyDocumentContentChanged();
        }

        private void NotifyDocumentContentChanged()
        {
            synchronizedHistoryItem = null;
            try
            {
                ContentEditedCallback?.Invoke();
            }
            catch (Exception ex)
            {
                lib.Logger.Log($"ContentEditedCallback threw: {ex.Message}");
            }
        }

        private bool clipboardHasPendingReload;
        private Bitmap? internalClipboardImage;
        internal Func<bool>? ConfirmReloadWhenDirtyOverrideForDiagnostics { get; set; }
        internal Func<Image?>? ClipboardImageProviderForDiagnostics { get; set; }
        internal Func<Image, bool>? ClipboardImageWriterForDiagnostics { get; set; }
        private bool HasEditableImage => viewport.Image != null && !isPlaceholderImage;
        internal ViewportMetrics ViewportDiagnostics => viewport?.Metrics ?? default;

        private void ShowPlaceholder()
        {
            using (Bitmap bmp = new Bitmap(640, 200))
            using (Graphics gr = Graphics.FromImage(bmp))
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                var captionFont = SystemFonts.CaptionFont ?? SystemFonts.DefaultFont;
                gr.DrawString("No image data in clipboard", captionFont, Brushes.White, new PointF(320, 100), sf);
                ResetZoom();
                LoadImage(bmp, true);
            }
        }

        private Point ImageToViewport(Point pt)
        {
            return viewport?.PixelToClient(pt) ?? pt;
        }

        private Rectangle ImageToViewport(Rectangle rect)
        {
            return viewport?.PixelToClient(rect) ?? rect;
        }

        private PointF ImageToViewportF(Point pt)
        {
            return viewport?.PixelToClientF(pt) ?? new PointF(pt.X, pt.Y);
        }

        private RectangleF ImageToViewportF(Rectangle rect)
        {
            return viewport?.PixelToClientF(rect) ?? new RectangleF(rect.Location, rect.Size);
        }

        private Point ViewportToImage(Point pt)
        {
            return viewport?.ClientToPixel(pt) ?? pt;
        }

        private Rectangle GetNormalizedRect(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        /// <summary>
        /// Applies the user's transparency-checkerboard colors (persisted as ARGB ints) to the
        /// viewport. Called at construction and again when the setting changes so open editors
        /// update live.
        /// </summary>
        internal void ApplyCheckerboardColorsFromSettings()
        {
            if (viewport == null)
            {
                return;
            }

            viewport.CheckerboardLightColor = Color.FromArgb(Properties.Settings.Default.checkerboardLightColorArgb);
            viewport.CheckerboardDarkColor = Color.FromArgb(Properties.Settings.Default.checkerboardDarkColorArgb);
        }

        private void UpdateReloadIndicator() => hostServices?.SetReloadIndicator?.Invoke(clipboardHasPendingReload);
        private void ClearClipboardNotification()
        {
            clipboardHasPendingReload = false;
            UpdateReloadIndicator();
        }

        public ImageDocumentEditor()
        {
            viewport.Invalidated += (_, _) => SurfaceInvalidated?.Invoke();
            viewport.OverlayPaint += ViewportPaint;
            viewport.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            ApplyCheckerboardColorsFromSettings();
            var settings = Properties.Settings.Default;
            textToolFontFamily = settings.textToolFontFamily;
            textToolFontVariant = string.IsNullOrEmpty(settings.textToolFontVariant) ? null : settings.textToolFontVariant;
            textToolFontSize = settings.textToolFontSize;
            textToolFontStyle = (FontStyle)settings.textToolFontStyle;
            textToolColor = Color.FromArgb(settings.textToolColorArgb);
            textToolOutlineThickness = settings.textToolOutlineThickness;
            textToolOutlineColor = Color.FromArgb(settings.textToolOutlineColorArgb);
            ShowPlaceholder();
        }

        public ImageDocumentEditor(Image image) : this()
        {
            LoadImage(image);
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            undoStack.Dispose();
            ClearImageLayers();
            ReleaseCensorPreviewBuffer();
            internalClipboardImage?.Dispose();
            viewport.Image?.Dispose();
            viewport.Dispose();
            IsDisposed = true;
        }

        internal void LoadImage(Image? imgData)
        {
            LoadImage(imgData, false);
        }

        internal void LoadImage(Image? imgData, bool treatAsPlaceholder) => LoadImage(imgData, treatAsPlaceholder, preserveView: false);
        private void LoadImage(Image? imgData, bool treatAsPlaceholder, bool preserveView)
        {
            if (imgData == null)
                return;
            synchronizedHistoryItem = null;
            LogViewportDebug($"=== LoadImage START: imgData.Size={imgData.Size}, treatAsPlaceholder={treatAsPlaceholder} ===");
            LogViewportDebug($"LoadImage: current viewport.ClientSize={viewport.ClientSize}, panOffset={viewport.Metrics.PanOffset}");
            var previousView = viewport.Metrics;
            bool previousAlphaView = viewport.AlphaViewEnabled;
            deJpegRevision++;
            isPlaceholderImage = treatAsPlaceholder;
            bufferTimestamp = treatAsPlaceholder ? (DateTime? )null : (ClipboardMetadata.LastCaptureTimestamp ?? DateTime.Now);
            currentSavePath = null;
            DeactivateCensorTool(false);
            DeactivateStraightenTool(false);
            ClearSelection();
            LogViewportDebug($"LoadImage: About to call ResetZoom");
            ResetZoom();
            LogViewportDebug($"LoadImage: After ResetZoom, panOffset={viewport.Metrics.PanOffset}");
            annotationShapes.Clear();
            workingAnnotation = null;
            selectedAnnotation = null;
            activeAnnotationHandle = AnnotationHandle.None;
            annotationSnapshotBeforeEdit = null;
            annotationChangedDuringDrag = false;
            isDrawingAnnotation = false;
            activeDrawingTool = DrawingTool.None;
            annotationTranslateModeActive = false;
            annotationDraftAnchorPixel = Point.Empty;
            // Reset image layers (smart objects). Disposes owned bitmaps.
            ClearImageLayers();
            // Reset text annotations
            textAnnotations.Clear();
            activeTextAnnotation = null;
            selectedTexts.Clear();
            selectedTextAnnotation = null;
            isTextToolActive = false;
            textAnnotationSnapshotBeforeEdit = null;
            textAnnotationChangedDuringDrag = false;
            isTextAnnotationDragging = false;
            var replacementImage = new Bitmap(imgData);
            var oldImageSize = viewport.GetImagePixelSize();
            LogViewportDebug($"LoadImage: new image size={replacementImage.Size}, viewport.ClientSize={viewport.ClientSize}");
            LogViewportDebug($"LoadImage: old image size={oldImageSize}");
            LogViewportDebug($"LoadImage: About to set viewport.Image");
            viewport.Image = replacementImage;
            LogViewportDebug($"LoadImage: After Image setter, panOffset={viewport.Metrics.PanOffset}");
            LogViewportDebug($"LoadImage: Calling explicit CenterImage");
            viewport.CenterImage();
            LogViewportDebug($"LoadImage: After CenterImage, panOffset={viewport.Metrics.PanOffset}");
            _zoomlevel = viewport.ZoomLevel;
            // Only resize window when not hosted (standalone mode)
            LogViewportDebug($"LoadImage: Calling HandleResize");
            HandleResize();
            LogViewportDebug($"LoadImage: After HandleResize, panOffset={viewport.Metrics.PanOffset}");
            LogViewportDebug($"LoadImage: === END ===");
            if (preserveView)
            {
                viewport.RestoreView(previousView.ZoomLevel, previousView.PanOffset, previousAlphaView);
                _zoomlevel = viewport.ZoomLevel;
            }

            undoStack.Clear();
            hasUnsavedChanges = false;
            DocumentIsDirty = false;
            committedDocumentRevision = undoStack.CurrentRevision;
            ClearClipboardNotification();
        }

        // File output is gated behind ImageViewport.EnableFileLogging (one switch for all
        // viewport diagnostics) — unconditional appends here grew viewport-debug.log by ~15 lines
        // per image load in release builds, reaching 100+ MB.
        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogViewportDebug(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [ImageDocumentEditor] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            if (!ImageViewport.EnableFileLogging)
            {
                return;
            }

            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screenzap", "viewport-debug.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        internal void ResetZoom()
        {
            ZoomLevel = 1;
            viewport?.CenterImage();
        }

        internal void HandleResize() => viewport.ClampPan();
        private void ClampImageLocationWithinCanvas()
        {
            viewport?.ClampPan();
        }

        private void RecenterViewportAfterImageChange(bool resizeWindow) => RealignViewportAfterCanvasMutation();
        private void RealignViewportAfterCanvasMutation()
        {
            viewport.CenterImage();
            viewport.Invalidate();
        }

        private Rectangle GetImageBounds()
        {
            var imageSize = viewport.GetImagePixelSize();
            return imageSize.IsEmpty ? Rectangle.Empty : new Rectangle(Point.Empty, imageSize);
        }

        private Rectangle ClampToImage(Rectangle region)
        {
            var imageSize = viewport.GetImagePixelSize();
            if (imageSize.IsEmpty)
            {
                return Rectangle.Empty;
            }

            var bounds = GetImageBounds();
            var intersection = Rectangle.Intersect(bounds, region);
            return intersection;
        }

        private bool ExecuteClearPixels()
        {
            if (!HasEditableImage || viewport.Image == null)
                return false;
            var region = Selection.IsEmpty ? GetImageBounds() : ClampToImage(Selection);
            if (region.Width <= 0 || region.Height <= 0)
                return false;
            var before = CaptureRegion(region);
            if (before == null)
                return false;
            var after = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(viewport.Image))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.FillRectangle(Brushes.Transparent, region);
            }

            PushUndoStep(region, before, after, Selection, Selection);
            viewport.Invalidate();
            return true;
        }

        private bool ExecuteReplaceWithBackground()
        {
            if (!HasEditableImage || Selection.IsEmpty)
            {
                return false;
            }

            var imageSize = viewport.GetImagePixelSize();
            if (imageSize.IsEmpty)
            {
                return false;
            }

            var clampedSelection = ClampToImage(Selection);
            if (clampedSelection.Width <= 0 || clampedSelection.Height <= 0)
            {
                return false;
            }

            var sourceEdges = ReplaceBackgroundInterpolation.DetermineSourceEdges(clampedSelection, imageSize);
            if (!sourceEdges.HasAnySource)
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
                after = new Bitmap(before.Width, before.Height, PixelFormat.Format32bppArgb);
                Rectangle lockRect = new Rectangle(0, 0, before.Width, before.Height);
                var sourceData = before.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var targetData = after.LockBits(lockRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stridePixels = sourceData.Stride / 4;
                    int width = before.Width;
                    int height = before.Height;
                    int totalPixels = stridePixels * height;
                    int[] sourcePixels = new int[totalPixels];
                    Marshal.Copy(sourceData.Scan0, sourcePixels, 0, totalPixels);
                    int[] workingPixels = new int[totalPixels];
                    Array.Copy(sourcePixels, workingPixels, totalPixels);
                    bool[] filled = new bool[totalPixels];
                    Queue<Point> queue = new Queue<Point>();
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int idx = y * stridePixels + x;
                            bool isBorder = (x == 0 && sourceEdges.UseLeft) || (y == 0 && sourceEdges.UseTop) || (x == width - 1 && sourceEdges.UseRight) || (y == height - 1 && sourceEdges.UseBottom);
                            filled[idx] = isBorder;
                            if (isBorder)
                            {
                                queue.Enqueue(new Point(x, y));
                            }
                        }
                    }

                    int[] offsets = new[]
                    {
                        -1,
                        1,
                        -stridePixels,
                        stridePixels
                    };
                    while (queue.Count > 0)
                    {
                        var pt = queue.Dequeue();
                        int baseIdx = pt.Y * stridePixels + pt.X;
                        foreach (var offset in offsets)
                        {
                            int neighborIdx = baseIdx + offset;
                            int nx = pt.X;
                            int ny = pt.Y;
                            if (offset == -1)
                                nx = pt.X - 1;
                            else if (offset == 1)
                                nx = pt.X + 1;
                            else if (offset == -stridePixels)
                                ny = pt.Y - 1;
                            else if (offset == stridePixels)
                                ny = pt.Y + 1;
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                            {
                                continue;
                            }

                            int idx = ny * stridePixels + nx;
                            if (!filled[idx])
                            {
                                workingPixels[idx] = workingPixels[baseIdx];
                                filled[idx] = true;
                                queue.Enqueue(new Point(nx, ny));
                            }
                        }
                    }

                    int[] tempPixels = new int[totalPixels];
                    Array.Copy(workingPixels, tempPixels, totalPixels);
                    int blurIterations = Math.Max(Math.Max(width, height) / 8, 3);
                    blurIterations = Math.Min(blurIterations, 20);
                    for (int iteration = 0; iteration < blurIterations; iteration++)
                    {
                        Array.Copy(workingPixels, tempPixels, totalPixels);
                        for (int y = 1; y < height - 1; y++)
                        {
                            for (int x = 1; x < width - 1; x++)
                            {
                                int idx = y * stridePixels + x;
                                int a = 0, r = 0, g = 0, b = 0, count = 0;
                                int[] neighborOffsets =
                                {
                                    0,
                                    -1,
                                    1,
                                    -stridePixels,
                                    stridePixels
                                };
                                foreach (var neighbor in neighborOffsets)
                                {
                                    int nIdx = idx + neighbor;
                                    int color = workingPixels[nIdx];
                                    b += color & 0xFF;
                                    g += (color >> 8) & 0xFF;
                                    r += (color >> 16) & 0xFF;
                                    a += (color >> 24) & 0xFF;
                                    count++;
                                }

                                int newColor = ((a / count) << 24) | ((r / count) << 16) | ((g / count) << 8) | (b / count);
                                tempPixels[idx] = newColor;
                            }
                        }

                        Array.Copy(tempPixels, workingPixels, totalPixels);
                    }

                    Marshal.Copy(workingPixels, 0, targetData.Scan0, totalPixels);
                }
                finally
                {
                    before.UnlockBits(sourceData);
                    after.UnlockBits(targetData);
                }

                var targetImage = viewport.Image;
                if (targetImage == null)
                {
                    return false;
                }

                using (var gImg = Graphics.FromImage(targetImage))
                {
                    gImg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    gImg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    gImg.DrawImage(after, clampedSelection);
                }

                PushUndoStep(clampedSelection, before, after, selectionBefore, Selection);
                viewport.Invalidate();
                return true;
            }
            catch
            {
                throw;
            }
        }

        private bool ExecuteOptimizeForText()
        {
            if (!HasEditableImage)
            {
                return false;
            }

            var targetRegion = Selection.IsEmpty ? GetImageBounds() : ClampToImage(Selection);
            if (targetRegion.Width <= 0 || targetRegion.Height <= 0)
            {
                return false;
            }

            var selectionBefore = Selection;
            var before = CaptureRegion(targetRegion);
            if (before == null)
            {
                return false;
            }

            Bitmap? after = null;
            try
            {
                after = CreateOptimizedForTextCopy(before);
                var targetImage = viewport.Image;
                if (targetImage == null)
                {
                    return false;
                }

                using (var gImg = Graphics.FromImage(targetImage))
                {
                    gImg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    gImg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    gImg.DrawImage(after, targetRegion);
                }

                PushUndoStep(targetRegion, before, after, selectionBefore, Selection);
                viewport.Invalidate();
                return true;
            }
            catch
            {
                throw;
            }
        }

        private bool ExecuteStraighten()
        {
            return ActivateStraightenTool();
        }

        private bool ExecuteExpandCanvas()
        {
            if (!HasEditableImage || viewport.Image == null)
            {
                return false;
            }

            var sourceImage = viewport.Image;
            if (sourceImage.Width <= 0 || sourceImage.Height <= 0)
            {
                return false;
            }

            var padding = ExpandCanvasPaddingPixels;
            var beforeImage = new Bitmap(sourceImage);
            var selectionBefore = Selection;
            var annotationStateBefore = CloneAnnotations();
            var textStateBefore = CloneTextAnnotations();
            var layerStateBefore = CloneLayers();
            var expandedWidth = sourceImage.Width + (padding * 2);
            var expandedHeight = sourceImage.Height + (padding * 2);
            var afterSnapshot = new Bitmap(expandedWidth, expandedHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(afterSnapshot))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(sourceImage, new Rectangle(padding, padding, sourceImage.Width, sourceImage.Height), new Rectangle(0, 0, sourceImage.Width, sourceImage.Height), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(0, padding, padding, sourceImage.Height), new Rectangle(0, 0, 1, sourceImage.Height), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(padding + sourceImage.Width, padding, padding, sourceImage.Height), new Rectangle(sourceImage.Width - 1, 0, 1, sourceImage.Height), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(padding, 0, sourceImage.Width, padding), new Rectangle(0, 0, sourceImage.Width, 1), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(padding, padding + sourceImage.Height, sourceImage.Width, padding), new Rectangle(0, sourceImage.Height - 1, sourceImage.Width, 1), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(0, 0, padding, padding), new Rectangle(0, 0, 1, 1), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(padding + sourceImage.Width, 0, padding, padding), new Rectangle(sourceImage.Width - 1, 0, 1, 1), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(0, padding + sourceImage.Height, padding, padding), new Rectangle(0, sourceImage.Height - 1, 1, 1), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(padding + sourceImage.Width, padding + sourceImage.Height, padding, padding), new Rectangle(sourceImage.Width - 1, sourceImage.Height - 1, 1, 1), GraphicsUnit.Pixel);
            }

            var newImage = new Bitmap(afterSnapshot);
            var currentZoom = ZoomLevel;
            viewport.Image = newImage;
            ZoomLevel = currentZoom;
            viewport.ClampPan();
            RecenterViewportAfterImageChange(resizeWindow: true);
            var offset = new Point(padding, padding);
            if (!Selection.IsEmpty)
            {
                Selection = new Rectangle(Selection.Location.Add(offset), Selection.Size);
            }

            for (int index = 0; index < annotationShapes.Count; index++)
            {
                var shape = annotationShapes[index];
                shape.Start = shape.Start.Add(offset);
                shape.End = shape.End.Add(offset);
            }

            for (int index = 0; index < textAnnotations.Count; index++)
            {
                var annotation = textAnnotations[index];
                annotation.Position = annotation.Position.Add(offset);
            }

            // Floating image layers hold absolute canvas coordinates, so they must move with the
            // content by the same top-left offset (same fix as cropping — see ApplyCropToImageLayers).
            for (int index = 0; index < imageLayers.Count; index++)
            {
                var layer = imageLayers[index];
                var frame = layer.Frame;
                frame.X += offset.X;
                frame.Y += offset.Y;
                layer.Frame = frame;
            }

            SyncSelectedAnnotation();
            SyncSelectedTextAnnotation();
            var selectionAfter = Selection;
            var annotationStateAfter = CloneAnnotations();
            var textStateAfter = CloneTextAnnotations();
            var layerStateAfter = CloneLayers();
            PushUndoStep(Rectangle.Empty, beforeImage, afterSnapshot, selectionBefore, selectionAfter, true, annotationStateBefore, annotationStateAfter, textStateBefore, textStateAfter, layerStateBefore, layerStateAfter);
            viewport.Invalidate();
            return true;
        }

        private static Bitmap CreateOptimizedForTextCopy(Bitmap source)
        {
            using var perf = PerfTrace.Scope("ImageDocumentEditor.CreateOptimizedForTextCopy", () => $"size={source.Width}x{source.Height} blurRadius={OptimizeTextBlurRadius}", slowMs: 60);
            using var original = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(original))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            var rect = new Rectangle(0, 0, original.Width, original.Height);
            var data = original.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int length = Math.Abs(stride) * original.Height;
                var sourceBytes = new byte[length];
                Marshal.Copy(data.Scan0, sourceBytes, 0, length);
                var blurred = ApplyGaussianBlur(sourceBytes, original.Width, original.Height, stride, OptimizeTextBlurRadius);
                var output = new byte[length];
                for (int i = 0; i < length; i += 4)
                {
                    byte srcB = sourceBytes[i];
                    byte srcG = sourceBytes[i + 1];
                    byte srcR = sourceBytes[i + 2];
                    int outB = (srcB * 255) / Math.Max(1, (int)blurred[i]);
                    int outG = (srcG * 255) / Math.Max(1, (int)blurred[i + 1]);
                    int outR = (srcR * 255) / Math.Max(1, (int)blurred[i + 2]);
                    output[i] = (byte)Math.Min(255, outB);
                    output[i + 1] = (byte)Math.Min(255, outG);
                    output[i + 2] = (byte)Math.Min(255, outR);
                    output[i + 3] = sourceBytes[i + 3];
                }

                TextToneNormalizer.Normalize(output);
                var result = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                var resultData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(output, 0, resultData.Scan0, length);
                }
                finally
                {
                    result.UnlockBits(resultData);
                }

                return result;
            }
            finally
            {
                original.UnlockBits(data);
            }
        }

        private static byte[] ApplyGaussianBlur(byte[] source, int width, int height, int stride, int radius)
        {
            using var perf = PerfTrace.Scope("ImageDocumentEditor.ApplyGaussianBlur", () => $"size={width}x{height} radius={radius}", slowMs: 40);
            var kernel = BuildGaussianKernel(radius);
            int pixelCount = width * height;
            var tempB = new float[pixelCount];
            var tempG = new float[pixelCount];
            var tempR = new float[pixelCount];
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                int rowIndex = y * width;
                for (int x = 0; x < width; x++)
                {
                    float sumB = 0f;
                    float sumG = 0f;
                    float sumR = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = Math.Clamp(x + k, 0, width - 1);
                        int idx = rowOffset + (sx * 4);
                        float weight = kernel[k + radius];
                        sumB += source[idx] * weight;
                        sumG += source[idx + 1] * weight;
                        sumR += source[idx + 2] * weight;
                    }

                    int tempIndex = rowIndex + x;
                    tempB[tempIndex] = sumB;
                    tempG[tempIndex] = sumG;
                    tempR[tempIndex] = sumR;
                }
            }

            var blurred = new byte[source.Length];
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    float sumB = 0f;
                    float sumG = 0f;
                    float sumR = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = Math.Clamp(y + k, 0, height - 1);
                        int tempIndex = (sy * width) + x;
                        float weight = kernel[k + radius];
                        sumB += tempB[tempIndex] * weight;
                        sumG += tempG[tempIndex] * weight;
                        sumR += tempR[tempIndex] * weight;
                    }

                    int idx = rowOffset + (x * 4);
                    blurred[idx] = ClampToByte(sumB);
                    blurred[idx + 1] = ClampToByte(sumG);
                    blurred[idx + 2] = ClampToByte(sumR);
                    blurred[idx + 3] = source[idx + 3];
                }
            }

            return blurred;
        }

        private static float[] BuildGaussianKernel(int radius)
        {
            int size = (radius * 2) + 1;
            var kernel = new float[size];
            double sigma = Math.Max(1.0, radius / 3.0);
            double twoSigmaSquared = 2.0 * sigma * sigma;
            double sum = 0.0;
            for (int i = -radius; i <= radius; i++)
            {
                double value = Math.Exp(-(i * i) / twoSigmaSquared);
                kernel[i + radius] = (float)value;
                sum += value;
            }

            if (sum > 0.0)
            {
                for (int i = 0; i < size; i++)
                {
                    kernel[i] = (float)(kernel[i] / sum);
                }
            }

            return kernel;
        }

        private static byte ClampToByte(float value)
        {
            if (value <= 0f)
            {
                return 0;
            }

            if (value >= 255f)
            {
                return 255;
            }

            return (byte)(value + 0.5f);
        }

        private bool ExecuteCrop()
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
            var selectionAfter = Rectangle.Empty;
            var annotationStateBefore = CloneAnnotations();
            var textStateBefore = CloneTextAnnotations();
            var layerStateBefore = CloneLayers();
            var sourceImage = viewport.Image;
            if (sourceImage == null)
            {
                return false;
            }

            var beforeImage = new Bitmap(sourceImage);
            Bitmap afterSnapshot = new Bitmap(clampedSelection.Width, clampedSelection.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(afterSnapshot))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(sourceImage, new Rectangle(Point.Empty, clampedSelection.Size), clampedSelection, GraphicsUnit.Pixel);
            }

            var newImage = new Bitmap(afterSnapshot);
            var currentZoom = ZoomLevel;
            viewport.Image = newImage;
            ZoomLevel = currentZoom;
            viewport.ClampPan();
            RecenterViewportAfterImageChange(resizeWindow: true);
            Selection = selectionAfter;
            isPlaceholderImage = false;
            ApplyCropToAnnotations(clampedSelection.Location, clampedSelection.Size);
            ApplyCropToTextAnnotations(clampedSelection.Location, clampedSelection.Size);
            ApplyCropToImageLayers(clampedSelection.Location, clampedSelection.Size);
            var annotationStateAfter = CloneAnnotations();
            var textStateAfter = CloneTextAnnotations();
            var layerStateAfter = CloneLayers();
            PushUndoStep(Rectangle.Empty, beforeImage, afterSnapshot, selectionBefore, selectionAfter, true, annotationStateBefore, annotationStateAfter, textStateBefore, textStateAfter, layerStateBefore, layerStateAfter);
            viewport.Invalidate();
            return true;
        }

        internal bool ExecuteCropForDiagnostics()
        {
            return ExecuteCrop();
        }

        private string BuildDefaultSavePath()
        {
            var folder = Environment.ExpandEnvironmentVariables(Properties.Settings.Default.captureFolder);
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch
            {
                folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                Directory.CreateDirectory(folder);
            }

            var timestamp = (bufferTimestamp ?? DateTime.Now).ToString("yyyy-MM-ddTHH-mm-ss");
            return Path.Combine(folder, $"{timestamp}.png");
        }

        private string EnsureUniquePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string filename = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            int counter = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(directory, $"{filename}_{counter}{extension}");
                counter++;
            }
            while (File.Exists(candidate));
            return candidate;
        }

        /// <summary>The live overlay, copied. See <see cref = "DocumentOverlay"/> for why it is one object.</summary>
        private DocumentOverlay CloneOverlay()
        {
            return new DocumentOverlay
            {
                Shapes = CloneAnnotations(),
                Texts = CloneTextAnnotations(),
                Layers = CloneLayers(),
            };
        }

        /// <summary>
        /// Restore an overlay into the editor. Null means "not tracked, leave the live state
        /// alone" — the same convention each part already used individually.
        /// </summary>
        private void ApplyOverlay(DocumentOverlay? source)
        {
            if (source == null)
            {
                return;
            }

            ApplyAnnotationState(source.Shapes);
            ApplyTextAnnotationState(source.Texts);
            ApplyLayerState(source.Layers);
        }

        private Bitmap BuildCompositeImage()
        {
            if (viewport.Image == null)
            {
                throw new InvalidOperationException("No image is currently loaded.");
            }

            using var perf = PerfTrace.Scope("ImageDocumentEditor.BuildCompositeImage", () => $"size={viewport.Image.Width}x{viewport.Image.Height} shapes={annotationShapes.Count} text={textAnnotations.Count} layers={imageLayers.Count}", slowMs: 50);
            var composite = new Bitmap(viewport.Image);
            if (annotationShapes.Count == 0 && textAnnotations.Count == 0 && imageLayers.Count == 0)
            {
                return composite;
            }

            using (var graphics = Graphics.FromImage(composite))
            {
                DrawImageLayers(graphics, AnnotationSurface.Image);
                DrawAnnotations(graphics, AnnotationSurface.Image);
                DrawTextAnnotations(graphics, AnnotationSurface.Image);
            }

            return composite;
        }

        private bool PersistImage(string targetPath)
        {
            if (!HasEditableImage)
            {
                return false;
            }

            try
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var bmp = BuildCompositeImage())
                {
                    bmp.Save(targetPath, ImageFormat.Png);
                }

                return true;
            }
            catch (Exception ex)
            {
                NotifySurface($"Failed to save image.\n{ex.Message}", "Save failed");
                return false;
            }
        }

        private bool ExecuteSave()
        {
            if (!HasEditableImage)
            {
                return false;
            }

            var targetPath = currentSavePath;
            bool generatedPath = false;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = EnsureUniquePath(BuildDefaultSavePath());
                generatedPath = true;
            }

            if (PersistImage(targetPath))
            {
                currentSavePath = targetPath;
                hasUnsavedChanges = false;
                return true;
            }

            if (generatedPath)
            {
                currentSavePath = null;
            }

            return false;
        }

        internal bool ExecuteSaveForDiagnostics()
        {
            return ExecuteSave();
        }

        internal bool ExecuteSaveAsForDiagnostics(string targetPath) => SaveImageAs(targetPath);
        private bool SaveImageAs(string targetPath)
        {
            if (!HasEditableImage || string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            if (!PersistImage(targetPath))
            {
                return false;
            }

            currentSavePath = targetPath;
            hasUnsavedChanges = false;
            return true;
        }

        internal bool ClipboardHasPendingReloadForDiagnostics => clipboardHasPendingReload;

        internal void SetPendingReloadForDiagnostics(bool hasPendingReload, bool useTextTarget)
        {
            clipboardHasPendingReload = hasPendingReload;
            UpdateReloadIndicator();
        }

        internal void SetHasUnsavedChangesForDiagnostics(bool hasUnsavedChangesValue)
        {
            hasUnsavedChanges = hasUnsavedChangesValue;
        }

        internal void ReloadFromClipboardForDiagnostics()
        {
            ReloadFromClipboard();
        }

        internal bool CopySelectionToClipboardForDiagnostics()
        {
            return CopySelectionToClipboard();
        }

        internal bool PasteFromClipboardForDiagnostics()
        {
            return TryPasteImageFromClipboard();
        }

        internal void SetInternalClipboardImageForDiagnostics(Bitmap source)
        {
            internalClipboardImage = source == null ? null : new Bitmap(source);
        }

        private bool HandleNavigationKey(Keys keyData)
        {
            if (activeTextAnnotation?.IsEditing == true && (keyData & Keys.KeyCode)is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End)
            {
                var args = new KeyEventArgs(keyData);
                HandleTextToolKeyDown(args);
                if (args.Handled)
                    return true;
            }

            return (keyData & Keys.KeyCode)is Keys.Left or Keys.Right or Keys.Up or Keys.Down && (TryHandleAnnotationArrowKey(keyData) || TryHandleLayerArrowKey(keyData) || TryHandleMarqueeArrowKey(keyData));
        }

        /// <summary>
        /// True when this key press will produce a typed character: no modifier at all (Shift is
        /// part of typing), or AltGr — which Windows reports as Ctrl+Alt and which produces real
        /// characters on international layouts (Swedish AltGr+E is €, and Ctrl+E is the censor
        /// tool). Ctrl alone or Alt alone never yields a character.
        /// </summary>
        private static bool ProducesTextCharacter(KeyEventArgs e)
        {
            bool ctrl = (e.Modifiers & Keys.Control) == Keys.Control;
            bool alt = (e.Modifiers & Keys.Alt) == Keys.Alt;
            return ctrl == alt;
        }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            // Modal tools own the keyboard: while straighten/censor/free-rotate is engaged only
            // their confirm/cancel (and censor's select-all) keys act, and everything else is
            // swallowed. These run BEFORE the text/annotation handlers so a selection left
            // behind under the modal overlay can't eat Delete or Escape.
            if (isFreeRotateToolActive)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DeactivateFreeRotateTool(false);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Enter)
                {
                    DeactivateFreeRotateTool(true);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                return;
            }

            if (isStraightenToolActive)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DeactivateStraightenTool(false);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Enter)
                {
                    DeactivateStraightenTool(true);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                return;
            }

            if (isCensorToolActive)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DeactivateCensorTool(false);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Enter)
                {
                    DeactivateCensorTool(true);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.E && e.Control)
                {
                    DeactivateCensorTool(true);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.A && e.Modifiers == Keys.Control)
                {
                    SelectAllCensorRegions();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                return;
            }

            // Multi-select Delete: when the user has selected more than one annotation
            // (any mix of shapes and texts) and isn't editing text, remove the whole
            // selection in a single combined undo step. Handled before HandleTextToolKeyDown
            // because the single-target text path would otherwise intercept Delete and
            // leave the shape side of a mixed selection behind.
            if (e.KeyCode == Keys.Delete && (selectedShapes.Count + selectedTexts.Count) > 1 && (activeTextAnnotation == null || !activeTextAnnotation.IsEditing))
            {
                DeleteMultiSelection();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            // Handle text tool keyboard input first
            if (HandleTextToolKeyDown(e))
            {
                return;
            }

            // Still editing an on-canvas text annotation? Then whatever the text editor did not
            // claim above is a CHARACTER, not a shortcut. The document shortcuts below set
            // SuppressKeyPress, which kills the WM_CHAR before OnKeyPress can insert it — that is
            // why the bare-M grid toggle made "m" impossible to type. Ctrl-only / Alt-only combos
            // produce no character and still fall through to the shortcuts.
            if (activeTextAnnotation?.IsEditing == true && ProducesTextCharacter(e))
            {
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                // Unified ladder: each press steps out ONE level — in-flight gesture →
                // active tool → selection → nothing. Leaving a MODE outranks dropping a
                // selection: from inside the text editor, Esc leaves editing (handled in
                // HandleTextToolKeyDown) and the very next Esc puts the text tool away,
                // rather than spending a press deselecting first. (Censor/straighten Esc
                // lives in the modal blocks above.)
                if (isDrawingAnnotation)
                {
                    CancelAnnotationPreview();
                    SelectAnnotation(null);
                    viewport.Invalidate();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (isTextToolActive)
                {
                    FinalizeActiveTextAnnotation();
                    isTextToolActive = false;
                    viewport.Invalidate();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (activeDrawingTool != DrawingTool.None)
                {
                    activeDrawingTool = DrawingTool.None;
                    viewport.Invalidate();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (selectedShapes.Count > 0 || selectedTexts.Count > 0)
                {
                    SelectAnnotation(null);
                    SelectTextAnnotation(null);
                    activeTextAnnotation = null;
                    viewport.Invalidate();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (DeselectImageLayerIfAny())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }
            }

            if (e.KeyCode == Keys.Delete && HasSelectedLayer)
            {
                if (TryDeleteSelectedLayer())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }
            }

            if (e.KeyCode == Keys.Delete && selectedAnnotation != null)
            {
                var before = CloneAnnotations();
                annotationShapes.Remove(selectedAnnotation);
                SelectAnnotation(null);
                var after = CloneAnnotations();
                PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false, before, after);
                viewport.Invalidate();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None && ExecuteClearPixels())
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Space)
            {
                if (isDrawingAnnotation && workingAnnotation != null)
                {
                    if (BeginAnnotationTranslation())
                    {
                        e.SuppressKeyPress = true;
                        e.Handled = true;
                    }
                }
                else if (isDrawingRubberBand)
                {
                    isMovingSelection = true;
                    if (viewport != null)
                    {
                        var cursorInViewport = CurrentPointerInViewport();
                        MoveInPixel = ViewportToImage(cursorInViewport);
                    }
                    else
                    {
                        MoveInPixel = MouseOutPixel;
                    }
                }

                return;
            }

            if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && HasSelectedLayer)
            {
                if (ApplyFloatingPaste())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }
            }
            else if (e.KeyCode == Keys.C && e.Control == true)
            {
                CopySelectionToClipboard();
            }
            else if (e.KeyCode == Keys.A && e.Modifiers == Keys.Control)
            {
                // Censor-mode Ctrl+A (select all regions) is handled in the modal block above.
                if (HasEditableImage)
                {
                    Selection = GetImageBounds();
                    viewport?.Invalidate();
                }

                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.V && e.Control == true)
            {
                if (TryPasteImageFromClipboard())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.E && e.Modifiers == (Keys.Control | Keys.Shift))
            {
                if (ExecuteExpandCanvas())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.E && e.Control == true)
            {
                if (ActivateCensorTool())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if ((e.KeyCode == Keys.B && e.Control) || (e.KeyCode == Keys.Back && e.Modifiers == Keys.None))
            {
                if (ExecuteReplaceWithBackground())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.S && e.Control == true)
            {
                bool handled = false;
                if ((e.Modifiers & Keys.Shift) == Keys.Shift)
                {
                    handled = false;
                }
                else
                {
                    handled = ExecuteSave();
                }

                if (handled)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.R && e.Control)
            {
                ReloadFromClipboard();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.T && e.Control)
            {
                if (ExecuteCrop())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.L && e.Control)
            {
                if (ExecuteStraighten())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.Z)
            {
                bool handled = false;
                if (e.Modifiers == (Keys.Control | Keys.Shift))
                {
                    var redoStep = undoStack.Redo();
                    if (redoStep != null)
                    {
                        ApplyUndoStep(redoStep, true);
                        handled = true;
                    }
                }
                else if (e.Modifiers == Keys.Control)
                {
                    CompletePendingDocumentEdits();
                    var undoStep = undoStack.Undo();
                    if (undoStep != null)
                    {
                        ApplyUndoStep(undoStep, false);
                        handled = true;
                    }
                }

                if (handled)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.M && e.Modifiers == Keys.None)
            {
                if (HasEditableImage)
                {
                    ToggleAlphaView();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
        }

        private void ToggleAlphaView()
        {
            viewport.AlphaViewEnabled = !viewport.AlphaViewEnabled;
        }

        private void HandleKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                isMovingSelection = false;
                annotationTranslateModeActive = false;
            }

            // Releasing the arrow key closes the annotation move/resize gesture, so a held
            // key's auto-repeat collapses into one undo step.
            if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
            {
                EndAnnotationKeyTransform();
                EndLayerKeyTransform();
            }

            // Close keyboard-initiated stamp/clone gestures when their modifier is released.
            // Mouse-initiated gestures are closed by MouseUp instead — while a drag is in
            // flight (button held) the modifier may be released and re-pressed freely.
            if ((mouseButtons_TestOverride ?? MouseButtons) == MouseButtons.None)
            {
                if (e.KeyCode == Keys.ControlKey && isCtrlStampingSelection)
                {
                    EndSelectionStampGesture();
                }

                if (e.KeyCode == Keys.Menu && isAltCloningSelection)
                {
                    EndSelectionCloneGesture();
                }
            }
        }

        private bool ExecuteRotate90Cw()
        {
            if (!HasEditableImage || viewport.Image == null)
            {
                return false;
            }

            // When a selection is active, rotate only the selected region in-place.
            if (!Selection.IsEmpty)
            {
                return ExecuteRotateSelection90Cw();
            }

            var beforeImage = new Bitmap(viewport.Image);
            var selectionBefore = Selection;
            var annotationStateBefore = CloneAnnotations();
            int width = viewport.Image.Width;
            int height = viewport.Image.Height;
            var rotated = new Bitmap(viewport.Image);
            rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);
            viewport.Image = rotated;
            // CW 90: (x,y) in (W,H) -> (H-1-y, x) in (H,W)
            static Point RotPoint(Point p, int srcHeight) => new Point(srcHeight - 1 - p.Y, p.X);
            if (!Selection.IsEmpty)
            {
                var tl = RotPoint(new Point(Selection.Left, Selection.Bottom), height);
                var br = RotPoint(new Point(Selection.Right, Selection.Top), height);
                Selection = new Rectangle(Math.Min(tl.X, br.X), Math.Min(tl.Y, br.Y), Math.Abs(br.X - tl.X), Math.Abs(br.Y - tl.Y));
            }

            for (int i = 0; i < annotationShapes.Count; i++)
            {
                var shape = annotationShapes[i];
                shape.Start = RotPoint(shape.Start, height);
                shape.End = RotPoint(shape.End, height);
            }

            for (int i = 0; i < textAnnotations.Count; i++)
            {
                var annotation = textAnnotations[i];
                annotation.Position = RotPoint(annotation.Position, height);
            }

            SyncSelectedAnnotation();
            SyncSelectedTextAnnotation();
            var selectionAfter = Selection;
            var annotationStateAfter = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, beforeImage, new Bitmap(rotated), selectionBefore, selectionAfter, true, annotationStateBefore, annotationStateAfter);
            MarkDirtyAndNotify();
            RecenterViewportAfterImageChange(resizeWindow: true);
            viewport.Invalidate();
            return true;
        }

        private bool ExecuteRotateSelection90Cw()
        {
            if (viewport.Image == null)
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

            Bitmap? rotated = null;
            Bitmap? after = null;
            try
            {
                rotated = new Bitmap(before);
                rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);
                // Keep operation bounded to current selection by compositing into a same-size buffer.
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

                using (var g = Graphics.FromImage(viewport.Image))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(after, clampedSelection);
                }

                PushUndoStep(clampedSelection, before, after, selectionBefore, Selection);
                MarkDirtyAndNotify();
                viewport.Invalidate();
                return true;
            }
            catch
            {
                throw;
            }
            finally
            {
            }
        }

        private bool ExecuteFlip(RotateFlipType flipType)
        {
            if (!HasEditableImage || viewport.Image == null)
            {
                return false;
            }

            // When a selection is active, flip only the selected region in-place
            if (!Selection.IsEmpty)
            {
                return ExecuteFlipSelection(flipType);
            }

            var beforeImage = new Bitmap(viewport.Image);
            var selectionBefore = Selection;
            var annotationStateBefore = CloneAnnotations();
            int width = viewport.Image.Width;
            int height = viewport.Image.Height;
            var flipped = new Bitmap(viewport.Image);
            flipped.RotateFlip(flipType);
            viewport.Image = flipped;
            bool horizontal = flipType == RotateFlipType.RotateNoneFlipX;
            // Mirror annotation shapes
            for (int i = 0; i < annotationShapes.Count; i++)
            {
                var shape = annotationShapes[i];
                if (horizontal)
                {
                    shape.Start = new Point(width - shape.Start.X, shape.Start.Y);
                    shape.End = new Point(width - shape.End.X, shape.End.Y);
                }
                else
                {
                    shape.Start = new Point(shape.Start.X, height - shape.Start.Y);
                    shape.End = new Point(shape.End.X, height - shape.End.Y);
                }
            }

            // Mirror text annotations
            for (int i = 0; i < textAnnotations.Count; i++)
            {
                var annotation = textAnnotations[i];
                if (horizontal)
                {
                    annotation.Position = new Point(width - annotation.Position.X, annotation.Position.Y);
                }
                else
                {
                    annotation.Position = new Point(annotation.Position.X, height - annotation.Position.Y);
                }
            }

            SyncSelectedAnnotation();
            SyncSelectedTextAnnotation();
            var selectionAfter = Selection;
            var annotationStateAfter = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, beforeImage, new Bitmap(flipped), selectionBefore, selectionAfter, true, annotationStateBefore, annotationStateAfter);
            MarkDirtyAndNotify();
            viewport.Invalidate();
            return true;
        }

        private bool ExecuteFlipSelection(RotateFlipType flipType)
        {
            if (viewport.Image == null)
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
                after = new Bitmap(before);
                after.RotateFlip(flipType);
                using (var g = Graphics.FromImage(viewport.Image))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(after, clampedSelection);
                }

                PushUndoStep(clampedSelection, before, after, selectionBefore, Selection);
                MarkDirtyAndNotify();
                viewport.Invalidate();
                return true;
            }
            catch
            {
                throw;
            }
        }

        private bool CopyImageToClipboard()
        {
            if (!HasEditableImage || viewport.Image == null)
            {
                return false;
            }

            using var snapshot = BuildCompositeImage();
            return WriteImageToClipboard(snapshot, "Failed to copy the image to the clipboard.");
        }

        private bool CopySelectionToClipboard()
        {
            if (Selection.IsEmpty || !HasEditableImage || viewport.Image == null)
            {
                return false;
            }

            using var composite = BuildCompositeImage();
            using var selectionBitmap = new Bitmap(Selection.Width, Selection.Height);
            using (var graphics = Graphics.FromImage(selectionBitmap))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImage(composite, new Rectangle(Point.Empty, selectionBitmap.Size), Selection, GraphicsUnit.Pixel);
            }

            internalClipboardImage = new Bitmap(selectionBitmap);
            return true;
        }

        private bool WriteImageToClipboard(Image image, string failurePrefix)
        {
            if (ClipboardImageWriterForDiagnostics != null)
            {
                using var diagnosticsImage = new Bitmap(image);
                if (!ClipboardImageWriterForDiagnostics(diagnosticsImage))
                {
                    return false;
                }

                ClipboardMetadata.LastCaptureTimestamp = DateTime.Now;
                ClearClipboardNotification();
                return true;
            }

            try
            {
                ClipboardImageWriter.WriteImage(image);
                ClipboardMetadata.LastCaptureTimestamp = DateTime.Now;
                ClearClipboardNotification();
                return true;
            }
            catch (ExternalException ex)
            {
                NotifySurface($"{failurePrefix}\n{ex.Message}", "Clipboard Error");
                return false;
            }
        }

        private bool TryPasteImageFromClipboard()
        {
            Image? clipboardImage = null;
            if (internalClipboardImage != null)
            {
                clipboardImage = new Bitmap(internalClipboardImage);
            }

            try
            {
                if (clipboardImage == null)
                {
                    clipboardImage = ClipboardImageDecoder.TryRead(Clipboard.GetDataObject());
                }
            }
            catch (ExternalException ex)
            {
                NotifySurface($"Failed to access the clipboard.\n{ex.Message}", WindowTitleBase);
                return true;
            }

            if (clipboardImage == null)
            {
                return false;
            }

            using (clipboardImage)
            {
                if (!HasEditableImage || viewport.Image == null)
                {
                    LoadImage(clipboardImage);
                    return true;
                }

                return AddFloatingImageLayer(clipboardImage);
            }
        }

        private bool AddFloatingImageLayer(Image source, PointF? dropCenter = null)
        {
            if (!HasEditableImage || viewport.Image == null || source == null)
            {
                return false;
            }

            // Both Ctrl+V and history-thumbnail drops arrive here. The only placement difference
            // is that a drag supplies its drop point; keyboard paste keeps the established
            // selection-origin / canvas-center behavior.
            var canvasSize = viewport.Image.Size;
            RectangleF frame;
            // Placement is rounded to whole pixels: halving an odd span (an odd-sized paste, or
            // a canvas whose parity differs from the paste) otherwise parks the layer on a
            // half-pixel, and a half-pixel destination makes the bake resample every pixel in
            // the region. The NearestNeighbor viewport snaps that away on screen, so an
            // unrounded frame looks perfectly 1:1 right up until it is committed.
            if (dropCenter.HasValue)
            {
                frame = new RectangleF(MathF.Round(dropCenter.Value.X - source.Width / 2f), MathF.Round(dropCenter.Value.Y - source.Height / 2f), source.Width, source.Height);
            }
            else
            {
                frame = !Selection.IsEmpty ? new RectangleF(Selection.X, Selection.Y, source.Width, source.Height) : new RectangleF(MathF.Round((canvasSize.Width - source.Width) / 2f), MathF.Round((canvasSize.Height - source.Height) / 2f), source.Width, source.Height);
            }

            // Non-destructive paste: drop the image in as a smart layer rather than rasterizing.
            // The base bitmap is captured in the undo step (ReplacesImage=true) so undoing across
            // a future commit-flatten still walks back to the unflattened baseline.
            var beforeImage = new Bitmap(viewport.Image);
            var afterImage = new Bitmap(viewport.Image);
            var selectionBefore = Selection;
            var layersBefore = CloneLayers();
            var annotationStateBefore = CloneAnnotations();
            var textAnnotationStateBefore = CloneTextAnnotations();
            var newLayer = new ImageLayer(new Bitmap(source), frame)
            {
                Name = $"Paste {NextPasteLayerNumber()}",
            };
            imageLayers.Add(newLayer);
            SelectImageLayer(imageLayers.Count - 1);
            // A layer selection replaces the legacy pixel-region selection so no stale
            // marching-ants rectangle remains after the layer moves or resizes.
            ClearSelection();
            var layersAfter = CloneLayers();
            var annotationStateAfter = CloneAnnotations();
            var textAnnotationStateAfter = CloneTextAnnotations();
            PushUndoStep(Rectangle.Empty, beforeImage, afterImage, selectionBefore, Selection, replacesImage: true, shapesBefore: annotationStateBefore, shapesAfter: annotationStateAfter, textsBefore: textAnnotationStateBefore, textsAfter: textAnnotationStateAfter, layersBefore: layersBefore, layersAfter: layersAfter);
            isPlaceholderImage = false;
            viewport.Invalidate();
            return true;
        }

        private void ReloadFromClipboard(bool showEmptyClipboardMessage = true)
        {
            if (!ConfirmReloadWhenDirty())
            {
                return;
            }

            if (TryReloadImageFromClipboard())
            {
                return;
            }

            if (showEmptyClipboardMessage)
            {
                NotifySurface("Clipboard does not contain image data to reload.", WindowTitleBase);
            }
        }

        private bool ConfirmReloadWhenDirty() => !hasUnsavedChanges || ConfirmReloadWhenDirtyOverrideForDiagnostics?.Invoke() == true;
        private bool TryReloadImageFromClipboard()
        {
            Image? clipboardImage = null;
            if (ClipboardImageProviderForDiagnostics != null)
            {
                clipboardImage = ClipboardImageProviderForDiagnostics();
            }
            else
            {
                try
                {
                    clipboardImage = ClipboardImageDecoder.TryRead(Clipboard.GetDataObject());
                }
                catch (ExternalException ex)
                {
                    NotifySurface($"Failed to access the clipboard.\n{ex.Message}", WindowTitleBase);
                    return true;
                }
            }

            if (clipboardImage == null)
            {
                return false;
            }

            using (clipboardImage)
            {
                LoadImage(clipboardImage);
            }

            ClearClipboardNotification();
            NotifyDocumentContentChanged();
            return true;
        }

        private async Task TraceImageToSvgAsync(lib.ImageTracer.TracingPreset preset)
        {
            if (!HasEditableImage || viewport?.Image == null)
            {
                return;
            }

            if (!lib.ImageTracer.IsAvailable())
            {
                NotifySurface("VTracer executable not found.\n\n" + "Download vtracer.exe from:\nhttps://github.com/visioncortex/vtracer/releases\n\n" + "Place vtracer.exe in the application folder.", "VTracer Not Found");
                return;
            }

            isTracing = true;
            try
            {
                using var bitmap = BuildCompositeImage();
                var svg = await lib.ImageTracer.TraceToSvgAsync(bitmap, preset);
                if (!string.IsNullOrEmpty(svg))
                {
                    Clipboard.SetText(svg);
                }
            }
            catch (Exception ex)
            {
                NotifySurface($"Failed to trace image:\n{ex.Message}", "Trace Error");
            }
            finally
            {
                isTracing = false;
            }
        }

        string IClipboardDocumentPresenter.DisplayName => "Image";

        void IClipboardDocumentPresenter.AttachHostServices(EditorHostServices services)
        {
            hostServices = services;
            ContentEditedCallback = () => services.NotifyContentEdited?.Invoke();
        }

        bool IClipboardDocumentPresenter.CanHandleClipboard(IDataObject dataObject)
        {
            return ClipboardImageDecoder.HasAlphaCapableFormat(dataObject);
        }

        void IClipboardDocumentPresenter.LoadFromClipboard(IDataObject dataObject)
        {
            if (dataObject == null)
            {
                return;
            }

            var clipboardImage = ClipboardImageDecoder.TryRead(dataObject);
            if (clipboardImage != null)
            {
                using (clipboardImage)
                {
                    LoadImage(clipboardImage);
                }
            }
        }

        bool IClipboardDocumentPresenter.CanExecute(EditorCommandId commandId) => commandId switch
        {
            EditorCommandId.Undo => undoStack.CanUndo,
            EditorCommandId.Redo => undoStack.CanRedo,
            EditorCommandId.Reload => true,
            EditorCommandId.CropTool or EditorCommandId.ReplaceBackground => HasEditableImage && !Selection.IsEmpty,
            EditorCommandId.ApplyFloatingPaste => HasSelectedLayer,
            EditorCommandId.CopySvgPoster or EditorCommandId.CopySvgPhoto or EditorCommandId.CopySvgBlackAndWhite => HasEditableImage && !isTracing,
            _ => HasEditableImage
        };
        bool IClipboardDocumentPresenter.TryExecute(EditorCommandId commandId)
        {
            switch (commandId)
            {
                case EditorCommandId.Save:
                    return ExecuteSave();
                case EditorCommandId.SaveAs:
                    return false;
                case EditorCommandId.Copy:
                    return CopyImageToClipboard();
                case EditorCommandId.Reload:
                    ReloadFromClipboard();
                    return true;
                case EditorCommandId.ExpandCanvas:
                    return ExecuteExpandCanvas();
                case EditorCommandId.Undo:
                {
                    CompletePendingDocumentEdits();
                    var step = undoStack.Undo();
                    if (step == null)
                    {
                        return false;
                    }

                    ApplyUndoStep(step, false);
                    return true;
                }

                case EditorCommandId.Redo:
                {
                    var step = undoStack.Redo();
                    if (step == null)
                    {
                        return false;
                    }

                    ApplyUndoStep(step, true);
                    return true;
                }

                case EditorCommandId.EmojiTool:
                    if (!HasEditableImage)
                        return false;
                    return false;
                case EditorCommandId.FitImageToView:
                    if (!HasEditableImage)
                        return false;
                    FitImageToCanvas();
                    return true;
                case EditorCommandId.CopySvgPoster:
                case EditorCommandId.CopySvgPhoto:
                case EditorCommandId.CopySvgBlackAndWhite:
                    if (!HasEditableImage || isTracing)
                        return false;
                    _ = TraceImageToSvgAsync(commandId switch
                    {
                        EditorCommandId.CopySvgPhoto => lib.ImageTracer.TracingPreset.Photo,
                        EditorCommandId.CopySvgBlackAndWhite => lib.ImageTracer.TracingPreset.BlackAndWhite,
                        _ => lib.ImageTracer.TracingPreset.Poster
                    });
                    return true;
                case EditorCommandId.ApplyFloatingPaste:
                    return ApplyFloatingPaste();
                case EditorCommandId.ToggleTransparencyGrid:
                    if (!HasEditableImage)
                    {
                        return false;
                    }

                    ToggleAlphaView();
                    return true;
                case EditorCommandId.SelectMoveTool:
                    if (!HasEditableImage)
                        return false;
                    SetActiveTool(ActiveTool.None);
                    RequestCanvasFocus();
                    return true;
                case EditorCommandId.ArrowTool:
                    if (!HasEditableImage)
                        return false;
                    ToggleDrawingTool(DrawingTool.Arrow);
                    return true;
                case EditorCommandId.RectangleTool:
                    if (!HasEditableImage)
                        return false;
                    ToggleDrawingTool(DrawingTool.Rectangle);
                    return true;
                case EditorCommandId.HighlighterTool:
                    if (!HasEditableImage)
                        return false;
                    ToggleDrawingTool(DrawingTool.Highlighter);
                    return true;
                case EditorCommandId.TextTool:
                    if (!HasEditableImage)
                        return false;
                    ToggleTextTool();
                    return true;
                case EditorCommandId.CropTool:
                    return ExecuteCrop();
                case EditorCommandId.RotateRight:
                    return ExecuteRotate90Cw();
                case EditorCommandId.FlipHorizontal:
                    return ExecuteFlip(RotateFlipType.RotateNoneFlipX);
                case EditorCommandId.FlipVertical:
                    return ExecuteFlip(RotateFlipType.RotateNoneFlipY);
                case EditorCommandId.StraightenTool:
                    return ExecuteStraighten();
                case EditorCommandId.FreeRotateTool:
                    return ActivateFreeRotateTool();
                case EditorCommandId.DeJpeg:
                    return false;
                case EditorCommandId.ResizeImage:
                    if (!HasEditableImage)
                        return false;
                    return false;
                case EditorCommandId.CensorTool:
                    return ActivateCensorTool();
                case EditorCommandId.ReplaceBackground:
                    return ExecuteReplaceWithBackground();
                case EditorCommandId.ColorCorrect:
                    if (!HasEditableImage)
                        return false;
                    return false;
                case EditorCommandId.OptimizeText:
                    return ExecuteOptimizeForText();
                default:
                    return false;
            }
        }

        bool IClipboardDocumentPresenter.CanPresent(ClipboardHistoryItem item)
        {
            return item?.Kind == ClipboardItemKind.Image;
        }

        void IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item) => LoadHistoryItem(item);
        private void LoadHistoryItem(ClipboardHistoryItem item, bool preserveView = false)
        {
            if (item?.CurrentImage == null)
                return;
            LoadImage(item.CurrentImage, false, preserveView);
            // LoadImage clears the undo stack, annotations, and layers. Restore the stashed state.
            undoStack.RestoreState(item.UndoSnapshot);
            if (item.UndoSnapshot == null)
                undoStack.SetInitialRevision(item.DocumentRevision);
            ApplyOverlay(item.Overlay);
            committedDocumentRevision = item.IsDirty ? item.CommittedRevision : undoStack.CurrentRevision;
            // A persisted live edit may not yet have reached an undo step. After restart
            // it is the initial state, and its former clean state is no longer reachable.
            if (item.IsDirty && committedDocumentRevision == undoStack.CurrentRevision)
                committedDocumentRevision = Guid.NewGuid();
            DocumentIsDirty = item.IsDirty;
            hasUnsavedChanges = item.IsDirty;
            synchronizedHistoryItem = item;
            viewport?.Invalidate();
        }

        internal void RecordSuccessfulClipboardExport()
        {
            CompletePendingDocumentEdits();
            committedDocumentRevision = undoStack.CurrentRevision;
            DocumentIsDirty = false;
            hasUnsavedChanges = false;
        }

        private void CompletePendingDocumentEdits()
        {
            if (annotationSnapshotBeforeEdit != null)
                CommitAnnotationUndo();
            else
                CommitTextAnnotationUndo();
            CommitLayerInteractionUndo();
        }

        void IClipboardDocumentPresenter.CaptureLiveStateInto(ClipboardHistoryItem item) => CaptureLiveStateInto(item);
        /// <summary>
        /// Copy the live document into the item: base image, annotations, texts, layers, and the
        /// flattened preview the thumbnail is drawn from. Everything here is a copy, so the
        /// editor is exactly as it was afterwards and this can run as often as the host likes.
        /// </summary>
        private void CaptureLiveStateInto(ClipboardHistoryItem item)
        {
            if (item == null)
                return;
            using var perf = PerfTrace.Scope("ImageDocumentEditor.CaptureLiveStateInto", () => $"layers={imageLayers.Count} shapes={annotationShapes.Count} text={textAnnotations.Count}", slowMs: 80);
            // Checkpoints can advance without changing pixels (e.g. a successful export).
            // Browsing or stashing after a debounced capture must still transfer undo state,
            // but need not PNG-encode the base or rebuild the overlay and preview again.
            item.DocumentRevision = undoStack.CurrentRevision;
            item.CommittedRevision = committedDocumentRevision;
            item.SetDirtyFlagForRestore(DocumentIsDirty);
            if (ReferenceEquals(synchronizedHistoryItem, item))
                return;
            // Base image stays unflattened so annotations and layers remain editable after a
            // round-trip through the item.
            if (HasEditableImage && viewport?.Image is Bitmap baseImage)
            {
                item.UpdateCurrentImageWithoutDirty(baseImage);
            }

            item.Overlay = CloneOverlay();
            // Flattened preview for the thumbnail. Skip the full-res copy when there is nothing
            // to composite — the thumbnail falls back to CurrentImage, which is identical then.
            if (!HasEditableImage)
            {
                item.SetPreviewComposite(null);
                synchronizedHistoryItem = item;
                return;
            }

            bool needsComposite = annotationShapes.Count > 0 || textAnnotations.Count > 0 || imageLayers.Count > 0;
            if (needsComposite)
            {
                using var composite = BuildCompositeImage();
                item.SetPreviewComposite(composite);
            }
            else
            {
                item.SetPreviewComposite(null);
            }
            synchronizedHistoryItem = item;
        }

        void IClipboardDocumentPresenter.StashHistoryItemState(ClipboardHistoryItem item)
        {
            if (item == null)
                return;
            using var perf = PerfTrace.Scope("ImageDocumentEditor.StashHistoryItemState", () => $"dirty={item.IsDirty} hasImage={HasEditableImage}", slowMs: 80);
            CompletePendingDocumentEdits();
            CaptureLiveStateInto(item);
            // The only thing a stash does that a capture does not, and the reason the two are
            // separate: ExtractState hands the undo steps to the snapshot and clears the live
            // stack. That is right when the presenter is leaving this item and wrong everywhere
            // else, so it lives here alone rather than inside the capture.
            item.UndoSnapshot = undoStack.ExtractState();
        }

        object? IClipboardDocumentPresenter.GetCurrentContent()
        {
            if (viewport?.Image is Bitmap && HasEditableImage)
            {
                using var perf = PerfTrace.Scope("ImageDocumentEditor.GetCurrentContent", () => $"size={viewport.Image.Width}x{viewport.Image.Height}", slowMs: 80);
                return BuildCompositeImage();
            }

            return null;
        }

        void IClipboardDocumentPresenter.FitContentToView() => FitImageToCanvas();
        /// <summary>
        /// Zoom out until the whole image is inside the canvas, and no further: an image smaller
        /// than the canvas stays at 1:1 rather than being magnified to fill it. The zoom is the
        /// exact fit rather than the nearest preset from <see cref = "ZoomLevels"/>, so nothing is
        /// left hanging over an edge; zoom in/out still steps back onto the preset ladder.
        /// </summary>
        internal void FitImageToCanvas()
        {
            if (viewport == null || !HasEditableImage)
            {
                return;
            }

            var image = viewport.GetImagePixelSize();
            var view = viewport.ClientSize;
            if (image.Width <= 0 || image.Height <= 0 || view.Width <= 0 || view.Height <= 0)
            {
                return;
            }

            var scale = Math.Min((decimal)view.Width / image.Width, (decimal)view.Height / image.Height);
            ZoomLevel = scale >= 1m ? 1m : Math.Round(scale, 3, MidpointRounding.ToZero);
            viewport.CenterImage();
        }
    }
}
