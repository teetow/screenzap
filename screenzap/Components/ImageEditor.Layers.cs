using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

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
        Left
    }

    public partial class ImageEditor
    {
        private readonly List<ImageLayer> imageLayers = new List<ImageLayer>();
        private int selectedLayerIndex = -1;
        private bool isLayerInteractionActive;
        private ImageLayerHandle activeLayerHandle = ImageLayerHandle.None;
        private Point layerInteractionOriginPixel;
        private RectangleF layerInteractionStartFrame;
        private List<ImageLayer>? layerInteractionLayersBefore;
        private bool layerChangedDuringInteraction;

        // Handle dimensions in screen pixels (constant regardless of zoom).
        private const float LayerHandleScreenSize = 8f;

        internal int ImageLayerCountForTests => imageLayers.Count;

        internal RectangleF GetImageLayerFrameForTests(int index) => imageLayers[index].Frame;

        internal Bitmap BuildCompositeImageForTests() => BuildCompositeImage();

        internal Bitmap? CloneBaseBitmapForTests() => pictureBox1?.Image is Bitmap b ? new Bitmap(b) : null;

        internal void RenderScreenLayersForTests(Graphics graphics)
        {
            DrawImageLayers(graphics, AnnotationSurface.Screen);
            DrawSelectedLayerOverlay(graphics);
        }

        internal int SelectedLayerIndexForTests => selectedLayerIndex;

        internal void SetSelectedLayerForTests(int index) => SelectImageLayer(index);

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

            ClearImageLayers();

            foreach (var layer in source)
            {
                imageLayers.Add(layer.Clone());
            }

            // Selection survives if the index is still valid; otherwise reset.
            if (selectedLayerIndex >= imageLayers.Count)
            {
                selectedLayerIndex = -1;
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
        }

        private void SelectImageLayer(int index)
        {
            if (index < -1 || index >= imageLayers.Count)
            {
                index = -1;
            }
            if (selectedLayerIndex == index) return;
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

            pictureBox1?.Invalidate();
        }

        private int? HitTestLayerBody(Point pixelPoint)
        {
            // Top-most first.
            for (int i = imageLayers.Count - 1; i >= 0; i--)
            {
                if (imageLayers[i].Frame.Contains(pixelPoint.X, pixelPoint.Y))
                {
                    return i;
                }
            }
            return null;
        }

        private ImageLayerHandle HitTestSelectedLayerHandle(Point pixelPoint)
        {
            if (selectedLayerIndex < 0 || selectedLayerIndex >= imageLayers.Count)
            {
                return ImageLayerHandle.None;
            }

            var layer = imageLayers[selectedLayerIndex];
            float tol = HandleHitToleranceImagePixels();

            // Convert the pixel point to screen for handle testing — handles are screen-sized,
            // not image-sized, so a fixed image-pixel tolerance grows/shrinks inversely with zoom.
            float zoom = pictureBox1 != null ? (float)pictureBox1.ZoomLevel : 1f;
            if (zoom <= 0f) zoom = 1f;
            float screenTol = LayerHandleScreenSize / 2f;
            tol = screenTol / zoom;

            var f = layer.Frame;
            // Order: corners first (smaller bullseye), then edges.
            if (IsNear(pixelPoint, new PointF(f.Left, f.Top), tol)) return ImageLayerHandle.TopLeft;
            if (IsNear(pixelPoint, new PointF(f.Right, f.Top), tol)) return ImageLayerHandle.TopRight;
            if (IsNear(pixelPoint, new PointF(f.Right, f.Bottom), tol)) return ImageLayerHandle.BottomRight;
            if (IsNear(pixelPoint, new PointF(f.Left, f.Bottom), tol)) return ImageLayerHandle.BottomLeft;

            // Edge midpoints — we accept hits along the edge, not just at the midpoint.
            if (System.Math.Abs(pixelPoint.Y - f.Top) <= tol && pixelPoint.X >= f.Left && pixelPoint.X <= f.Right) return ImageLayerHandle.Top;
            if (System.Math.Abs(pixelPoint.Y - f.Bottom) <= tol && pixelPoint.X >= f.Left && pixelPoint.X <= f.Right) return ImageLayerHandle.Bottom;
            if (System.Math.Abs(pixelPoint.X - f.Left) <= tol && pixelPoint.Y >= f.Top && pixelPoint.Y <= f.Bottom) return ImageLayerHandle.Left;
            if (System.Math.Abs(pixelPoint.X - f.Right) <= tol && pixelPoint.Y >= f.Top && pixelPoint.Y <= f.Bottom) return ImageLayerHandle.Right;

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
            layerInteractionLayersBefore = CloneLayers();
            layerChangedDuringInteraction = false;
            pictureBox1?.Invalidate();
        }

        private void UpdateLayerInteraction(Point pixelPoint)
        {
            if (!isLayerInteractionActive || selectedLayerIndex < 0) return;

            float dx = pixelPoint.X - layerInteractionOriginPixel.X;
            float dy = pixelPoint.Y - layerInteractionOriginPixel.Y;
            var start = layerInteractionStartFrame;
            RectangleF next = start;

            switch (activeLayerHandle)
            {
                case ImageLayerHandle.Body:
                    next = new RectangleF(start.X + dx, start.Y + dy, start.Width, start.Height);
                    break;
                case ImageLayerHandle.TopLeft:
                    next = RectangleF.FromLTRB(start.Left + dx, start.Top + dy, start.Right, start.Bottom);
                    break;
                case ImageLayerHandle.Top:
                    next = RectangleF.FromLTRB(start.Left, start.Top + dy, start.Right, start.Bottom);
                    break;
                case ImageLayerHandle.TopRight:
                    next = RectangleF.FromLTRB(start.Left, start.Top + dy, start.Right + dx, start.Bottom);
                    break;
                case ImageLayerHandle.Right:
                    next = RectangleF.FromLTRB(start.Left, start.Top, start.Right + dx, start.Bottom);
                    break;
                case ImageLayerHandle.BottomRight:
                    next = RectangleF.FromLTRB(start.Left, start.Top, start.Right + dx, start.Bottom + dy);
                    break;
                case ImageLayerHandle.Bottom:
                    next = RectangleF.FromLTRB(start.Left, start.Top, start.Right, start.Bottom + dy);
                    break;
                case ImageLayerHandle.BottomLeft:
                    next = RectangleF.FromLTRB(start.Left + dx, start.Top, start.Right, start.Bottom + dy);
                    break;
                case ImageLayerHandle.Left:
                    next = RectangleF.FromLTRB(start.Left + dx, start.Top, start.Right, start.Bottom);
                    break;
            }

            // Shift-key: preserve aspect ratio for corner handles.
            if (activeLayerHandle != ImageLayerHandle.Body && IsCornerHandle(activeLayerHandle)
                && System.Windows.Forms.Control.ModifierKeys.HasFlag(System.Windows.Forms.Keys.Shift)
                && start.Width > 0f && start.Height > 0f)
            {
                float scaleByW = next.Width / start.Width;
                float scaleByH = next.Height / start.Height;
                float scale = (System.Math.Abs(scaleByW - 1f) >= System.Math.Abs(scaleByH - 1f)) ? scaleByW : scaleByH;
                if (scale <= 0f) scale = 0.001f;
                float targetW = start.Width * scale;
                float targetH = start.Height * scale;
                // Anchor at the fixed corner (opposite to the dragged corner).
                switch (activeLayerHandle)
                {
                    case ImageLayerHandle.TopLeft:
                        next = RectangleF.FromLTRB(next.Right - targetW, next.Bottom - targetH, next.Right, next.Bottom);
                        break;
                    case ImageLayerHandle.TopRight:
                        next = RectangleF.FromLTRB(next.Left, next.Bottom - targetH, next.Left + targetW, next.Bottom);
                        break;
                    case ImageLayerHandle.BottomLeft:
                        next = RectangleF.FromLTRB(next.Right - targetW, next.Top, next.Right, next.Top + targetH);
                        break;
                    case ImageLayerHandle.BottomRight:
                        next = RectangleF.FromLTRB(next.Left, next.Top, next.Left + targetW, next.Top + targetH);
                        break;
                }
            }

            // Disallow zero/negative dimensions; clamp to a 1px minimum.
            if (next.Width < 1f) next.Width = 1f;
            if (next.Height < 1f) next.Height = 1f;

            var layer = imageLayers[selectedLayerIndex];
            if (layer.Frame != next)
            {
                layer.Frame = next;
                layerChangedDuringInteraction = true;
                pictureBox1?.Invalidate();
            }
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
            var screenRect = new RectangleF(
                pan.X + f.X * zoom,
                pan.Y + f.Y * zoom,
                f.Width * zoom,
                f.Height * zoom);

            using (var pen = new Pen(Color.DodgerBlue, 1.25f))
            {
                pen.DashStyle = DashStyle.Solid;
                graphics.DrawRectangle(pen, screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height);
            }

            DrawLayerHandle(graphics, screenRect.Left, screenRect.Top);
            DrawLayerHandle(graphics, screenRect.Left + screenRect.Width / 2f, screenRect.Top);
            DrawLayerHandle(graphics, screenRect.Right, screenRect.Top);
            DrawLayerHandle(graphics, screenRect.Right, screenRect.Top + screenRect.Height / 2f);
            DrawLayerHandle(graphics, screenRect.Right, screenRect.Bottom);
            DrawLayerHandle(graphics, screenRect.Left + screenRect.Width / 2f, screenRect.Bottom);
            DrawLayerHandle(graphics, screenRect.Left, screenRect.Bottom);
            DrawLayerHandle(graphics, screenRect.Left, screenRect.Top + screenRect.Height / 2f);
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

                graphics.DrawImage(layer.Source, dest, layer.Fill, GraphicsUnit.Pixel);
            }
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
            pictureBox1?.Invalidate();
            return true;
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
                _ => Cursors.Default,
            };
        }
    }
}
