using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using screenzap.Components;

namespace screenzap
{
    internal enum ImageLayerHandle
    {
        None,
        Body,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left,
        Rotate
    }

    public partial class ImageEditor
    {
        private readonly List<ImageLayer> imageLayers = new List<ImageLayer>();
        private int selectedLayerIndex = -1;
        private bool isLayerInteractionActive;
        private ImageLayerHandle activeLayerHandle = ImageLayerHandle.None;
        private Point layerInteractionOriginPixel;
        private RectangleF layerInteractionStartFrame;
        private RectangleF layerInteractionStartFill;
        private float layerInteractionStartRotation;
        private List<ImageLayer>? layerInteractionLayersBefore;
        private bool layerChangedDuringInteraction;
        private ToolStrip? layerOptionsToolStrip;
        private ToolStripTextBox? layerHeightTextBox;
        private ToolStripTextBox? layerWidthTextBox;
        private ToolStripTextBox? layerAngleTextBox;
        private CheckBox? layerAspectLockCheckBox;
        private bool isSyncingLayerToolbarControls;
        private bool isLayerCropModifierHeld_TestOverride;

        // Handle dimensions in screen pixels (constant regardless of zoom).
        private const float LayerHandleScreenSize = 8f;
        private const float LayerRotationHandleScreenOffset = 28f;

        internal int ImageLayerCountForTests => imageLayers.Count;

        internal RectangleF GetImageLayerFrameForTests(int index) => imageLayers[index].Frame;

        internal RectangleF GetImageLayerFillForTests(int index) => imageLayers[index].Fill;

        internal float GetImageLayerRotationForTests(int index) => imageLayers[index].RotationDeg;

        internal Bitmap BuildCompositeImageForTests() => BuildCompositeImage();

        internal Bitmap? CloneBaseBitmapForTests() => pictureBox1?.Image is Bitmap b ? new Bitmap(b) : null;

        internal void RenderScreenLayersForTests(Graphics graphics)
        {
            DrawImageLayers(graphics, AnnotationSurface.Screen);
            DrawSelectedLayerOverlay(graphics);
        }

        internal int SelectedLayerIndexForTests => selectedLayerIndex;

        internal void SetSelectedLayerForTests(int index) => SelectImageLayer(index);

        internal bool LayerToolbarAvailableForTests => layerOptionsToolStrip != null;

        internal bool LayerToolbarShownForTests => layerOptionsToolStrip?.Visible == true;

        internal bool LayerRotationInputAvailableForTests => layerAngleTextBox != null;

        internal void SetLayerAspectLockForTests(bool locked)
        {
            if (layerAspectLockCheckBox != null)
            {
                layerAspectLockCheckBox.Checked = locked;
            }
        }

        internal void SetLayerCropModifierForTests(bool held) =>
            isLayerCropModifierHeld_TestOverride = held;

        internal void SetSelectedLayerHeightForTests(float height) =>
            ApplySelectedLayerDimension(height, updateWidth: false);

        internal void SetSelectedLayerWidthForTests(float width) =>
            ApplySelectedLayerDimension(width, updateWidth: true);

        internal void SetSelectedLayerAngleForTests(float angle) =>
            ApplySelectedLayerAngle(angle);

        internal void ResetSelectedLayerForTests() => ResetSelectedLayerDimensions();

        internal bool DropHistoryImageForTests(IDataObject data, Point imagePixel)
        {
            return ClipboardHistoryImageDragPayload.TryGetImage(data, out var image)
                && image != null
                && AddFloatingImageLayer(image, imagePixel);
        }

        internal bool ApplyFloatingPasteForTests() => ApplyFloatingPaste();

        internal bool BeginLayerInteractionForTests(Point pixelPoint)
        {
            return TryBeginLayerInteraction(pixelPoint);
        }

        internal void UpdateLayerInteractionForTests(Point pixelPoint)
        {
            UpdateLayerInteraction(pixelPoint);
        }

        internal void EndLayerInteractionForTests()
        {
            EndLayerInteraction();
        }

        private List<ImageLayer> CloneLayers()
        {
            return imageLayers.Select(layer => layer.Clone()).ToList();
        }

        private void ApplyLayerState(List<ImageLayer>? source)
        {
            // Convention (mirrors ApplyAnnotationState): null means "not tracked, leave live state alone".
            // An empty list means "set live layers to empty".
            if (source == null)
            {
                return;
            }

            int selectionToRestore = selectedLayerIndex;
            ClearImageLayers();

            foreach (var layer in source)
            {
                imageLayers.Add(layer.Clone());
            }

            // Selection survives if the index is still valid; otherwise reset.
            selectedLayerIndex = selectionToRestore >= 0 && selectionToRestore < imageLayers.Count
                ? selectionToRestore
                : -1;
            UpdateLayerToolbarState();
        }

        private void ApplyCropToImageLayers(Point cropOrigin, Size newSize)
        {
            if (imageLayers.Count == 0)
            {
                return;
            }

            var selectedLayer = selectedLayerIndex >= 0 && selectedLayerIndex < imageLayers.Count
                ? imageLayers[selectedLayerIndex]
                : null;

            var newBounds = new RectangleF(0f, 0f, newSize.Width, newSize.Height);
            var updated = new List<ImageLayer>();

            foreach (var layer in imageLayers)
            {
                var frame = layer.Frame;
                frame.X -= cropOrigin.X;
                frame.Y -= cropOrigin.Y;

                if (!frame.IntersectsWith(newBounds))
                {
                    layer.Dispose();
                    continue;
                }

                layer.Frame = frame;
                updated.Add(layer);
            }

            imageLayers.Clear();
            imageLayers.AddRange(updated);

            selectedLayerIndex = selectedLayer != null ? imageLayers.IndexOf(selectedLayer) : -1;
            UpdateLayerToolbarState();
        }

