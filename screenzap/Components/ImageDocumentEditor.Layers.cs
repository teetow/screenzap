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

    public partial class ImageDocumentEditor
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
        private bool? isLayerCropModifierHeld_TestOverride;
        // Handle dimensions in screen pixels (constant regardless of zoom).
        private const float LayerHandleScreenSize = 8f;
        private const float LayerRotationHandleScreenOffset = 28f;
        internal int ImageLayerCountForTests => imageLayers.Count;

        internal RectangleF GetImageLayerFrameForTests(int index) => imageLayers[index].Frame;
        internal RectangleF GetImageLayerFillForTests(int index) => imageLayers[index].Fill;
        internal float GetImageLayerRotationForTests(int index) => imageLayers[index].RotationDeg;
        internal Bitmap BuildCompositeImageForTests() => BuildCompositeImage();
        internal Bitmap? CloneBaseBitmapForTests() => viewport?.Image is Bitmap b ? new Bitmap(b) : null;
        internal void RenderScreenLayersForTests(Graphics graphics)
        {
            DrawImageLayers(graphics, AnnotationSurface.Screen);
            DrawSelectedLayerOverlay(graphics);
        }

        internal int SelectedLayerIndexForTests => selectedLayerIndex;

        internal void SetSelectedLayerForTests(int index) => SelectImageLayer(index);
        internal void SetLayerAspectLockForTests(bool locked) => layerAspectRatioLocked = locked;
        internal void SetLayerCropModifierForTests(bool held) => isLayerCropModifierHeld_TestOverride = held;
        internal void SetSelectedLayerHeightForTests(float height) => ApplySelectedLayerDimension(height, updateWidth: false);
        internal void SetSelectedLayerWidthForTests(float width) => ApplySelectedLayerDimension(width, updateWidth: true);
        internal void SetSelectedLayerAngleForTests(float angle) => ApplySelectedLayerAngle(angle);
        internal void SetSelectedLayerXForTests(float x) => ApplySelectedLayerPosition(x, updateX: true);
        internal void SetSelectedLayerYForTests(float y) => ApplySelectedLayerPosition(y, updateX: false);
        internal void ResetSelectedLayerForTests() => ResetSelectedLayerDimensions();
        internal bool DropHistoryImageForTests(IDataObject data, Point imagePixel)
        {
            return ClipboardHistoryImageDragPayload.TryGetImage(data, out var image) && image != null && AddFloatingImageLayer(image, imagePixel);
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
            selectedLayerIndex = selectionToRestore >= 0 && selectionToRestore < imageLayers.Count ? selectionToRestore : -1;
        }

        private void ApplyCropToImageLayers(Point cropOrigin, Size newSize)
        {
            if (imageLayers.Count == 0)
            {
                return;
            }

            var selectedLayer = selectedLayerIndex >= 0 && selectedLayerIndex < imageLayers.Count ? imageLayers[selectedLayerIndex] : null;
            var newBounds = new RectangleF(0f, 0f, newSize.Width, newSize.Height);
            var updated = new List<ImageLayer>();
            foreach (var layer in imageLayers)
            {
                var frame = layer.Frame;
                frame.X -= cropOrigin.X;
                frame.Y -= cropOrigin.Y;
                if (!frame.IntersectsWith(newBounds))
                {
                    continue;
                }

                layer.Frame = frame;
                updated.Add(layer);
            }

            imageLayers.Clear();
            imageLayers.AddRange(updated);
            selectedLayerIndex = selectedLayer != null ? imageLayers.IndexOf(selectedLayer) : -1;
        }

        private void ClearImageLayers()
        {
            foreach (var layer in imageLayers)
            {
            }

            imageLayers.Clear();
            selectedLayerIndex = -1;
            ResetLayerInteractionState();
        }

        /// <summary>
        /// Drop any in-flight gesture state. The captured before-snapshot is disposed rather
        /// than dropped: it holds cloned layer bitmaps, and the step it belonged to is gone.
        /// </summary>
        private void ResetLayerInteractionState()
        {
            isLayerInteractionActive = false;
            activeLayerHandle = ImageLayerHandle.None;
            layerChangedDuringInteraction = false;
            if (layerInteractionLayersBefore != null)
            {
                foreach (var layer in layerInteractionLayersBefore)
                {
                }

                layerInteractionLayersBefore = null;
            }
        }

        private void SelectImageLayer(int index)
        {
            if (index < -1 || index >= imageLayers.Count)
            {
                index = -1;
            }

            if (selectedLayerIndex == index)
            {
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

            viewport?.Invalidate();
        }

        private int? HitTestLayerBody(Point pixelPoint)
        {
            // Top-most first. A muted layer is not on screen, so it is not clickable either.
            for (int i = imageLayers.Count - 1; i >= 0; i--)
            {
                if (imageLayers[i].IsVisible && PointIsInLayerBody(pixelPoint, imageLayers[i]))
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
            return new PointF((float)(dx * cos - dy * sin) + cx, (float)(dx * sin + dy * cos) + cy);
        }

        private ImageLayerHandle HitTestSelectedLayerHandle(Point pixelPoint)
        {
            if (selectedLayerIndex < 0 || selectedLayerIndex >= imageLayers.Count)
            {
                return ImageLayerHandle.None;
            }

            var layer = imageLayers[selectedLayerIndex];
            float zoom = viewport != null ? (float)viewport.ZoomLevel : 1f;
            if (zoom <= 0f)
                zoom = 1f;
            float screenTol = LayerHandleScreenSize / 2f;
            float tol = screenTol / zoom;
            // Rotate the test point into the layer's local (unrotated) space for resize handles.
            var f = layer.Frame;
            var localPt = layer.RotationDeg == 0f ? (PointF)pixelPoint : RotatePointAroundCenter(pixelPoint, f, -layer.RotationDeg);
            // Corners first (smaller bullseye), then edges.
            if (IsNearF(localPt, new PointF(f.Left, f.Top), tol))
                return ImageLayerHandle.TopLeft;
            if (IsNearF(localPt, new PointF(f.Right, f.Top), tol))
                return ImageLayerHandle.TopRight;
            if (IsNearF(localPt, new PointF(f.Right, f.Bottom), tol))
                return ImageLayerHandle.BottomRight;
            if (IsNearF(localPt, new PointF(f.Left, f.Bottom), tol))
                return ImageLayerHandle.BottomLeft;
            if (System.Math.Abs(localPt.Y - f.Top) <= tol && localPt.X >= f.Left && localPt.X <= f.Right)
                return ImageLayerHandle.Top;
            if (System.Math.Abs(localPt.Y - f.Bottom) <= tol && localPt.X >= f.Left && localPt.X <= f.Right)
                return ImageLayerHandle.Bottom;
            if (System.Math.Abs(localPt.X - f.Left) <= tol && localPt.Y >= f.Top && localPt.Y <= f.Bottom)
                return ImageLayerHandle.Left;
            if (System.Math.Abs(localPt.X - f.Right) <= tol && localPt.Y >= f.Top && localPt.Y <= f.Bottom)
                return ImageLayerHandle.Right;
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
            float zoom = viewport != null ? (float)viewport.ZoomLevel : 1f;
            if (zoom <= 0f)
                zoom = 1f;
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

        private static bool IsCornerHandle(ImageLayerHandle h) => h == ImageLayerHandle.TopLeft || h == ImageLayerHandle.TopRight || h == ImageLayerHandle.BottomLeft || h == ImageLayerHandle.BottomRight;
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
            viewport?.Invalidate();
        }

        private void UpdateLayerInteraction(Point pixelPoint)
        {
            if (!isLayerInteractionActive || selectedLayerIndex < 0)
                return;
            // Rotation is handled separately — it only updates RotationDeg, not Frame.
            if (activeLayerHandle == ImageLayerHandle.Rotate)
            {
                float lx = layerInteractionStartFrame.X + layerInteractionStartFrame.Width / 2f;
                float ly = layerInteractionStartFrame.Y + layerInteractionStartFrame.Height / 2f;
                double startAngle = System.Math.Atan2(layerInteractionOriginPixel.Y - ly, layerInteractionOriginPixel.X - lx) * 180.0 / System.Math.PI;
                double currentAngle = System.Math.Atan2(pixelPoint.Y - ly, pixelPoint.X - lx) * 180.0 / System.Math.PI;
                float newRotation = layerInteractionStartRotation + (float)(currentAngle - startAngle);
                // Normalize to (-180, 180].
                while (newRotation > 180f)
                    newRotation -= 360f;
                while (newRotation <= -180f)
                    newRotation += 360f;
                // Shift: snap to 15° increments.
                if (ModifierKeys.HasFlag(System.Windows.Forms.Keys.Shift))
                    newRotation = (float)(System.Math.Round(newRotation / 15.0) * 15.0);
                var rotLayer = imageLayers[selectedLayerIndex];
                if (rotLayer.RotationDeg != newRotation)
                {
                    rotLayer.RotationDeg = newRotation;
                    layerChangedDuringInteraction = true;
                    viewport?.Invalidate();
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
                    viewport?.Invalidate();
                }

                return;
            }

            bool isCropping = activeLayerHandle != ImageLayerHandle.Body && IsLayerCropModifierDown;
            if (isCropping)
            {
                ApplyLayerCropDrag(layer, pixelPoint);
                return;
            }

            // Shift inverts the aspect lock for the drag: free resize while the lock
            // is on (the default), proportional while it's off.
            bool preserveAspect = IsCornerHandle(activeLayerHandle) && (LayerAspectRatioLocked ^ IsLayerAspectInvertModifierDown);
            GetLayerResizeDimensions(pixelPoint, preserveAspect, out float targetWidth, out float targetHeight);
            var next = CreateAnchoredResizeFrame(targetWidth, targetHeight);
            if (layer.Frame != next)
            {
                layer.Frame = next;
                layerChangedDuringInteraction = true;
                viewport?.Invalidate();
            }
        }

        private bool IsLayerCropModifierDown => isLayerCropModifierHeld_TestOverride ?? ModifierKeys.HasFlag(System.Windows.Forms.Keys.Control);
        private bool IsLayerAspectInvertModifierDown => isShiftHeld_TestOverride ?? ModifierKeys.HasFlag(System.Windows.Forms.Keys.Shift);

        private bool layerAspectRatioLocked = true;
        private bool LayerAspectRatioLocked => layerAspectRatioLocked;

        private void GetLayerResizeDimensions(Point pixelPoint, bool preserveAspect, out float targetWidth, out float targetHeight)
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
            if (HandleMovesLeft(activeLayerHandle))
                targetWidth -= localDx;
            if (HandleMovesRight(activeLayerHandle))
                targetWidth += localDx;
            if (HandleMovesTop(activeLayerHandle))
                targetHeight -= localDy;
            if (HandleMovesBottom(activeLayerHandle))
                targetHeight += localDy;
            if (preserveAspect && frame.Width > 0f && frame.Height > 0f)
            {
                float scaleByWidth = targetWidth / frame.Width;
                float scaleByHeight = targetHeight / frame.Height;
                float scale = Math.Abs(scaleByWidth - 1f) >= Math.Abs(scaleByHeight - 1f) ? scaleByWidth : scaleByHeight;
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
            var startCenter = new PointF(start.X + start.Width / 2f, start.Y + start.Height / 2f);
            var originalFixedOffset = GetResizeFixedAnchorOffset(start.Width, start.Height, activeLayerHandle);
            var fixedAnchor = Add(startCenter, RotateVector(originalFixedOffset, layerInteractionStartRotation));
            var newFixedOffset = GetResizeFixedAnchorOffset(width, height, activeLayerHandle);
            var newCenter = Subtract(fixedAnchor, RotateVector(newFixedOffset, layerInteractionStartRotation));
            return new RectangleF(newCenter.X - width / 2f, newCenter.Y - height / 2f, width, height);
        }

        private static PointF GetResizeFixedAnchorOffset(float width, float height, ImageLayerHandle draggedHandle)
        {
            float x = HandleMovesLeft(draggedHandle) ? width / 2f : HandleMovesRight(draggedHandle) ? -width / 2f : 0f;
            float y = HandleMovesTop(draggedHandle) ? height / 2f : HandleMovesBottom(draggedHandle) ? -height / 2f : 0f;
            return new PointF(x, y);
        }

        private static PointF RotateVector(PointF vector, float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            return new PointF(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
        }

        private static PointF Add(PointF a, PointF b) => new PointF(a.X + b.X, a.Y + b.Y);
        private static PointF Subtract(PointF a, PointF b) => new PointF(a.X - b.X, a.Y - b.Y);
        private static bool HandleMovesLeft(ImageLayerHandle handle) => handle == ImageLayerHandle.TopLeft || handle == ImageLayerHandle.BottomLeft || handle == ImageLayerHandle.Left;
        private static bool HandleMovesRight(ImageLayerHandle handle) => handle == ImageLayerHandle.TopRight || handle == ImageLayerHandle.BottomRight || handle == ImageLayerHandle.Right;
        private static bool HandleMovesTop(ImageLayerHandle handle) => handle == ImageLayerHandle.TopLeft || handle == ImageLayerHandle.TopRight || handle == ImageLayerHandle.Top;
        private static bool HandleMovesBottom(ImageLayerHandle handle) => handle == ImageLayerHandle.BottomLeft || handle == ImageLayerHandle.BottomRight || handle == ImageLayerHandle.Bottom;
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
            viewport?.Invalidate();
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
            _ = handleAtBegin; // currently unused; kept for future per-handle metadata.
            CommitLayerInteractionUndo();
        }

        /// <summary>
        /// Push the layer diff captured since the interaction began as one undo step, or drop
        /// the snapshot when nothing actually moved. Shared by the mouse gesture and the
        /// keyboard one so both produce identical undo entries.
        /// </summary>
        private void CommitLayerInteractionUndo()
        {
            if (!layerChangedDuringInteraction || layerInteractionLayersBefore == null)
            {
                // Interaction was a click without drag — discard the captured before-state.
                if (layerInteractionLayersBefore != null)
                {
                    foreach (var l in layerInteractionLayersBefore)
                        l.Dispose();
                    layerInteractionLayersBefore = null;
                }

                return;
            }

            var layersAfter = CloneLayers();
            // Capture the base bitmap on every layer-only step too (ReplacesImage=true). This
            // costs a bitmap clone per drag/resize but lets undo across a commit-flatten restore
            // the unflattened baseline. Without it, undoing a layer-drag after commit shows the
            // baked-in flattened layer plus the restored live layer (double render).
            Bitmap? baseClone = viewport?.Image is Bitmap b ? new Bitmap(b) : null;
            Bitmap? baseClone2 = viewport?.Image is Bitmap b2 ? new Bitmap(b2) : null;
            PushUndoStep(Rectangle.Empty, baseClone, baseClone2, Selection, Selection, replacesImage: baseClone != null, shapesBefore: null, shapesAfter: null, textsBefore: null, textsAfter: null, layersBefore: layerInteractionLayersBefore, layersAfter: layersAfter);
            layerInteractionLayersBefore = null;
            layerChangedDuringInteraction = false;
        }

        // Keyboard move/resize of the selected layer. Mirrors the annotation gesture: presses
        // coalesce into one undo step, closed when the arrow key comes back up.
        private bool layerKeyTransformActive;
        /// <summary>
        /// Arrow keys drive the selected image layer, matching what they already do for shapes
        /// and texts: plain arrows nudge it a pixel, Shift accelerates to 10, and Ctrl resizes
        /// from the frame's top-left (free aspect — the aspect lock belongs to corner drags).
        /// </summary>
        private bool TryHandleLayerArrowKey(System.Windows.Forms.Keys keyData)
        {
            var code = keyData & System.Windows.Forms.Keys.KeyCode;
            bool ctrl = (keyData & System.Windows.Forms.Keys.Control) == System.Windows.Forms.Keys.Control;
            bool alt = (keyData & System.Windows.Forms.Keys.Alt) == System.Windows.Forms.Keys.Alt;
            bool shift = (keyData & System.Windows.Forms.Keys.Shift) == System.Windows.Forms.Keys.Shift;
            if (alt || !HasSelectedLayer || !HasEditableImage)
            {
                return false;
            }

            if (isStraightenToolActive || isCensorToolActive || isFreeRotateToolActive)
            {
                return false;
            }

            if (activeTextAnnotation?.IsEditing == true)
            {
                return false;
            }

            // A mouse gesture owns the layer while it is in flight.
            if (isLayerInteractionActive && !layerKeyTransformActive)
            {
                return false;
            }

            int step = shift ? 10 : 1;
            var delta = code switch
            {
                System.Windows.Forms.Keys.Left => new SizeF(-step, 0),
                System.Windows.Forms.Keys.Right => new SizeF(step, 0),
                System.Windows.Forms.Keys.Up => new SizeF(0, -step),
                System.Windows.Forms.Keys.Down => new SizeF(0, step),
                _ => SizeF.Empty,
            };
            if (delta.IsEmpty)
            {
                return false;
            }

            BeginLayerKeyTransform();
            var layer = imageLayers[selectedLayerIndex];
            var frame = layer.Frame;
            RectangleF next;
            if (ctrl)
            {
                // Anchored at the top-left, like the shape keyboard resize.
                next = new RectangleF(frame.X, frame.Y, Math.Max(1f, frame.Width + delta.Width), Math.Max(1f, frame.Height + delta.Height));
            }
            else
            {
                next = frame;
                next.Offset(delta.Width, delta.Height);
            }

            if (next != frame)
            {
                layer.Frame = next;
                layerChangedDuringInteraction = true;
                viewport?.Invalidate();
            }

            // Claimed either way, so a clamped press cannot become focus navigation.
            return true;
        }

        private void BeginLayerKeyTransform()
        {
            if (layerKeyTransformActive)
            {
                return;
            }

            layerKeyTransformActive = true;
            layerInteractionLayersBefore ??= CloneLayers();
            layerChangedDuringInteraction = false;
        }

        /// <summary>
        /// Close the keyboard layer gesture, committing the accumulated presses as one step.
        /// </summary>
        private void EndLayerKeyTransform()
        {
            if (!layerKeyTransformActive)
            {
                return;
            }

            layerKeyTransformActive = false;
            CommitLayerInteractionUndo();
        }

        private void DrawSelectedLayerOverlay(Graphics graphics)
        {
            if (selectedLayerIndex < 0 || selectedLayerIndex >= imageLayers.Count)
            {
                return;
            }

            // No frame or grips around something that is not being drawn.
            if (!imageLayers[selectedLayerIndex].IsVisible)
            {
                return;
            }

            float zoom = viewport != null ? (float)viewport.ZoomLevel : 1f;
            PointF pan = viewport != null ? viewport.Metrics.PanOffset : PointF.Empty;
            var f = imageLayers[selectedLayerIndex].Frame;
            float rotDeg = imageLayers[selectedLayerIndex].RotationDeg;
            // Screen-space center of the layer
            float cx = pan.X + (f.X + f.Width / 2f) * zoom;
            float cy = pan.Y + (f.Y + f.Height / 2f) * zoom;
            float hw = f.Width * zoom / 2f;
            float hh = f.Height * zoom / 2f;
            var state = graphics.Save();
            graphics.TranslateTransform(cx, cy);
            if (rotDeg != 0f)
                graphics.RotateTransform(rotDeg);
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

        private void DrawImageLayers(Graphics graphics, AnnotationSurface surface) => DrawImageLayers(graphics, imageLayers, surface);
        private void DrawImageLayers(Graphics graphics, IReadOnlyList<ImageLayer> layers, AnnotationSurface surface)
        {
            if (layers.Count == 0)
            {
                return;
            }

            float zoom = 1f;
            PointF pan = PointF.Empty;
            if (surface == AnnotationSurface.Screen && viewport != null)
            {
                zoom = (float)viewport.ZoomLevel;
                pan = viewport.Metrics.PanOffset;
            }

            foreach (var layer in layers)
            {
                if (!layer.IsVisible)
                {
                    continue;
                }

                var dest = new RectangleF(pan.X + layer.Frame.X * zoom, pan.Y + layer.Frame.Y * zoom, layer.Frame.Width * zoom, layer.Frame.Height * zoom);
                if (layer.RotationDeg != 0f)
                {
                    float cx = dest.X + dest.Width / 2f;
                    float cy = dest.Y + dest.Height / 2f;
                    var state = graphics.Save();
                    graphics.TranslateTransform(cx, cy);
                    graphics.RotateTransform(layer.RotationDeg);
                    graphics.DrawImage(layer.Source, new RectangleF(-dest.Width / 2f, -dest.Height / 2f, dest.Width, dest.Height), layer.Fill, GraphicsUnit.Pixel);
                    graphics.Restore(state);
                }
                else if (dest.Width == layer.Fill.Width && dest.Height == layer.Fill.Height)
                {
                    // Native scale: the layer must land pixel-for-pixel. Graphics.FromImage hands
                    // back GDI+'s defaults (bilinear + PixelOffsetMode.None), which stay exact on
                    // a whole-pixel destination but smear the entire region across its neighbours
                    // the moment it is not — so pin the sampling rather than trusting the caller's
                    // state and the frame's fractional part. Same convention as the censor, rotate
                    // and resize paths. Scaled layers fall through and keep the caller's setup.
                    var previousInterpolation = graphics.InterpolationMode;
                    var previousPixelOffset = graphics.PixelOffsetMode;
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    graphics.DrawImage(layer.Source, dest, layer.Fill, GraphicsUnit.Pixel);
                    graphics.InterpolationMode = previousInterpolation;
                    graphics.PixelOffsetMode = previousPixelOffset;
                }
                else
                {
                    graphics.DrawImage(layer.Source, dest, layer.Fill, GraphicsUnit.Pixel);
                }
            }
        }

        /// <summary>
        /// Glue the selected layer down. Nothing selected means nothing to glue — Enter and the
        /// Apply command are both no-ops there rather than quietly burning the whole stack.
        /// </summary>
        private bool ApplyFloatingPaste() => HasSelectedLayer && ApplyFloatingPaste(selectedLayerIndex);
        /// <summary>
        /// Burn one layer into the pixel buffer. A hardened layer joins the canvas, so anything
        /// still floating now draws above it even if it used to sit underneath: "glue this one
        /// down" puts it in the background, and floating things are on top of the background.
        /// </summary>
        private bool ApplyFloatingPaste(int index)
        {
            if (index < 0 || index >= imageLayers.Count)
                return false;
            if (viewport?.Image == null)
                return false;
            var doomed = imageLayers[index];
            // Gluing down something you have muted would drop invisible pixels into the canvas.
            if (!doomed.IsVisible)
                return false;
            var beforeImage = new Bitmap(viewport.Image);
            var layersBefore = CloneLayers();
            var selectionBefore = Selection;
            // Only this layer gets burned in. BuildCompositeImage would also flatten the live
            // annotations and texts, which stay live here and would then render twice.
            var afterImage = new Bitmap(viewport.Image);
            using (var graphics = Graphics.FromImage(afterImage))
            {
                DrawImageLayers(graphics, new[] { doomed }, AnnotationSurface.Image);
            }

            var currentZoom = ZoomLevel;
            viewport.Image = afterImage;
            ZoomLevel = currentZoom;
            viewport.ClampPan();
            imageLayers.Remove(doomed);
            // Hand the selection to the top of what is left so a second Enter glues the next
            // layer down — mashing Enter walks the stack one layer at a time.
            selectedLayerIndex = imageLayers.Count > 0 ? imageLayers.Count - 1 : -1;
            ResetLayerInteractionState();
            var layersAfter = CloneLayers();
            PushUndoStep(Rectangle.Empty, beforeImage, new Bitmap(afterImage), selectionBefore, Selection, replacesImage: true, shapesBefore: null, shapesAfter: null, textsBefore: null, textsAfter: null, layersBefore: layersBefore, layersAfter: layersAfter);
            viewport.Invalidate();
            return true;
        }

        private bool HasSelectedLayer => selectedLayerIndex >= 0 && selectedLayerIndex < imageLayers.Count;

        private bool TryDeleteSelectedLayer() => HasSelectedLayer && TryDeleteLayerAt(selectedLayerIndex);
        private bool TryDeleteLayerAt(int index)
        {
            if (index < 0 || index >= imageLayers.Count)
                return false;
            var layersBefore = CloneLayers();
            var doomed = imageLayers[index];
            imageLayers.RemoveAt(index);
            selectedLayerIndex = -1;
            var layersAfter = CloneLayers();
            Bitmap? baseClone = viewport?.Image is Bitmap b ? new Bitmap(b) : null;
            Bitmap? baseClone2 = viewport?.Image is Bitmap b2 ? new Bitmap(b2) : null;
            PushUndoStep(Rectangle.Empty, baseClone, baseClone2, Selection, Selection, replacesImage: baseClone != null, shapesBefore: null, shapesAfter: null, textsBefore: null, textsAfter: null, layersBefore: layersBefore, layersAfter: layersAfter);
            viewport?.Invalidate();
            return true;
        }

        private bool DeselectImageLayerIfAny()
        {
            if (!HasSelectedLayer)
                return false;
            selectedLayerIndex = -1;
            viewport?.Invalidate();
            return true;
        }

        /// <summary>
        /// Move the selected layer so its frame origin lands on <paramref name = "value"/>. The
        /// coordinate is the unrotated frame's top-left, i.e. the same origin a body drag moves,
        /// so typing one X into several layers lines their frames up exactly.
        /// </summary>
        private void ApplySelectedLayerPosition(float value, bool updateX)
        {
            if (!HasSelectedLayer || !float.IsFinite(value))
            {
                return;
            }

            var layer = imageLayers[selectedLayerIndex];
            var before = CloneLayers();
            var frame = layer.Frame;
            layer.Frame = updateX ? new RectangleF(value, frame.Y, frame.Width, frame.Height) : new RectangleF(frame.X, value, frame.Width, frame.Height);
            CompleteLayerPropertyChange(before, frame, layer.Fill, layer.RotationDeg);
        }

        private void ApplySelectedLayerDimension(float value, bool updateWidth)
        {
            if (!HasSelectedLayer || !float.IsFinite(value) || value < 1f)
            {
                return;
            }

            var layer = imageLayers[selectedLayerIndex];
            var before = CloneLayers();
            var frame = layer.Frame;
            if (updateWidth)
            {
                float height = LayerAspectRatioLocked && frame.Width > 0f ? Math.Max(1f, value * frame.Height / frame.Width) : frame.Height;
                layer.Frame = ResizeFrameKeepingVisualTopLeft(frame, layer.RotationDeg, value, height);
            }
            else
            {
                float width = LayerAspectRatioLocked && frame.Height > 0f ? Math.Max(1f, value * frame.Width / frame.Height) : frame.Width;
                layer.Frame = ResizeFrameKeepingVisualTopLeft(frame, layer.RotationDeg, width, value);
            }

            CompleteLayerPropertyChange(before, frame, layer.Fill, layer.RotationDeg);
        }

        private static RectangleF ResizeFrameKeepingVisualTopLeft(RectangleF frame, float rotationDegrees, float width, float height)
        {
            var oldCenter = new PointF(frame.X + frame.Width / 2f, frame.Y + frame.Height / 2f);
            var fixedTopLeft = Add(oldCenter, RotateVector(new PointF(-frame.Width / 2f, -frame.Height / 2f), rotationDegrees));
            var newCenter = Subtract(fixedTopLeft, RotateVector(new PointF(-width / 2f, -height / 2f), rotationDegrees));
            return new RectangleF(newCenter.X - width / 2f, newCenter.Y - height / 2f, width, height);
        }

        private void ApplySelectedLayerAngle(float angle)
        {
            if (!HasSelectedLayer || !float.IsFinite(angle))
            {
                return;
            }

            var layer = imageLayers[selectedLayerIndex];
            var before = CloneLayers();
            var frameBefore = layer.Frame;
            var fillBefore = layer.Fill;
            float rotationBefore = layer.RotationDeg;
            layer.RotationDeg = NormalizeLayerAngle(angle);
            CompleteLayerPropertyChange(before, frameBefore, fillBefore, rotationBefore);
        }

        private static float NormalizeLayerAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            if (angle <= -180f)
                angle += 360f;
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
            layer.Frame = new RectangleF(layer.Frame.X, layer.Frame.Y, layer.Source.Width, layer.Source.Height);
            layer.Fill = new RectangleF(0f, 0f, layer.Source.Width, layer.Source.Height);
            layer.RotationDeg = 0f;
            CompleteLayerPropertyChange(before, frameBefore, fillBefore, rotationBefore);
        }

        private void CompleteLayerPropertyChange(List<ImageLayer> layersBefore, RectangleF frameBefore, RectangleF fillBefore, float rotationBefore)
        {
            var layer = imageLayers[selectedLayerIndex];
            if (layer.Frame == frameBefore && layer.Fill == fillBefore && layer.RotationDeg == rotationBefore)
            {
                DisposeOrphanedLayers(layersBefore);
                return;
            }

            PushLayerOnlyUndoStep(layersBefore);
            viewport?.Invalidate();
        }

        private void PushLayerOnlyUndoStep(List<ImageLayer> layersBefore)
        {
            var layersAfter = CloneLayers();
            Bitmap? baseClone = viewport?.Image is Bitmap before ? new Bitmap(before) : null;
            Bitmap? baseClone2 = viewport?.Image is Bitmap after ? new Bitmap(after) : null;
            PushUndoStep(Rectangle.Empty, baseClone, baseClone2, Selection, Selection, replacesImage: baseClone != null, shapesBefore: null, shapesAfter: null, textsBefore: null, textsAfter: null, layersBefore: layersBefore, layersAfter: layersAfter);
        }

        private static EditorCursor CursorForLayerHandle(ImageLayerHandle handle)
        {
            return handle switch
            {
                ImageLayerHandle.Body => EditorCursor.SizeAll,
                ImageLayerHandle.TopLeft => EditorCursor.SizeNWSE,
                ImageLayerHandle.BottomRight => EditorCursor.SizeNWSE,
                ImageLayerHandle.TopRight => EditorCursor.SizeNESW,
                ImageLayerHandle.BottomLeft => EditorCursor.SizeNESW,
                ImageLayerHandle.Top => EditorCursor.SizeNS,
                ImageLayerHandle.Bottom => EditorCursor.SizeNS,
                ImageLayerHandle.Left => EditorCursor.SizeWE,
                ImageLayerHandle.Right => EditorCursor.SizeWE,
                ImageLayerHandle.Rotate => EditorCursor.Cross,
                _ => EditorCursor.Default,
            };
        }
    }
}