        private void InitializeHistoryImageDrop()
        {
            if (pictureBox1 == null)
            {
                return;
            }

            pictureBox1.AllowDrop = true;
            pictureBox1.DragEnter += pictureBox1_HistoryImageDragEnter;
            pictureBox1.DragOver += pictureBox1_HistoryImageDragEnter;
            pictureBox1.DragDrop += pictureBox1_HistoryImageDragDrop;
        }

        private void pictureBox1_HistoryImageDragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = HasEditableImage
                && ClipboardHistoryImageDragPayload.TryGetImage(e.Data, out _)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
        }

        private void pictureBox1_HistoryImageDragDrop(object? sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
            if (!HasEditableImage
                || !ClipboardHistoryImageDragPayload.TryGetImage(e.Data, out var image)
                || image == null)
            {
                return;
            }

            var clientPoint = pictureBox1.PointToClient(new Point(e.X, e.Y));
            var imagePixel = FormCoordToPixel(clientPoint);
            if (AddFloatingImageLayer(image, imagePixel))
            {
                e.Effect = DragDropEffects.Copy;
                pictureBox1.Focus();
            }
        }

        private void ClearImageLayers()
        {
            foreach (var layer in imageLayers)
            {
                layer.Dispose();
            }
            imageLayers.Clear();
            selectedLayerIndex = -1;
            isLayerInteractionActive = false;
            activeLayerHandle = ImageLayerHandle.None;
            layerInteractionLayersBefore = null;
            UpdateLayerToolbarState();
        }

        private void SelectImageLayer(int index)
        {
            if (index < -1 || index >= imageLayers.Count)
            {
                index = -1;
            }
            if (selectedLayerIndex == index)
            {
                UpdateLayerToolbarState();
                return;
            }
            selectedLayerIndex = index;

            // Selecting a layer clears any annotation / text-annotation selection so
            // selection feels mutually exclusive between content types (Figma model).
            if (index >= 0)
            {
                if (selectedAnnotation != null)
                {
                    SelectAnnotation(null);
                }
                if (selectedTextAnnotation != null)
                {
                    SelectTextAnnotation(null);
                }
            }

            UpdateLayerToolbarState();
            pictureBox1?.Invalidate();
        }

        private int? HitTestLayerBody(Point pixelPoint)
        {
            // Top-most first.
            for (int i = imageLayers.Count - 1; i >= 0; i--)
            {
                if (PointIsInLayerBody(pixelPoint, imageLayers[i]))
                    return i;
            }
            return null;
        }

        private static bool PointIsInLayerBody(Point pixelPoint, ImageLayer layer)
        {
            if (layer.RotationDeg == 0f)
                return layer.Frame.Contains(pixelPoint.X, pixelPoint.Y);

            // Rotate the point into the layer's local (unrotated) space.
            var local = RotatePointAroundCenter(pixelPoint, layer.Frame, -layer.RotationDeg);
            return layer.Frame.Contains(local.X, local.Y);
        }

        private static PointF RotatePointAroundCenter(PointF pt, RectangleF frame, float deg)
        {
            float cx = frame.X + frame.Width / 2f;
            float cy = frame.Y + frame.Height / 2f;
            double rad = deg * System.Math.PI / 180.0;
            double cos = System.Math.Cos(rad);
            double sin = System.Math.Sin(rad);
            float dx = pt.X - cx;
            float dy = pt.Y - cy;
            return new PointF(
                (float)(dx * cos - dy * sin) + cx,
                (float)(dx * sin + dy * cos) + cy);
        }

        private ImageLayerHandle HitTestSelectedLayerHandle(Point pixelPoint)
        {
            if (selectedLayerIndex < 0 || selectedLayerIndex >= imageLayers.Count)
            {
                return ImageLayerHandle.None;
            }

            var layer = imageLayers[selectedLayerIndex];
            float zoom = pictureBox1 != null ? (float)pictureBox1.ZoomLevel : 1f;
            if (zoom <= 0f) zoom = 1f;
            float screenTol = LayerHandleScreenSize / 2f;
            float tol = screenTol / zoom;

            // Rotate the test point into the layer's local (unrotated) space for resize handles.
            var f = layer.Frame;
            var localPt = layer.RotationDeg == 0f
                ? (PointF)pixelPoint
                : RotatePointAroundCenter(pixelPoint, f, -layer.RotationDeg);

            // Corners first (smaller bullseye), then edges.
            if (IsNearF(localPt, new PointF(f.Left, f.Top), tol)) return ImageLayerHandle.TopLeft;
            if (IsNearF(localPt, new PointF(f.Right, f.Top), tol)) return ImageLayerHandle.TopRight;
            if (IsNearF(localPt, new PointF(f.Right, f.Bottom), tol)) return ImageLayerHandle.BottomRight;
            if (IsNearF(localPt, new PointF(f.Left, f.Bottom), tol)) return ImageLayerHandle.BottomLeft;

            if (System.Math.Abs(localPt.Y - f.Top) <= tol && localPt.X >= f.Left && localPt.X <= f.Right) return ImageLayerHandle.Top;
            if (System.Math.Abs(localPt.Y - f.Bottom) <= tol && localPt.X >= f.Left && localPt.X <= f.Right) return ImageLayerHandle.Bottom;
            if (System.Math.Abs(localPt.X - f.Left) <= tol && localPt.Y >= f.Top && localPt.Y <= f.Bottom) return ImageLayerHandle.Left;
            if (System.Math.Abs(localPt.X - f.Right) <= tol && localPt.Y >= f.Top && localPt.Y <= f.Bottom) return ImageLayerHandle.Right;

            // Rotation handle: located LayerRotationHandleScreenOffset pixels above the
            // top-center in the layer's local space (same point regardless of rotation because
            // the test point has already been unrotated above).
            float rotHandleImageY = f.Top - LayerRotationHandleScreenOffset / zoom;
            float rotHandleTol = screenTol * 1.5f / zoom;
            if (IsNearF(localPt, new PointF(f.Left + f.Width / 2f, rotHandleImageY), rotHandleTol))
                return ImageLayerHandle.Rotate;

            return ImageLayerHandle.None;
        }

        private float HandleHitToleranceImagePixels()
        {
            float zoom = pictureBox1 != null ? (float)pictureBox1.ZoomLevel : 1f;
            if (zoom <= 0f) zoom = 1f;
            return (LayerHandleScreenSize / 2f) / zoom;
        }

        private static bool IsNear(Point p, PointF target, float tol)
        {
            return System.Math.Abs(p.X - target.X) <= tol && System.Math.Abs(p.Y - target.Y) <= tol;
        }

        private static bool IsNearF(PointF p, PointF target, float tol)
        {
            return System.Math.Abs(p.X - target.X) <= tol && System.Math.Abs(p.Y - target.Y) <= tol;
        }

        private static bool IsCornerHandle(ImageLayerHandle h) =>
            h == ImageLayerHandle.TopLeft || h == ImageLayerHandle.TopRight ||
            h == ImageLayerHandle.BottomLeft || h == ImageLayerHandle.BottomRight;

        private bool TryBeginLayerInteraction(Point pixelPoint)
        {
            // Hit-test priority (Figma-ish):
            //  1. If the click is INSIDE the body of a layer that isn't the selected one,
            //     switch selection to it. This wins over a selected layer's handle hit because
            //     a body click on another layer is a clearer "I want that layer" gesture than
            //     a fuzzy edge-tolerance match.
            //  2. Otherwise, if the selected layer has a handle here, use it.
            //  3. Otherwise, body hit on the selected layer (if any) starts a body drag.
            var topMostBody = HitTestLayerBody(pixelPoint);

            if (topMostBody != null && topMostBody.Value != selectedLayerIndex)
            {
                SelectImageLayer(topMostBody.Value);
                activeLayerHandle = ImageLayerHandle.Body;
                BeginInteraction(pixelPoint);
                return true;
            }

            var handle = HitTestSelectedLayerHandle(pixelPoint);
            if (handle != ImageLayerHandle.None)
            {
                activeLayerHandle = handle;
                BeginInteraction(pixelPoint);
                return true;
            }

            if (topMostBody != null)
            {
                // Body hit on the already-selected layer.
                SelectImageLayer(topMostBody.Value);
                activeLayerHandle = ImageLayerHandle.Body;
                BeginInteraction(pixelPoint);
                return true;
            }

            return false;
        }

        private void BeginInteraction(Point pixelPoint)
        {
            isLayerInteractionActive = true;
            layerInteractionOriginPixel = pixelPoint;
            layerInteractionStartFrame = imageLayers[selectedLayerIndex].Frame;
            layerInteractionStartFill = imageLayers[selectedLayerIndex].Fill;
            layerInteractionStartRotation = imageLayers[selectedLayerIndex].RotationDeg;
            layerInteractionLayersBefore = CloneLayers();
            layerChangedDuringInteraction = false;
            pictureBox1?.Invalidate();
        }

        private void UpdateLayerInteraction(Point pixelPoint)
        {
            if (!isLayerInteractionActive || selectedLayerIndex < 0) return;

            // Rotation is handled separately — it only updates RotationDeg, not Frame.
            if (activeLayerHandle == ImageLayerHandle.Rotate)
            {
                float lx = layerInteractionStartFrame.X + layerInteractionStartFrame.Width / 2f;
                float ly = layerInteractionStartFrame.Y + layerInteractionStartFrame.Height / 2f;
                double startAngle = System.Math.Atan2(layerInteractionOriginPixel.Y - ly, layerInteractionOriginPixel.X - lx) * 180.0 / System.Math.PI;
                double currentAngle = System.Math.Atan2(pixelPoint.Y - ly, pixelPoint.X - lx) * 180.0 / System.Math.PI;
                float newRotation = layerInteractionStartRotation + (float)(currentAngle - startAngle);
                // Normalize to (-180, 180].
                while (newRotation > 180f) newRotation -= 360f;
                while (newRotation <= -180f) newRotation += 360f;
                // Shift: snap to 15° increments.
                if (System.Windows.Forms.Control.ModifierKeys.HasFlag(System.Windows.Forms.Keys.Shift))
                    newRotation = (float)(System.Math.Round(newRotation / 15.0) * 15.0);

                var rotLayer = imageLayers[selectedLayerIndex];
                if (rotLayer.RotationDeg != newRotation)
                {
                    rotLayer.RotationDeg = newRotation;
                    layerChangedDuringInteraction = true;
                    UpdateLayerToolbarState();
                    pictureBox1?.Invalidate();
                }
                return;
            }

            float dx = pixelPoint.X - layerInteractionOriginPixel.X;
            float dy = pixelPoint.Y - layerInteractionOriginPixel.Y;
            var start = layerInteractionStartFrame;
            var layer = imageLayers[selectedLayerIndex];
            if (activeLayerHandle == ImageLayerHandle.Body)
            {
                var moved = new RectangleF(start.X + dx, start.Y + dy, start.Width, start.Height);
                if (layer.Frame != moved)
                {
                    layer.Frame = moved;
                    layerChangedDuringInteraction = true;
                    UpdateLayerToolbarState();
                    pictureBox1?.Invalidate();
                }
                return;
            }

            bool isCropping = activeLayerHandle != ImageLayerHandle.Body
                && IsLayerCropModifierDown;
            if (isCropping)
            {
                ApplyLayerCropDrag(layer, pixelPoint);
                return;
            }

            // Shift inverts the aspect lock for the drag: free resize while the lock
            // is on (the default), proportional while it's off.
            bool preserveAspect = IsCornerHandle(activeLayerHandle)
                && (LayerAspectRatioLocked ^ IsLayerAspectInvertModifierDown);
            GetLayerResizeDimensions(pixelPoint, preserveAspect, out float targetWidth, out float targetHeight);
            var next = CreateAnchoredResizeFrame(targetWidth, targetHeight);

            if (layer.Frame != next)
            {
                layer.Frame = next;
                layerChangedDuringInteraction = true;
                UpdateLayerToolbarState();
                pictureBox1?.Invalidate();
            }
        }

        private bool IsLayerCropModifierDown =>
            isLayerCropModifierHeld_TestOverride
            || System.Windows.Forms.Control.ModifierKeys.HasFlag(System.Windows.Forms.Keys.Control);

        private bool IsLayerAspectInvertModifierDown =>
            isShiftHeld_TestOverride
            || System.Windows.Forms.Control.ModifierKeys.HasFlag(System.Windows.Forms.Keys.Shift);

        private bool LayerAspectRatioLocked => layerAspectLockCheckBox?.Checked == true;

        private void GetLayerResizeDimensions(
            Point pixelPoint,
            bool preserveAspect,
            out float targetWidth,
            out float targetHeight)
        {
            var frame = layerInteractionStartFrame;
            float worldDx = pixelPoint.X - layerInteractionOriginPixel.X;
            float worldDy = pixelPoint.Y - layerInteractionOriginPixel.Y;
            double radians = layerInteractionStartRotation * Math.PI / 180.0;
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);

            // Project the pointer delta onto the layer's own rotated axes. Applying canvas
            // X/Y directly to Frame sides made a rotated resize drift away from its fixed handle.
            float localDx = worldDx * cos + worldDy * sin;
            float localDy = -worldDx * sin + worldDy * cos;

            targetWidth = frame.Width;
            targetHeight = frame.Height;
            if (HandleMovesLeft(activeLayerHandle)) targetWidth -= localDx;
            if (HandleMovesRight(activeLayerHandle)) targetWidth += localDx;
            if (HandleMovesTop(activeLayerHandle)) targetHeight -= localDy;
            if (HandleMovesBottom(activeLayerHandle)) targetHeight += localDy;

            if (preserveAspect && frame.Width > 0f && frame.Height > 0f)
            {
                float scaleByWidth = targetWidth / frame.Width;
                float scaleByHeight = targetHeight / frame.Height;
                float scale = Math.Abs(scaleByWidth - 1f) >= Math.Abs(scaleByHeight - 1f)
                    ? scaleByWidth
                    : scaleByHeight;
                scale = Math.Max(0.001f, scale);
                targetWidth = frame.Width * scale;
                targetHeight = frame.Height * scale;
            }

            targetWidth = Math.Max(1f, targetWidth);
            targetHeight = Math.Max(1f, targetHeight);
        }

        private RectangleF CreateAnchoredResizeFrame(float width, float height)
        {
            var start = layerInteractionStartFrame;
            var startCenter = new PointF(
                start.X + start.Width / 2f,
                start.Y + start.Height / 2f);
            var originalFixedOffset = GetResizeFixedAnchorOffset(start.Width, start.Height, activeLayerHandle);
            var fixedAnchor = Add(startCenter, RotateVector(originalFixedOffset, layerInteractionStartRotation));

            var newFixedOffset = GetResizeFixedAnchorOffset(width, height, activeLayerHandle);
            var newCenter = Subtract(fixedAnchor, RotateVector(newFixedOffset, layerInteractionStartRotation));
            return new RectangleF(
                newCenter.X - width / 2f,
                newCenter.Y - height / 2f,
                width,
                height);
        }

        private static PointF GetResizeFixedAnchorOffset(
            float width,
            float height,
            ImageLayerHandle draggedHandle)
        {
            float x = HandleMovesLeft(draggedHandle)
                ? width / 2f
                : HandleMovesRight(draggedHandle) ? -width / 2f : 0f;
            float y = HandleMovesTop(draggedHandle)
                ? height / 2f
                : HandleMovesBottom(draggedHandle) ? -height / 2f : 0f;
            return new PointF(x, y);
        }

        private static PointF RotateVector(PointF vector, float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            return new PointF(
                vector.X * cos - vector.Y * sin,
                vector.X * sin + vector.Y * cos);
        }

        private static PointF Add(PointF a, PointF b) => new PointF(a.X + b.X, a.Y + b.Y);

        private static PointF Subtract(PointF a, PointF b) => new PointF(a.X - b.X, a.Y - b.Y);

        private static bool HandleMovesLeft(ImageLayerHandle handle) =>
            handle == ImageLayerHandle.TopLeft
            || handle == ImageLayerHandle.BottomLeft
            || handle == ImageLayerHandle.Left;

        private static bool HandleMovesRight(ImageLayerHandle handle) =>
            handle == ImageLayerHandle.TopRight
            || handle == ImageLayerHandle.BottomRight
            || handle == ImageLayerHandle.Right;

        private static bool HandleMovesTop(ImageLayerHandle handle) =>
            handle == ImageLayerHandle.TopLeft
            || handle == ImageLayerHandle.TopRight
            || handle == ImageLayerHandle.Top;

        private static bool HandleMovesBottom(ImageLayerHandle handle) =>
            handle == ImageLayerHandle.BottomLeft
            || handle == ImageLayerHandle.BottomRight
            || handle == ImageLayerHandle.Bottom;

        private void ApplyLayerCropDrag(ImageLayer layer, Point pixelPoint)
        {
            var frame = layerInteractionStartFrame;
            var fill = layerInteractionStartFill;
            if (frame.Width <= 0f || frame.Height <= 0f || fill.Width <= 0f || fill.Height <= 0f)
            {
                return;
            }

            float sourcePerFrameX = fill.Width / frame.Width;
            float sourcePerFrameY = fill.Height / frame.Height;
            float left = fill.Left;
            float top = fill.Top;
            float right = fill.Right;
            float bottom = fill.Bottom;
            bool movesLeft = HandleMovesLeft(activeLayerHandle);
            bool movesRight = HandleMovesRight(activeLayerHandle);
            bool movesTop = HandleMovesTop(activeLayerHandle);
            bool movesBottom = HandleMovesBottom(activeLayerHandle);
            GetLayerResizeDimensions(pixelPoint, preserveAspect: false, out float desiredWidth, out float desiredHeight);

            // Keep the destination/source scale fixed. Moving an edge changes the source fill
            // by the same scaled amount; clamping to Source lets an outward drag reveal pixels
            // that were cropped earlier without ever sampling outside the pasted bitmap.
            if (movesLeft)
            {
                float proposed = fill.Right - desiredWidth * sourcePerFrameX;
                left = Math.Clamp(proposed, 0f, fill.Right - sourcePerFrameX);
            }
            if (movesRight)
            {
                float proposed = fill.Left + desiredWidth * sourcePerFrameX;
                right = Math.Clamp(proposed, fill.Left + sourcePerFrameX, layer.Source.Width);
            }
            if (movesTop)
            {
                float proposed = fill.Bottom - desiredHeight * sourcePerFrameY;
                top = Math.Clamp(proposed, 0f, fill.Bottom - sourcePerFrameY);
            }
            if (movesBottom)
            {
                float proposed = fill.Top + desiredHeight * sourcePerFrameY;
                bottom = Math.Clamp(proposed, fill.Top + sourcePerFrameY, layer.Source.Height);
            }

            var nextFill = RectangleF.FromLTRB(left, top, right, bottom);
            float actualWidth = nextFill.Width / sourcePerFrameX;
            float actualHeight = nextFill.Height / sourcePerFrameY;
            var nextFrame = CreateAnchoredResizeFrame(actualWidth, actualHeight);
            if (layer.Frame == nextFrame && layer.Fill == nextFill)
            {
                return;
            }

            layer.Frame = nextFrame;
            layer.Fill = nextFill;
            layerChangedDuringInteraction = true;
            UpdateLayerToolbarState();
            pictureBox1?.Invalidate();
        }

        private void EndLayerInteraction()
        {
            if (!isLayerInteractionActive)
            {
                return;
            }

            isLayerInteractionActive = false;
            var handleAtBegin = activeLayerHandle;
            activeLayerHandle = ImageLayerHandle.None;

            if (!layerChangedDuringInteraction || layerInteractionLayersBefore == null)
            {
                // Interaction was a click without drag — discard the captured before-state.
                if (layerInteractionLayersBefore != null)
                {
                    foreach (var l in layerInteractionLayersBefore) l.Dispose();
                    layerInteractionLayersBefore = null;
                }
                return;
            }

            var layersAfter = CloneLayers();

            // Capture the base bitmap on every layer-only step too (ReplacesImage=true). This
            // costs a bitmap clone per drag/resize but lets undo across a commit-flatten restore
            // the unflattened baseline. Without it, undoing a layer-drag after commit shows the
            // baked-in flattened layer plus the restored live layer (double render).
            Bitmap? baseClone = pictureBox1?.Image is Bitmap b ? new Bitmap(b) : null;
            Bitmap? baseClone2 = pictureBox1?.Image is Bitmap b2 ? new Bitmap(b2) : null;

            PushUndoStep(
                Rectangle.Empty,
                baseClone,
                baseClone2,
                Selection,
                Selection,
                replacesImage: baseClone != null,
                shapesBefore: null,
                shapesAfter: null,
                textsBefore: null,
                textsAfter: null,
                layersBefore: layerInteractionLayersBefore,
                layersAfter: layersAfter);

            layerInteractionLayersBefore = null;
            layerChangedDuringInteraction = false;
            UpdateLayerToolbarState();
            _ = handleAtBegin; // currently unused; kept for future per-handle metadata.
        }

        private void DrawSelectedLayerOverlay(Graphics graphics)
        {
            if (selectedLayerIndex < 0 || selectedLayerIndex >= imageLayers.Count)
            {
                return;
            }

            float zoom = pictureBox1 != null ? (float)pictureBox1.ZoomLevel : 1f;
            PointF pan = pictureBox1 != null ? pictureBox1.Metrics.PanOffset : PointF.Empty;

            var f = imageLayers[selectedLayerIndex].Frame;
            float rotDeg = imageLayers[selectedLayerIndex].RotationDeg;

            // Screen-space center of the layer
            float cx = pan.X + (f.X + f.Width / 2f) * zoom;
            float cy = pan.Y + (f.Y + f.Height / 2f) * zoom;
            float hw = f.Width * zoom / 2f;
            float hh = f.Height * zoom / 2f;

            var state = graphics.Save();
            graphics.TranslateTransform(cx, cy);
            if (rotDeg != 0f) graphics.RotateTransform(rotDeg);

            using (var pen = new Pen(Color.DodgerBlue, 1.25f))
            {
                graphics.DrawRectangle(pen, -hw, -hh, hw * 2f, hh * 2f);
            }

            // 8 resize handles (coords relative to layer center after rotation applied)
            DrawLayerHandle(graphics, -hw, -hh);
            DrawLayerHandle(graphics, 0f, -hh);
            DrawLayerHandle(graphics, hw, -hh);
            DrawLayerHandle(graphics, hw, 0f);
            DrawLayerHandle(graphics, hw, hh);
            DrawLayerHandle(graphics, 0f, hh);
            DrawLayerHandle(graphics, -hw, hh);
            DrawLayerHandle(graphics, -hw, 0f);

            // Rotation handle: stem from top-center to a circle above it
            float stemEndY = -hh - LayerRotationHandleScreenOffset;
            using (var stemPen = new Pen(Color.DodgerBlue, 1f))
                graphics.DrawLine(stemPen, 0f, -hh, 0f, stemEndY);
            DrawRotationHandle(graphics, 0f, stemEndY);

            graphics.Restore(state);
        }

        private static void DrawRotationHandle(Graphics graphics, float cx, float cy)
        {
            const float r = 5f;
            using var fill = new SolidBrush(Color.White);
            using var stroke = new Pen(Color.DodgerBlue, 1f);
            graphics.FillEllipse(fill, cx - r, cy - r, r * 2f, r * 2f);
            graphics.DrawEllipse(stroke, cx - r, cy - r, r * 2f, r * 2f);
        }

        private static void DrawLayerHandle(Graphics graphics, float cx, float cy)
        {
            float half = LayerHandleScreenSize / 2f;
            var rect = new RectangleF(cx - half, cy - half, LayerHandleScreenSize, LayerHandleScreenSize);
            using (var fill = new SolidBrush(Color.White))
            using (var stroke = new Pen(Color.DodgerBlue, 1f))
            {
                graphics.FillRectangle(fill, rect);
                graphics.DrawRectangle(stroke, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        private void DrawImageLayers(Graphics graphics, AnnotationSurface surface)
        {
            if (imageLayers.Count == 0)
            {
                return;
            }

            float zoom = 1f;
            PointF pan = PointF.Empty;
            if (surface == AnnotationSurface.Screen && pictureBox1 != null)
            {
                zoom = (float)pictureBox1.ZoomLevel;
                pan = pictureBox1.Metrics.PanOffset;
            }

            foreach (var layer in imageLayers)
            {
                var dest = new RectangleF(
                    pan.X + layer.Frame.X * zoom,
                    pan.Y + layer.Frame.Y * zoom,
                    layer.Frame.Width * zoom,
                    layer.Frame.Height * zoom);

                if (layer.RotationDeg != 0f)
                {
                    float cx = dest.X + dest.Width / 2f;
                    float cy = dest.Y + dest.Height / 2f;
                    var state = graphics.Save();
                    graphics.TranslateTransform(cx, cy);
                    graphics.RotateTransform(layer.RotationDeg);
                    graphics.DrawImage(layer.Source,
                        new RectangleF(-dest.Width / 2f, -dest.Height / 2f, dest.Width, dest.Height),
                        layer.Fill, GraphicsUnit.Pixel);
                    graphics.Restore(state);
                }
                else
                {
                    graphics.DrawImage(layer.Source, dest, layer.Fill, GraphicsUnit.Pixel);
                }
            }
        }

        private bool ApplyFloatingPaste()
        {
            if (imageLayers.Count == 0) return false;
            if (pictureBox1?.Image == null) return false;

            var beforeImage = new Bitmap(pictureBox1.Image);
            var layersBefore = CloneLayers();
            var selectionBefore = Selection;

            using var composite = BuildCompositeImage();
            var afterImage = new Bitmap(composite);

            var currentZoom = ZoomLevel;
            pictureBox1.Image?.Dispose();
            pictureBox1.Image = afterImage;
            ZoomLevel = currentZoom;
            pictureBox1.ClampPan();

            ClearImageLayers();
            var layersAfter = CloneLayers(); // empty list

            PushUndoStep(
                Rectangle.Empty,
                beforeImage,
                new Bitmap(afterImage),
                selectionBefore,
                Selection,
                replacesImage: true,
                shapesBefore: null,
                shapesAfter: null,
                textsBefore: null,
                textsAfter: null,
                layersBefore: layersBefore,
                layersAfter: layersAfter);

            UpdateCommandUI();
            UpdateLayerToolbarState();
            pictureBox1.Invalidate();
            return true;
        }

        private bool HasSelectedLayer => selectedLayerIndex >= 0 && selectedLayerIndex < imageLayers.Count;

        private bool TryDeleteSelectedLayer()
        {
            if (!HasSelectedLayer) return false;

            var layersBefore = CloneLayers();
            var doomed = imageLayers[selectedLayerIndex];
            imageLayers.RemoveAt(selectedLayerIndex);
            doomed.Dispose();
            selectedLayerIndex = -1;
            UpdateLayerToolbarState();
            var layersAfter = CloneLayers();

            Bitmap? baseClone = pictureBox1?.Image is Bitmap b ? new Bitmap(b) : null;
            Bitmap? baseClone2 = pictureBox1?.Image is Bitmap b2 ? new Bitmap(b2) : null;

            PushUndoStep(
                Rectangle.Empty,
                baseClone,
                baseClone2,
                Selection,
                Selection,
                replacesImage: baseClone != null,
                shapesBefore: null,
                shapesAfter: null,
                textsBefore: null,
                textsAfter: null,
                layersBefore: layersBefore,
                layersAfter: layersAfter);

            pictureBox1?.Invalidate();
            return true;
        }

        private bool DeselectImageLayerIfAny()
        {
            if (!HasSelectedLayer) return false;
            selectedLayerIndex = -1;
            UpdateLayerToolbarState();
            pictureBox1?.Invalidate();
            return true;
        }

        private void InitializeLayerToolbar()
        {
            if (mainToolStrip == null || layerOptionsToolStrip != null)
            {
                return;
            }

            layerOptionsToolStrip = new ToolStrip
            {
                Name = "layerOptionsToolStrip",
                Dock = DockStyle.None,
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = true,
                ImageScalingSize = mainToolStrip.ImageScalingSize,
                Padding = new Padding(4, 2, 0, 2),
                Visible = false,
                CanOverflow = false
            };

            layerHeightTextBox = CreateLayerDimensionTextBox(
                "layerHeightTextBox",
                "Layer height in pixels",
                () => CommitLayerDimensionText(layerHeightTextBox, updateWidth: false));
            layerWidthTextBox = CreateLayerDimensionTextBox(
                "layerWidthTextBox",
                "Layer width in pixels",
                () => CommitLayerDimensionText(layerWidthTextBox, updateWidth: true));
            layerAngleTextBox = CreateLayerDimensionTextBox(
                "layerAngleTextBox",
                "Layer rotation angle in degrees",
                CommitLayerAngleText);

            layerAspectLockCheckBox = new CheckBox
            {
                Name = "layerAspectLockCheckBox",
                Text = "Lock aspect ratio",
                AutoSize = true,
                Checked = true,
                Margin = Padding.Empty
            };
            var aspectHost = new ToolStripControlHost(layerAspectLockCheckBox)
            {
                Name = "layerAspectLockHost",
                AutoSize = true,
                ToolTipText = "Keep the current proportions when resizing (hold Shift while dragging to invert)"
            };

            var resetButton = new ToolStripButton
            {
                Name = "layerResetButton",
                Text = "Reset",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "Restore original dimensions, crop, and rotation"
            };
            resetButton.Click += (_, _) => ResetSelectedLayerDimensions();

            layerOptionsToolStrip.Items.Add(new ToolStripLabel("H"));
            layerOptionsToolStrip.Items.Add(layerHeightTextBox);
            layerOptionsToolStrip.Items.Add(new ToolStripLabel("W"));
            layerOptionsToolStrip.Items.Add(layerWidthTextBox);
            layerOptionsToolStrip.Items.Add(new ToolStripLabel("Rotation"));
            layerOptionsToolStrip.Items.Add(layerAngleTextBox);
            layerOptionsToolStrip.Items.Add(new ToolStripSeparator());
            layerOptionsToolStrip.Items.Add(aspectHost);
            layerOptionsToolStrip.Items.Add(resetButton);

            Controls.Add(layerOptionsToolStrip);
            UpdateLayerToolbarState();
        }

        private ToolStripTextBox CreateLayerDimensionTextBox(string name, string toolTip, Action commit)
        {
            var textBox = new ToolStripTextBox
            {
                Name = name,
                AutoSize = false,
                Width = 58,
                ToolTipText = toolTip
            };

            textBox.Leave += (_, _) => commit();
            textBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    commit();
                    pictureBox1?.Focus();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    UpdateLayerToolbarState();
                    pictureBox1?.Focus();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            };
            return textBox;
        }

        private bool HandleLayerToolbarKeyDown(KeyEventArgs e)
        {
            if (layerOptionsToolStrip?.ContainsFocus != true)
            {
                return false;
            }

            ToolStripTextBox? focusedTextBox = null;
            if (layerHeightTextBox?.Control.Focused == true)
                focusedTextBox = layerHeightTextBox;
            else if (layerWidthTextBox?.Control.Focused == true)
                focusedTextBox = layerWidthTextBox;
            else if (layerAngleTextBox?.Control.Focused == true)
                focusedTextBox = layerAngleTextBox;

            if (focusedTextBox != null && e.KeyCode == Keys.Enter)
            {
                if (focusedTextBox == layerHeightTextBox)
                    CommitLayerDimensionText(focusedTextBox, updateWidth: false);
                else if (focusedTextBox == layerWidthTextBox)
                    CommitLayerDimensionText(focusedTextBox, updateWidth: true);
                else
                    CommitLayerAngleText();

                pictureBox1?.Focus();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (focusedTextBox != null && e.KeyCode == Keys.Escape)
            {
                UpdateLayerToolbarState();
                pictureBox1?.Focus();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }

            // With KeyPreview enabled, the form sees toolbar keystrokes before the hosted
            // control. Returning true keeps editor shortcuts (Enter-to-apply, Delete, etc.)
            // from acting on the document while the user is editing a toolbar value.
            return true;
        }

        private void UpdateLayerToolbarState()
        {
            if (layerOptionsToolStrip == null)
            {
                return;
            }

            bool show = HasSelectedLayer;
            layerOptionsToolStrip.Visible = show;
            if (!show)
            {
                return;
            }

            var layer = imageLayers[selectedLayerIndex];
            isSyncingLayerToolbarControls = true;
            try
            {
                if (layerHeightTextBox != null)
                    layerHeightTextBox.Text = FormatLayerToolbarValue(layer.Frame.Height);
                if (layerWidthTextBox != null)
                    layerWidthTextBox.Text = FormatLayerToolbarValue(layer.Frame.Width);
                if (layerAngleTextBox != null)
                    layerAngleTextBox.Text = FormatLayerToolbarValue(layer.RotationDeg);
            }
            finally
            {
                isSyncingLayerToolbarControls = false;
            }

            PositionOverlayToolStrips();
            layerOptionsToolStrip.BringToFront();
        }

        private static string FormatLayerToolbarValue(float value) =>
            value.ToString("0.##", CultureInfo.CurrentCulture);

        private static bool TryParseLayerToolbarValue(string? text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void CommitLayerDimensionText(ToolStripTextBox? textBox, bool updateWidth)
        {
            if (isSyncingLayerToolbarControls || textBox == null)
            {
                return;
            }

            if (TryParseLayerToolbarValue(textBox.Text, out float value) && value >= 1f)
            {
                ApplySelectedLayerDimension(value, updateWidth);
            }
            else
            {
                UpdateLayerToolbarState();
            }
        }

        private void CommitLayerAngleText()
        {
            if (isSyncingLayerToolbarControls || layerAngleTextBox == null)
            {
                return;
            }

            if (TryParseLayerToolbarValue(layerAngleTextBox.Text, out float value))
            {
                ApplySelectedLayerAngle(value);
            }
            else
            {
                UpdateLayerToolbarState();
            }
        }

        private void ApplySelectedLayerDimension(float value, bool updateWidth)
        {
            if (!HasSelectedLayer || !float.IsFinite(value) || value < 1f)
            {
                UpdateLayerToolbarState();
                return;
            }

            var layer = imageLayers[selectedLayerIndex];
            var before = CloneLayers();
            var frame = layer.Frame;

            if (updateWidth)
            {
                float height = LayerAspectRatioLocked && frame.Width > 0f
                    ? Math.Max(1f, value * frame.Height / frame.Width)
                    : frame.Height;
                layer.Frame = ResizeFrameKeepingVisualTopLeft(frame, layer.RotationDeg, value, height);
            }
            else
            {
                float width = LayerAspectRatioLocked && frame.Height > 0f
                    ? Math.Max(1f, value * frame.Width / frame.Height)
                    : frame.Width;
                layer.Frame = ResizeFrameKeepingVisualTopLeft(frame, layer.RotationDeg, width, value);
            }

            FinishLayerToolbarMutation(before, frame, layer.Fill, layer.RotationDeg);
        }

        private static RectangleF ResizeFrameKeepingVisualTopLeft(
            RectangleF frame,
            float rotationDegrees,
            float width,
            float height)
        {
            var oldCenter = new PointF(
                frame.X + frame.Width / 2f,
                frame.Y + frame.Height / 2f);
            var fixedTopLeft = Add(
                oldCenter,
                RotateVector(new PointF(-frame.Width / 2f, -frame.Height / 2f), rotationDegrees));
            var newCenter = Subtract(
                fixedTopLeft,
                RotateVector(new PointF(-width / 2f, -height / 2f), rotationDegrees));
            return new RectangleF(
                newCenter.X - width / 2f,
                newCenter.Y - height / 2f,
                width,
                height);
        }

        private void ApplySelectedLayerAngle(float angle)
        {
            if (!HasSelectedLayer || !float.IsFinite(angle))
            {
                UpdateLayerToolbarState();
                return;
            }

            var layer = imageLayers[selectedLayerIndex];
            var before = CloneLayers();
            var frameBefore = layer.Frame;
            var fillBefore = layer.Fill;
            float rotationBefore = layer.RotationDeg;
            layer.RotationDeg = NormalizeLayerAngle(angle);
            FinishLayerToolbarMutation(before, frameBefore, fillBefore, rotationBefore);
        }

        private static float NormalizeLayerAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle <= -180f) angle += 360f;
            return angle;
        }

        private void ResetSelectedLayerDimensions()
        {
            if (!HasSelectedLayer)
            {
                return;
            }

            var layer = imageLayers[selectedLayerIndex];
            var before = CloneLayers();
            var frameBefore = layer.Frame;
            var fillBefore = layer.Fill;
            float rotationBefore = layer.RotationDeg;
            layer.Frame = new RectangleF(
                layer.Frame.X,
                layer.Frame.Y,
                layer.Source.Width,
                layer.Source.Height);
            layer.Fill = new RectangleF(0f, 0f, layer.Source.Width, layer.Source.Height);
            layer.RotationDeg = 0f;
            FinishLayerToolbarMutation(before, frameBefore, fillBefore, rotationBefore);
        }

        private void FinishLayerToolbarMutation(
            List<ImageLayer> layersBefore,
            RectangleF frameBefore,
            RectangleF fillBefore,
            float rotationBefore)
        {
            var layer = imageLayers[selectedLayerIndex];
            if (layer.Frame == frameBefore
                && layer.Fill == fillBefore
                && layer.RotationDeg == rotationBefore)
            {
                DisposeOrphanedLayers(layersBefore);
                UpdateLayerToolbarState();
                return;
            }

            PushLayerOnlyUndoStep(layersBefore);
            UpdateLayerToolbarState();
            UpdateCommandUI();
            pictureBox1?.Invalidate();
        }

        private void PushLayerOnlyUndoStep(List<ImageLayer> layersBefore)
        {
            var layersAfter = CloneLayers();
            Bitmap? baseClone = pictureBox1?.Image is Bitmap before ? new Bitmap(before) : null;
            Bitmap? baseClone2 = pictureBox1?.Image is Bitmap after ? new Bitmap(after) : null;
            PushUndoStep(
                Rectangle.Empty,
                baseClone,
                baseClone2,
                Selection,
                Selection,
                replacesImage: baseClone != null,
                shapesBefore: null,
                shapesAfter: null,
                textsBefore: null,
                textsAfter: null,
                layersBefore: layersBefore,
                layersAfter: layersAfter);
        }

        private static Cursor CursorForLayerHandle(ImageLayerHandle handle)
        {
            return handle switch
            {
                ImageLayerHandle.Body => Cursors.SizeAll,
                ImageLayerHandle.TopLeft => Cursors.SizeNWSE,
                ImageLayerHandle.BottomRight => Cursors.SizeNWSE,
                ImageLayerHandle.TopRight => Cursors.SizeNESW,
                ImageLayerHandle.BottomLeft => Cursors.SizeNESW,
                ImageLayerHandle.Top => Cursors.SizeNS,
                ImageLayerHandle.Bottom => Cursors.SizeNS,
                ImageLayerHandle.Left => Cursors.SizeWE,
                ImageLayerHandle.Right => Cursors.SizeWE,
                ImageLayerHandle.Rotate => Cursors.Cross,
                _ => Cursors.Default,
            };
        }
    }
}
