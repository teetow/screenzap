using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using screenzap.lib;

namespace screenzap
{
    internal enum DrawingTool
    {
        None,
        Arrow,
        Rectangle,
        Highlighter
    }

    internal enum AnnotationHandle
    {
        None,
        Move,
        ArrowStart,
        ArrowEnd,
        RectTopLeft,
        RectTopRight,
        RectBottomLeft,
        RectBottomRight
    }

    internal enum AnnotationType
    {
        Arrow,
        Rectangle,
        Highlighter
    }

    internal sealed class AnnotationShape
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public AnnotationType Type { get; init; }
        public Point Start { get; set; }
        public Point End { get; set; }
        public float LineThickness { get; set; } = 2f;
        public float ArrowSize { get; set; } = 1f; // Multiplier for arrow head size
        public Color Color { get; set; } = Color.Red;
        public float Opacity { get; set; } = 0.40f;
        public bool Selected { get; set; }

        // Sampled freehand path. Non-null only for Highlighter shapes. Start/End are kept
        // synced to Points[0]/Points[^1] so the endpoint-based machinery (bounds fallback,
        // crop, clamp) keeps working without special-casing every call site.
        public List<Point>? Points { get; set; }

        public AnnotationShape Clone()
        {
            return new AnnotationShape
            {
                Id = Id,
                Type = Type,
                Start = Start,
                End = End,
                LineThickness = LineThickness,
                ArrowSize = ArrowSize,
                Color = Color,
                Opacity = Opacity,
                Selected = Selected,
                Points = Points != null ? new List<Point>(Points) : null
            };
        }

        public Rectangle GetBounds()
        {
            if (Points != null && Points.Count > 0)
            {
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var p in Points)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
                return Rectangle.FromLTRB(minX, minY, maxX, maxY);
            }

            return RectangleExt.fromPoints(Start, End);
        }

        public bool IsValid()
        {
            if (Type == AnnotationType.Highlighter)
            {
                if (Points == null || Points.Count < 2)
                {
                    return false;
                }

                double length = 0;
                for (int i = 1; i < Points.Count; i++)
                {
                    double dx = Points[i].X - Points[i - 1].X;
                    double dy = Points[i].Y - Points[i - 1].Y;
                    length += Math.Sqrt(dx * dx + dy * dy);
                }
                return length >= 4.0;
            }

            if (Type == AnnotationType.Arrow)
            {
                return !Start.Equals(End);
            }

            var bounds = GetBounds();
            return bounds.Width > 1 && bounds.Height > 1;
        }
    }

    public partial class ImageEditor
    {
        private readonly List<AnnotationShape> annotationShapes = new List<AnnotationShape>();
        private bool isDrawingAnnotation;
        private AnnotationShape? workingAnnotation;
        // Multi-selection: selectedShapes is the source of truth, selectedAnnotation mirrors
        // selectedShapes.LastOrDefault() to keep the ~30 existing single-target read sites
        // working without churn. All writes go through SelectAnnotation / SyncSelectedAnnotation.
        private readonly List<AnnotationShape> selectedShapes = new List<AnnotationShape>();
        private AnnotationShape? selectedAnnotation;
        private AnnotationShape? hoveredAnnotation;
        private AnnotationHandle activeAnnotationHandle = AnnotationHandle.None;
        private Point annotationDragOriginPixel;
        private Point annotationDraftAnchorPixel;
        private bool annotationTranslateModeActive;
        private Point annotationTranslationOriginPixel;
        private Point annotationTranslationStartSnapshot;
        private Point annotationTranslationEndSnapshot;
        private Point annotationTranslationAnchorSnapshot;
        private List<AnnotationShape>? annotationSnapshotBeforeEdit;
        private bool annotationChangedDuringDrag;
        // Captured at the start of a highlighter corner-resize so each mouse-move scales the
        // ORIGINAL polyline (no per-move accumulation drift).
        private List<Point>? highlighterResizeOriginalPoints;
        private Rectangle highlighterResizeOriginalBounds;

        private const float DefaultHighlighterPeakOpacity = 0.40f;
        private const float DefaultHighlighterBodyOpacity = 0.26f;
        private const double HighlighterBodyLevel = DefaultHighlighterBodyOpacity / DefaultHighlighterPeakOpacity;

        // Annotation tool settings
        private float annotationLineThickness = 2f;
        private float annotationArrowSize = 1f;
        private float annotationHighlighterThickness = 12f;
        private Color annotationColor = Color.Red;
        // The highlighter keeps its own default so it lands lemon-yellow rather than inheriting the
        // arrow/rectangle red. Picking a color while the highlighter tool is active updates this one.
        private Color annotationHighlighterColor = Color.FromArgb(255, 238, 88);
        private float annotationHighlighterOpacity = DefaultHighlighterPeakOpacity;

        /// <summary>
        /// The tool default color the color picker reads/writes: the highlighter's own default when
        /// that tool is active, otherwise the shared arrow/rectangle default.
        /// </summary>
        private Color ActiveToolDefaultColor
        {
            get => activeDrawingTool == DrawingTool.Highlighter ? annotationHighlighterColor : annotationColor;
            set
            {
                if (activeDrawingTool == DrawingTool.Highlighter)
                {
                    annotationHighlighterColor = value;
                }
                else
                {
                    annotationColor = value;
                }
            }
        }

        private static float ClampHighlighterOpacity(float opacity)
        {
            return Math.Clamp(opacity, 0f, 1f);
        }

        private static int HighlighterOpacityToPercent(float opacity)
        {
            return (int)Math.Round(ClampHighlighterOpacity(opacity) * 100f);
        }

        private static float HighlighterOpacityFromPercent(int percent)
        {
            return Math.Clamp(percent, 0, 100) / 100f;
        }

        // Tests can't manipulate real keyboard state and Control.ModifierKeys is a static
        // property tied to OS input. Set via TestSetShiftHeld to simulate Shift-during-click.
        private bool isShiftHeld_TestOverride;
        private bool IsMultiSelectModifierDown =>
            isShiftHeld_TestOverride || ModifierKeys.HasFlag(Keys.Shift);

        // True while UpdateAnnotationToolbarFromSelection is programmatically pushing
        // values into the comboboxes so SelectedIndexChanged handlers don't write the
        // displayed value back into the selection (would create no-op undo steps and,
        // when the displayed value is a "Mixed" blank, would clobber the real per-shape
        // values).
        private bool isSyncingAnnotationToolbarControls;

        private List<AnnotationShape> CloneAnnotations()
        {
            return annotationShapes.Select(a => a.Clone()).ToList();
        }

        /// <summary>
        /// Update the shape selection. When <paramref name="add"/> is false (the default,
        /// matching legacy single-select behaviour), the selection is replaced with the
        /// target (or cleared when target is null). When add is true, the target is
        /// toggled into or out of the existing selection; passing null with add=true is
        /// a no-op.
        /// </summary>
        private void SelectAnnotation(AnnotationShape? target, bool add = false)
        {
            if (add)
            {
                if (target == null)
                {
                    return;
                }
                if (selectedShapes.Contains(target))
                {
                    selectedShapes.Remove(target);
                }
                else
                {
                    selectedShapes.Add(target);
                }
            }
            else
            {
                selectedShapes.Clear();
                if (target != null)
                {
                    selectedShapes.Add(target);
                }
            }

            SyncSelectionFlagsFromList();
            selectedAnnotation = selectedShapes.LastOrDefault();
            UpdateAnnotationToolbarFromSelection();
            UpdateAnnotationToolbarVisibility();
            pictureBox1?.Invalidate();
        }

        private void SyncSelectionFlagsFromList()
        {
            foreach (var annotation in annotationShapes)
            {
                annotation.Selected = selectedShapes.Contains(annotation);
            }
        }

        private void SyncSelectedAnnotation()
        {
            // Rebuild selectedShapes from each shape's Selected flag (used after undo/redo
            // or any code path that mutates annotationShapes wholesale).
            selectedShapes.Clear();
            foreach (var annotation in annotationShapes)
            {
                if (annotation.Selected)
                {
                    selectedShapes.Add(annotation);
                }
            }
            selectedAnnotation = selectedShapes.LastOrDefault();
        }

        private void UpdateDrawingToolButtons()
        {
            bool enable = HasEditableImage;

            if (!enable)
            {
                activeDrawingTool = DrawingTool.None;
                CancelAnnotationPreview();
            }

            if (arrowToolStripButton != null)
            {
                arrowToolStripButton.Enabled = enable;
                arrowToolStripButton.Checked = enable && activeDrawingTool == DrawingTool.Arrow;
            }

            if (rectangleToolStripButton != null)
            {
                rectangleToolStripButton.Enabled = enable;
                rectangleToolStripButton.Checked = enable && activeDrawingTool == DrawingTool.Rectangle;
            }

            if (highlighterToolStripButton != null)
            {
                highlighterToolStripButton.Enabled = enable;
                highlighterToolStripButton.Checked = enable && activeDrawingTool == DrawingTool.Highlighter;
            }

            UpdateAnnotationToolbarVisibility();
        }

        private void UpdateAnnotationToolbarVisibility()
        {
            bool isAnnotationToolActive = activeDrawingTool != DrawingTool.None;
            // Any selected shape (arrow or rect) keeps the options panel up. With multi-
            // selection this is "are there any shapes in the selection?" — texts alone
            // don't need the line-width / arrow controls visible.
            bool hasShapeSelection = selectedShapes.Count > 0;
            bool showPanel = isAnnotationToolActive || hasShapeSelection;
            bool showArrowControls =
                activeDrawingTool == DrawingTool.Arrow || AnyArrowInSelection();

            // Highlighter has its own 8–24 thickness combo; the regular 1–10 line combo
            // governs arrows/rects. Show whichever the current tool/selection calls for so
            // both thickness controls don't crowd the strip for a pure-highlighter context.
            bool showHighlighterControls =
                activeDrawingTool == DrawingTool.Highlighter || AnyHighlighterInSelection();
            bool nonHighlighterToolActive =
                isAnnotationToolActive && activeDrawingTool != DrawingTool.Highlighter;
            bool showLineControls =
                showPanel && (nonHighlighterToolActive || AnyNonHighlighterShapeInSelection());

            if (annotationToolSeparator != null)
            {
                annotationToolSeparator.Visible = showPanel;
            }
            if (lineThicknessLabel != null)
            {
                lineThicknessLabel.Visible = showLineControls;
            }
            if (lineThicknessComboBox != null)
            {
                lineThicknessComboBox.Visible = showLineControls;
            }
            if (highlighterThicknessComboBox != null)
            {
                highlighterThicknessComboBox.Visible = showHighlighterControls;
            }
            if (highlighterOpacityLabel != null)
            {
                highlighterOpacityLabel.Visible = showHighlighterControls;
            }
            if (highlighterOpacityToolStripHost != null)
            {
                highlighterOpacityToolStripHost.Visible = showHighlighterControls;
            }
            if (highlighterOpacityValueLabel != null)
            {
                highlighterOpacityValueLabel.Visible = showHighlighterControls;
            }
            if (annotationColorButton != null)
            {
                annotationColorButton.Visible = showPanel;
            }
            if (arrowSizeLabel != null)
            {
                arrowSizeLabel.Visible = showArrowControls;
            }
            if (arrowSizeComboBox != null)
            {
                arrowSizeComboBox.Visible = showArrowControls;
            }

            if (annotationOptionsToolStrip != null)
            {
                annotationOptionsToolStrip.Visible = showPanel;
                PositionOverlayToolStrips();
            }

            // Reflect the active tool's default color in the swatch (e.g. lemon when the highlighter
            // tool is engaged with nothing selected).
            UpdateAnnotationColorButtonAppearance();
        }

        private void ToggleDrawingTool(DrawingTool tool)
        {
            if (!HasEditableImage)
            {
                return;
            }

            // Deactivate text tool when switching to drawing tools
            if (isTextToolActive)
            {
                FinalizeActiveTextAnnotation();
                isTextToolActive = false;
                UpdateTextToolButtons();
                UpdateTextToolbarVisibility();
            }

            if (activeDrawingTool == tool && !isDrawingAnnotation)
            {
                activeDrawingTool = DrawingTool.None;
            }
            else
            {
                activeDrawingTool = tool;
            }

            CancelAnnotationPreview();
            UpdateDrawingToolButtons();
        }

        private void CancelAnnotationPreview()
        {
            if (isDrawingAnnotation && workingAnnotation != null)
            {
                annotationShapes.Remove(workingAnnotation);
            }

            isDrawingAnnotation = false;
            workingAnnotation = null;
            activeAnnotationHandle = AnnotationHandle.None;
            annotationSnapshotBeforeEdit = null;
            annotationChangedDuringDrag = false;
            highlighterResizeOriginalPoints = null;
            annotationTranslateModeActive = false;
            annotationDraftAnchorPixel = Point.Empty;
            pictureBox1?.Invalidate();
        }

        private enum AnnotationSurface
        {
            Screen,
            Image
        }

        private void DrawAnnotations(Graphics graphics, AnnotationSurface surface)
        {
            if (annotationShapes.Count == 0)
            {
                return;
            }

            var previousSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            try
            {
                foreach (var annotation in annotationShapes)
                {
                    DrawAnnotationShape(graphics, annotation, surface);
                    if (surface == AnnotationSurface.Screen)
                    {
                        DrawAnnotationHandles(graphics, annotation);
                    }
                }
            }
            finally
            {
                graphics.SmoothingMode = previousSmoothing;
            }
        }

        private void DrawAnnotationShape(Graphics graphics, AnnotationShape annotation, AnnotationSurface surface)
        {
            float scale = surface == AnnotationSurface.Screen ? (float)ZoomLevel : 1f;

            if (annotation.Type == AnnotationType.Highlighter)
            {
                DrawHighlighter(graphics, annotation, surface, scale);
                return;
            }

            float strokeWidth = Math.Max(1f, annotation.LineThickness * scale);

            // Selection feedback comes from the corner handles drawn separately in
            // DrawAnnotationHandles, so the stroke uses the user-chosen color regardless of
            // selection state.
            using var pen = new Pen(annotation.Color, strokeWidth)
            {
                Alignment = System.Drawing.Drawing2D.PenAlignment.Center
            };

            if (annotation.Type == AnnotationType.Arrow)
            {
                float arrowScale = scale * annotation.ArrowSize;
                using var arrowCap = new System.Drawing.Drawing2D.AdjustableArrowCap(4f * arrowScale, 6f * arrowScale, true);
                pen.CustomEndCap = arrowCap;
                var start = ConvertAnnotationPoint(annotation.Start, surface);
                var end = ConvertAnnotationPoint(annotation.End, surface);
                graphics.DrawLine(pen, start, end);
            }
            else
            {
                var rect = ConvertAnnotationRectangle(annotation.GetBounds(), surface);
                graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        private void DrawHighlighter(Graphics graphics, AnnotationShape annotation, AnnotationSurface surface, float scale)
        {
            var pts = annotation.Points;
            if (pts == null || pts.Count == 0)
            {
                return;
            }

            float peakOpacity = ClampHighlighterOpacity(annotation.Opacity);
            if (peakOpacity <= 0f)
            {
                return;
            }

            // Vertically-stretched chisel tip: t tall, t/3 wide (t=12 -> 4x12).
            float t = Math.Max(1f, annotation.LineThickness * scale);
            float w = Math.Max(1f, t / 3f);

            // Map path to target coordinates.
            var mapped = new PointF[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                mapped[i] = ConvertAnnotationPoint(pts[i], surface);
            }

            // Buffer covers the path bounds inflated by a full stamp so caps aren't clipped.
            float pad = Math.Max(t, w) / 2f + 2f;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in mapped)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            var bounds = Rectangle.FromLTRB(
                (int)Math.Floor(minX - pad),
                (int)Math.Floor(minY - pad),
                (int)Math.Ceiling(maxX + pad),
                (int)Math.Ceiling(maxY + pad));

            // Cap the buffer to the visible/relevant area to bound memory and cost.
            var clip = surface == AnnotationSurface.Screen
                ? new Rectangle(0, 0, pictureBox1?.ClientSize.Width ?? bounds.Right, pictureBox1?.ClientSize.Height ?? bounds.Bottom)
                : new Rectangle(0, 0, pictureBox1?.Image?.Width ?? bounds.Right, pictureBox1?.Image?.Height ?? bounds.Bottom);
            // Inflate the clip by the pad so caps near the edge still render.
            clip.Inflate((int)Math.Ceiling(pad), (int)Math.Ceiling(pad));
            bounds = Rectangle.Intersect(bounds, clip);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            // Render the whole stroke at FULL opacity into an offscreen buffer, then composite
            // once at the target alpha. Compositing in one pass (rather than stamping translucent
            // ink directly) avoids alpha doubling-up where the stroke overlaps itself.
            using (var buffer = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var bg = Graphics.FromImage(buffer))
                using (var brush = new SolidBrush(annotation.Color))
                {
                    bg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    StampHighlighterPath(bg, brush, mapped, w, t, bounds.Location);
                }

                // Soften the edges with a small 2D blur, then modulate the alpha with value noise
                // (wavy edges, ink pooling, fiber grain, heavier tips) so the ink reads as an organic
                // wash. The noise is sampled in IMAGE space (see imageOrigin/invScale below) so the
                // texture stays locked to the stroke and doesn't reshuffle when the view zooms/pans.
                int blurRadius = Math.Max(2, (int)Math.Round(w * 0.45f));
                var endA = new PointF(mapped[0].X - bounds.X, mapped[0].Y - bounds.Y);
                var endB = new PointF(mapped[mapped.Length - 1].X - bounds.X, mapped[mapped.Length - 1].Y - bounds.Y);

                // Affine map from image px to target px is uniform: target = image*scale + off.
                // Recover off from a known pair, then express each buffer pixel back in image space.
                var off = new PointF(mapped[0].X - pts[0].X * scale, mapped[0].Y - pts[0].Y * scale);
                double invScale = 1.0 / scale;
                double imageOriginX = (bounds.X - off.X) * invScale;
                double imageOriginY = (bounds.Y - off.Y) * invScale;

                PostProcessHighlighterBuffer(buffer, annotation.Color, annotation.Id.GetHashCode(), blurRadius, t,
                    endA, endB, imageOriginX, imageOriginY, invScale);

                using var attributes = new System.Drawing.Imaging.ImageAttributes();
                var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = peakOpacity };
                attributes.SetColorMatrix(matrix);
                graphics.DrawImage(
                    buffer,
                    new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    0, 0, bounds.Width, bounds.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }
        }

        /// <summary>
        /// Draw the path as ONE contiguous vertically-stretched streak. A round-capped/round-joined
        /// pen produces a seamless stroke (no visible per-stamp scalloping); an anisotropic X-scale
        /// on the graphics squashes that round stroke into the chisel-tip profile (t tall, w wide)
        /// while the points are pre-divided by the same factor so the path geometry is unchanged.
        /// </summary>
        private static void StampHighlighterPath(Graphics graphics, Brush brush, PointF[] mapped, float w, float t, Point origin)
        {
            if (mapped.Length == 1)
            {
                var g = graphics.Save();
                graphics.TranslateTransform(-origin.X, -origin.Y);
                StampEllipse(graphics, brush, mapped[0], w, t);
                graphics.Restore(g);
                return;
            }

            float s = w / t; // horizontal squash factor (<1 for the chisel profile)
            var drawPts = new PointF[mapped.Length];
            for (int i = 0; i < mapped.Length; i++)
            {
                drawPts[i] = new PointF(mapped[i].X / s, mapped[i].Y);
            }

            var state = graphics.Save();
            // Device = scale(s,1) then translate(-origin); points are pre-divided by s above so the
            // path lands back at its true position with only the pen width squashed horizontally.
            graphics.TranslateTransform(-origin.X, -origin.Y);
            graphics.ScaleTransform(s, 1f);
            using (var pen = new Pen(brush, t)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round
            })
            {
                graphics.DrawLines(pen, drawPts);
            }
            graphics.Restore(state);
        }

        /// <summary>
        /// In-place 2D blur + value-noise alpha modulation of a 32bpp ARGB highlighter buffer. RGB
        /// is forced to <paramref name="color"/> everywhere so blurring the alpha can't drag edge
        /// pixels toward black. Noise is seeded per-shape AND sampled in image space
        /// (<paramref name="imageOriginX"/> + x·<paramref name="invScale"/>) so the texture is stable
        /// across repaints and stays locked to the stroke when the view zooms or pans.
        /// </summary>
        private static void PostProcessHighlighterBuffer(Bitmap buffer, Color color, int seed, int blurRadius, float thickness,
            PointF endA, PointF endB, double imageOriginX, double imageOriginY, double invScale)
        {
            int width = buffer.Width;
            int height = buffer.Height;
            if (width == 0 || height == 0)
            {
                return;
            }

            var rect = new Rectangle(0, 0, width, height);
            var data = buffer.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int bytes = stride * height;
                var pixels = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, bytes);

                // Pull the alpha channel out (memory order is B,G,R,A on little-endian).
                var srcAlpha = new byte[width * height];
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    int arow = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        srcAlpha[arow + x] = pixels[row + x * 4 + 3];
                    }
                }

                // Pass 1: separable box blur (horizontal then vertical) → blur[]. The vertical pass
                // matters: a horizontal stroke's long top/bottom edges are vertical transitions, so
                // X-only blur would leave them razor-sharp. A slightly smaller Y radius keeps the
                // chisel from going mushy.
                int radiusX = blurRadius;
                int radiusY = Math.Max(1, (int)Math.Round(blurRadius * 0.7));
                var tmp = new byte[width * height];
                var blur = new byte[width * height];
                int windowX = radiusX * 2 + 1;
                for (int y = 0; y < height; y++)
                {
                    int arow = y * width;
                    int sum = 0;
                    for (int k = -radiusX; k <= radiusX; k++)
                    {
                        sum += srcAlpha[arow + Math.Clamp(k, 0, width - 1)];
                    }
                    for (int x = 0; x < width; x++)
                    {
                        tmp[arow + x] = (byte)(sum / windowX);
                        int outX = Math.Clamp(x - radiusX, 0, width - 1);
                        int inX = Math.Clamp(x + radiusX + 1, 0, width - 1);
                        sum += srcAlpha[arow + inX] - srcAlpha[arow + outX];
                    }
                }
                int windowY = radiusY * 2 + 1;
                for (int x = 0; x < width; x++)
                {
                    int sum = 0;
                    for (int k = -radiusY; k <= radiusY; k++)
                    {
                        sum += tmp[Math.Clamp(k, 0, height - 1) * width + x];
                    }
                    for (int y = 0; y < height; y++)
                    {
                        blur[y * width + x] = (byte)(sum / windowY);
                        int outY = Math.Clamp(y - radiusY, 0, height - 1);
                        int inY = Math.Clamp(y + radiusY + 1, 0, height - 1);
                        sum += tmp[inY * width + x] - tmp[outY * width + x];
                    }
                }

                // Pass 2: the "delight" — combine real-marker cues, all driven by the same seeded
                // value noise so the texture is stable across repaints:
                //   * displacement   — sample the band through a low-frequency noise offset so its
                //                       edges wobble organically (the feTurbulence+feDisplacementMap
                //                       trick the realistic CSS markers use), instead of staying a
                //                       parallel-sided bar.
                //   * tip emphasis   — raise opacity toward the two endpoints (felt-tip pooling on
                //                       press/lift) with a noisy, non-circular boundary so the caps
                //                       look dabbed rather than rubber-stamped.
                //   * ink pooling    — reinforce the feathered rim so the wet edge reads heavier.
                //   * fiber streaks  — faint dry lines along the travel axis (the marker's "grain").
                // Displacement amplitude lives in device px (so the wobble scales with the on-screen
                // stroke), but the noise driving it is sampled in image space for zoom stability.
                // Kept gentle — earlier values read like the page was marked up during an earthquake.
                double dispAmp = Math.Max(0.8, thickness * 0.06);
                const double dispCell = 26.0;   // longer wavelength → calmer, rolling edges
                const double blobCell = 12.0;   // ~5–15px opacity blobs (image space)
                const double poolStrength = 0.22;
                const double fiberStrength = 0.16;
                double tipRadius = Math.Max(3.0, thickness * 1.25); // how far in from each end the dab reaches

                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    double imgY = imageOriginY + y * invScale;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = row + x * 4;
                        double imgX = imageOriginX + x * invScale;

                        // Displace the sample position by a smooth 2D noise field (image-space noise,
                        // device-space offset).
                        double dispX = (ValueNoise(imgX / dispCell, imgY / dispCell, seed ^ 0x1b56c4e9) - 0.5) * 2.0 * dispAmp;
                        double dispY = (ValueNoise(imgX / dispCell, imgY / dispCell, seed ^ 0x7f4a7c15) - 0.5) * 2.0 * dispAmp;
                        int sx = Math.Clamp((int)Math.Round(x + dispX), 0, width - 1);
                        int sy = Math.Clamp((int)Math.Round(y + dispY), 0, height - 1);

                        double band = blur[sy * width + sx] / 255.0;
                        double nb;
                        if (band <= 0)
                        {
                            nb = 0;
                        }
                        else
                        {
                            // Mottled opacity (two octaves of blobs) keeps a mostly-opaque floor.
                            double blob = 0.65 * ValueNoise(imgX / blobCell, imgY / blobCell, seed)
                                        + 0.35 * ValueNoise(imgX / (blobCell * 0.45), imgY / (blobCell * 0.45), seed ^ 0x5bd1e995);
                            double mult = 0.80 + 0.20 * blob;

                            // Fiber grain: thin dry lines stretched ALONG the stroke (long cells in
                            // x, fine in y) plus a finer octave, for the streaky look of a felt tip.
                            double fiber = 0.7 * ValueNoise(imgX / 42.0, imgY / 2.1, seed ^ 0x2c1b3a55)
                                         + 0.3 * ValueNoise(imgX / 18.0, imgY / 1.3, seed ^ 0x511fb3a7);
                            mult *= 1.0 - fiberStrength * fiber;

                            // Longitudinal density: body sits at the floor, rising toward the tips.
                            // The dab edge is roughened by noise so it isn't a clean radius.
                            double dEnd = Math.Min(Distance(sx, sy, endA), Distance(sx, sy, endB));
                            double rough = tipRadius * (0.30 * ValueNoise(imgX / 7.0, imgY / 7.0, seed ^ 0x3da1f29b) - 0.10);
                            double endE = 1.0 - Smoothstep(0.0, tipRadius, dEnd + rough);
                            double level = HighlighterBodyLevel + (1.0 - HighlighterBodyLevel) * endE;

                            // Edge factor peaks in the feathered band boundary (band ≈ 0.5).
                            double edge = 4.0 * band * (1.0 - band);
                            nb = band * level * mult + poolStrength * edge;
                            if (nb > 1.0) nb = 1.0;
                        }

                        pixels[idx + 0] = color.B;
                        pixels[idx + 1] = color.G;
                        pixels[idx + 2] = color.R;
                        pixels[idx + 3] = (byte)(nb * 255.0);
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, bytes);
            }
            finally
            {
                buffer.UnlockBits(data);
            }
        }

        private static double Distance(int x, int y, PointF p)
        {
            double dx = x - p.X;
            double dy = y - p.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Hermite smoothstep: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>.</summary>
        private static double Smoothstep(double edge0, double edge1, double x)
        {
            if (edge1 <= edge0) return x < edge0 ? 0.0 : 1.0;
            double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>Smooth (smoothstep-interpolated) value noise in [0,1] on an integer lattice.</summary>
        private static double ValueNoise(double x, double y, int seed)
        {
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            double fx = x - x0;
            double fy = y - y0;

            double v00 = LatticeValue(x0, y0, seed);
            double v10 = LatticeValue(x0 + 1, y0, seed);
            double v01 = LatticeValue(x0, y0 + 1, seed);
            double v11 = LatticeValue(x0 + 1, y0 + 1, seed);

            double sx = fx * fx * (3 - 2 * fx);
            double sy = fy * fy * (3 - 2 * fy);
            double a = v00 + (v10 - v00) * sx;
            double b = v01 + (v11 - v01) * sx;
            return a + (b - a) * sy;
        }

        private static double LatticeValue(int ix, int iy, int seed)
        {
            uint h = (uint)(ix * 374761393 + iy * 668265263 + seed * 362437);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0xFFFFFF;
        }

        private static void StampEllipse(Graphics graphics, Brush brush, PointF center, float w, float t)
        {
            graphics.FillEllipse(brush, center.X - w / 2f, center.Y - t / 2f, w, t);
        }

        private void DrawAnnotationHandles(Graphics graphics, AnnotationShape annotation)
        {
            // Draw hover hitbox for non-selected annotations
            if (annotation == hoveredAnnotation && !annotation.Selected)
            {
                DrawAnnotationHoverHitbox(graphics, annotation);
            }

            if (!annotation.Selected)
            {
                return;
            }

            if (annotation.Type == AnnotationType.Highlighter)
            {
                // Dashed bounding outline makes the (otherwise edge-less) selection legible; the
                // corner handles below it let the stroke be scaled for finer adjustments.
                var outline = PixelToFormCoord(annotation.GetBounds());
                outline.Inflate(4, 4);
                using var selectionPen = new Pen(Color.FromArgb(200, Color.DodgerBlue), 1.5f)
                {
                    DashStyle = System.Drawing.Drawing2D.DashStyle.Dash
                };
                graphics.DrawRectangle(selectionPen, outline);
            }

            const int handleSize = 8;
            var handles = GetAnnotationHandleRects(annotation, handleSize);
            foreach (var kvp in handles)
            {
                var rect = kvp.Value;
                graphics.FillRectangle(Brushes.White, rect);
                graphics.DrawRectangle(Pens.Black, rect);
            }
        }

        private void DrawAnnotationHoverHitbox(Graphics graphics, AnnotationShape annotation)
        {
            using var pen = new Pen(Color.FromArgb(180, Color.Cyan), 2f);
            pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;

            if (annotation.Type == AnnotationType.Arrow)
            {
                // Draw a stroke around the arrow line
                var start = PixelToFormCoordF(annotation.Start);
                var end = PixelToFormCoordF(annotation.End);
                float hitWidth = Math.Max(8f, 4f * (float)ZoomLevel);
                using var hitPen = new Pen(Color.FromArgb(60, Color.Cyan), hitWidth);
                graphics.DrawLine(hitPen, start, end);
                graphics.DrawLine(pen, start, end);
            }
            else if (annotation.Type == AnnotationType.Highlighter)
            {
                // Trace the polyline with a translucent cyan stroke roughly the stroke's width.
                var path = annotation.Points;
                if (path != null && path.Count >= 2)
                {
                    var formPts = new PointF[path.Count];
                    for (int i = 0; i < path.Count; i++)
                    {
                        formPts[i] = PixelToFormCoordF(path[i]);
                    }
                    float hitWidth = Math.Max(8f, annotation.LineThickness * (float)ZoomLevel);
                    using var hitPen = new Pen(Color.FromArgb(60, Color.Cyan), hitWidth)
                    {
                        StartCap = System.Drawing.Drawing2D.LineCap.Round,
                        EndCap = System.Drawing.Drawing2D.LineCap.Round,
                        LineJoin = System.Drawing.Drawing2D.LineJoin.Round
                    };
                    graphics.DrawLines(hitPen, formPts);
                    graphics.DrawLines(pen, formPts);
                }
            }
            else
            {
                // Draw an inflated hitbox outside the rectangle
                var bounds = PixelToFormCoord(annotation.GetBounds());
                const int padding = 4;
                bounds.Inflate(padding, padding);
                
                using var fillBrush = new SolidBrush(Color.FromArgb(30, Color.Cyan));
                graphics.FillRectangle(fillBrush, bounds);
                graphics.DrawRectangle(pen, bounds);
            }
        }

        private PointF ConvertAnnotationPoint(Point pixelPoint, AnnotationSurface surface)
        {
            return surface == AnnotationSurface.Screen ? PixelToFormCoordF(pixelPoint) : new PointF(pixelPoint.X, pixelPoint.Y);
        }

        private RectangleF ConvertAnnotationRectangle(Rectangle rect, AnnotationSurface surface)
        {
            return surface == AnnotationSurface.Screen ? PixelToFormCoordF(rect) : new RectangleF(rect.Location, rect.Size);
        }

        private Dictionary<AnnotationHandle, Rectangle> GetAnnotationHandleRects(AnnotationShape annotation, int size)
        {
            var handles = new Dictionary<AnnotationHandle, Rectangle>();
            int half = size / 2;

            void AddHandle(AnnotationHandle handle, Point pixelPoint)
            {
                var center = PixelToFormCoordF(pixelPoint);
                var rect = new Rectangle((int)Math.Round(center.X) - half, (int)Math.Round(center.Y) - half, size, size);
                handles[handle] = rect;
            }

            if (annotation.Type == AnnotationType.Arrow)
            {
                AddHandle(AnnotationHandle.ArrowStart, annotation.Start);
                AddHandle(AnnotationHandle.ArrowEnd, annotation.End);
            }
            else
            {
                // Rectangle and Highlighter both expose four corner handles; the highlighter scales
                // its whole polyline about the opposite corner (see ScaleHighlighterByHandle).
                var bounds = annotation.GetBounds();
                AddHandle(AnnotationHandle.RectTopLeft, new Point(bounds.Left, bounds.Top));
                AddHandle(AnnotationHandle.RectTopRight, new Point(bounds.Right, bounds.Top));
                AddHandle(AnnotationHandle.RectBottomLeft, new Point(bounds.Left, bounds.Bottom));
                AddHandle(AnnotationHandle.RectBottomRight, new Point(bounds.Right, bounds.Bottom));
            }

            return handles;
        }

        private AnnotationShape? HitTestAnnotation(Point pixelPoint, Point formPoint)
        {
            for (int i = annotationShapes.Count - 1; i >= 0; i--)
            {
                var annotation = annotationShapes[i];
                if (annotation.Type == AnnotationType.Arrow)
                {
                    if (IsPointNearArrow(pixelPoint, annotation))
                    {
                        return annotation;
                    }
                }
                else if (annotation.Type == AnnotationType.Highlighter)
                {
                    if (IsPointNearHighlighter(pixelPoint, annotation))
                    {
                        return annotation;
                    }
                }
                else
                {
                    var bounds = PixelToFormCoord(annotation.GetBounds());
                    if (bounds.Contains(formPoint))
                    {
                        return annotation;
                    }
                }
            }

            return null;
        }

        private bool IsPointNearArrow(Point pixelPoint, AnnotationShape annotation)
        {
            double distance = DistanceFromPointToSegment(pixelPoint, annotation.Start, annotation.End);
            double tolerance = Math.Max(4.0 / (double)ZoomLevel, 3.0);
            return distance <= tolerance;
        }

        private bool IsPointNearHighlighter(Point pixelPoint, AnnotationShape annotation)
        {
            var pts = annotation.Points;
            if (pts == null || pts.Count == 0)
            {
                return false;
            }

            // Half the (vertical) stroke thickness, with a small floor so thin strokes at low
            // zoom stay grabbable.
            double tolerance = Math.Max(annotation.LineThickness / 2.0, 4.0 / (double)ZoomLevel);

            if (pts.Count == 1)
            {
                double dx = pixelPoint.X - pts[0].X;
                double dy = pixelPoint.Y - pts[0].Y;
                return Math.Sqrt(dx * dx + dy * dy) <= tolerance;
            }

            for (int i = 1; i < pts.Count; i++)
            {
                if (DistanceFromPointToSegment(pixelPoint, pts[i - 1], pts[i]) <= tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        private static double DistanceFromPointToSegment(Point point, Point start, Point end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;

            if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
            {
                dx = point.X - start.X;
                dy = point.Y - start.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            double t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));

            double projectionX = start.X + t * dx;
            double projectionY = start.Y + t * dy;
            double diffX = point.X - projectionX;
            double diffY = point.Y - projectionY;
            return Math.Sqrt(diffX * diffX + diffY * diffY);
        }

        /// <summary>
        /// Moving-average smoothing of a freehand polyline (the "2D low-pass"). Endpoints are
        /// preserved exactly; interior points are averaged over a window of <paramref name="window"/>
        /// samples. A window &lt; 2 (or fewer than 3 points) returns a copy unchanged.
        /// </summary>
        internal static List<Point> SmoothPolyline(IReadOnlyList<Point> points, int window)
        {
            int n = points.Count;
            if (n < 3 || window < 2)
            {
                return new List<Point>(points);
            }

            int radius = window / 2;
            var result = new List<Point>(n) { points[0] };
            for (int i = 1; i < n - 1; i++)
            {
                int lo = Math.Max(0, i - radius);
                int hi = Math.Min(n - 1, i + radius);
                double sx = 0, sy = 0;
                for (int j = lo; j <= hi; j++)
                {
                    sx += points[j].X;
                    sy += points[j].Y;
                }
                int count = hi - lo + 1;
                result.Add(new Point((int)Math.Round(sx / count), (int)Math.Round(sy / count)));
            }
            result.Add(points[n - 1]);
            return result;
        }

        /// <summary>
        /// Ramer–Douglas–Peucker decimation: drops samples that lie within <paramref name="epsilon"/>
        /// pixels of the line between retained neighbours, collapsing near-straight runs while
        /// preserving the overall shape. Always keeps both endpoints.
        /// </summary>
        internal static List<Point> SimplifyPolyline(IReadOnlyList<Point> points, double epsilon)
        {
            int n = points.Count;
            if (n < 3)
            {
                return new List<Point>(points);
            }

            var keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;
            SimplifySegment(points, 0, n - 1, epsilon, keep);

            var result = new List<Point>(n);
            for (int i = 0; i < n; i++)
            {
                if (keep[i])
                {
                    result.Add(points[i]);
                }
            }
            return result;
        }

        private static void SimplifySegment(IReadOnlyList<Point> points, int first, int last, double epsilon, bool[] keep)
        {
            if (last <= first + 1)
            {
                return;
            }

            double maxDistance = 0;
            int index = first;
            for (int i = first + 1; i < last; i++)
            {
                double distance = DistanceFromPointToSegment(points[i], points[first], points[last]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    index = i;
                }
            }

            if (maxDistance > epsilon)
            {
                keep[index] = true;
                SimplifySegment(points, first, index, epsilon, keep);
                SimplifySegment(points, index, last, epsilon, keep);
            }
        }

        private AnnotationHandle HitTestAnnotationHandle(Point formPoint)
        {
            if (selectedAnnotation == null)
            {
                return AnnotationHandle.None;
            }

            const int handleSize = 8;
            var handles = GetAnnotationHandleRects(selectedAnnotation, handleSize);
            foreach (var kvp in handles)
            {
                if (kvp.Value.Contains(formPoint))
                {
                    return kvp.Key;
                }
            }

            return AnnotationHandle.None;
        }

        private bool HandleAnnotationMouseDown(Point pixelPoint, Point formPoint)
        {
            if (!HasEditableImage)
            {
                return false;
            }

            // Selected-annotation handle (resize / move) takes priority — the user
            // is actively manipulating the selection.
            var handle = HitTestAnnotationHandle(formPoint);
            if (handle != AnnotationHandle.None)
            {
                annotationSnapshotBeforeEdit = CloneAnnotations();
                activeAnnotationHandle = handle;
                annotationDragOriginPixel = pixelPoint;
                annotationChangedDuringDrag = false;
                // Snapshot the source polyline so the resize scales from the original each move.
                if (selectedAnnotation?.Type == AnnotationType.Highlighter && IsCornerHandle(handle) && selectedAnnotation.Points != null)
                {
                    highlighterResizeOriginalPoints = new List<Point>(selectedAnnotation.Points);
                    highlighterResizeOriginalBounds = selectedAnnotation.GetBounds();
                }
                return true;
            }

            // Object selection wins over tool action: clicking on an existing annotation
            // selects it (and drops the active drawing tool) even when Arrow/Rectangle
            // is engaged. Without this the drawing tool would draw a new shape on top
            // of the click, making it impossible to re-select or move existing shapes
            // without first manually toggling the tool off.
            var hit = HitTestAnnotation(pixelPoint, formPoint);
            if (hit != null)
            {
                if (activeDrawingTool != DrawingTool.None)
                {
                    activeDrawingTool = DrawingTool.None;
                }
                bool addToSelection = IsMultiSelectModifierDown;
                if (addToSelection)
                {
                    SelectAnnotation(hit, add: true);
                }
                else if (!selectedShapes.Contains(hit))
                {
                    // Plain click on something not already in the selection → replace.
                    SelectAnnotation(hit, add: false);
                }
                // else: plain click on an already-selected item → preserve the multi-
                // selection so the user can drag the group as a whole. (Replacing would
                // collapse the selection to this single item and silently break multi-drag.)

                // Only arm a drag when the click ended with the hit shape still in the
                // selection — shift-deselecting (toggling a shape off) should not start
                // a translate gesture.
                if (selectedShapes.Contains(hit))
                {
                    annotationSnapshotBeforeEdit = CloneAnnotations();
                    // Snapshot text annotations too so a multi-drag including texts can
                    // be undone as a single step.
                    if (selectedTexts.Count > 0)
                    {
                        textAnnotationSnapshotBeforeEdit = CloneTextAnnotations();
                    }
                    activeAnnotationHandle = AnnotationHandle.Move;
                    annotationDragOriginPixel = pixelPoint;
                    annotationChangedDuringDrag = false;
                }
                return true;
            }

            if (activeDrawingTool != DrawingTool.None)
            {
                annotationSnapshotBeforeEdit = CloneAnnotations();
                var annotationType = activeDrawingTool switch
                {
                    DrawingTool.Arrow => AnnotationType.Arrow,
                    DrawingTool.Highlighter => AnnotationType.Highlighter,
                    _ => AnnotationType.Rectangle
                };
                var clampedPoint = ClampPointToImage(pixelPoint);
                bool isHighlighter = annotationType == AnnotationType.Highlighter;
                workingAnnotation = new AnnotationShape
                {
                    Type = annotationType,
                    Start = clampedPoint,
                    End = clampedPoint,
                    LineThickness = isHighlighter ? annotationHighlighterThickness : annotationLineThickness,
                    ArrowSize = annotationArrowSize,
                    Color = isHighlighter ? annotationHighlighterColor : annotationColor,
                    Opacity = isHighlighter ? annotationHighlighterOpacity : 1f,
                    Selected = true,
                    Points = isHighlighter ? new List<Point> { clampedPoint } : null
                };
                annotationShapes.Add(workingAnnotation);
                SelectAnnotation(workingAnnotation);
                isDrawingAnnotation = true;
                annotationDraftAnchorPixel = clampedPoint;
                annotationTranslateModeActive = false;
                annotationChangedDuringDrag = false;
                return true;
            }

            if (selectedAnnotation != null)
            {
                SelectAnnotation(null);
            }

            return false;
        }

        private bool HandleAnnotationMouseMove(Point pixelPoint, Point formPoint, MouseButtons buttons)
        {
            if (!HasEditableImage)
            {
                return false;
            }

            if (buttons == MouseButtons.Left)
            {
                if (isDrawingAnnotation && workingAnnotation != null)
                {
                    if (workingAnnotation.Type == AnnotationType.Highlighter)
                    {
                        // Freehand sampling: append the cursor position whenever it has moved
                        // at least 1px from the last sample. The raw samples are decimated and
                        // smoothed on mouse-up (CompleteAnnotationDraft).
                        var sample = ClampPointToImage(pixelPoint);
                        var pts = workingAnnotation.Points!;
                        var last = pts[pts.Count - 1];
                        if (Math.Abs(sample.X - last.X) >= 1 || Math.Abs(sample.Y - last.Y) >= 1)
                        {
                            pts.Add(sample);
                            workingAnnotation.End = sample;
                        }
                    }
                    else if (annotationTranslateModeActive)
                    {
                        ApplyAnnotationTranslation(pixelPoint);
                    }
                    else
                    {
                        var targetPoint = pixelPoint;
                        if (workingAnnotation.Type == AnnotationType.Rectangle && ModifierKeys.HasFlag(Keys.Shift))
                        {
                            targetPoint = ConstrainPointToSquare(annotationDraftAnchorPixel, pixelPoint);
                        }

                        workingAnnotation.End = ClampPointToImage(targetPoint);
                    }

                    annotationChangedDuringDrag = true;
                    pictureBox1.Invalidate();
                    return true;
                }

                if (activeAnnotationHandle != AnnotationHandle.None)
                {
                    ApplyAnnotationHandleDrag(pixelPoint);
                    annotationChangedDuringDrag = true;
                    pictureBox1.Invalidate();
                    return true;
                }
            }
            else if (buttons == MouseButtons.None)
            {
                var handle = HitTestAnnotationHandle(formPoint);
                if (handle != AnnotationHandle.None)
                {
                    SetHoveredAnnotation(null);
                    Cursor = Cursors.Cross;
                    return true;
                }

                var hit = HitTestAnnotation(pixelPoint, formPoint);
                SetHoveredAnnotation(hit);
                if (hit != null)
                {
                    Cursor = Cursors.Hand;
                    return true;
                }
            }

            return false;
        }

        private void SetHoveredAnnotation(AnnotationShape? annotation)
        {
            if (hoveredAnnotation != annotation)
            {
                hoveredAnnotation = annotation;
                pictureBox1?.Invalidate();
            }
        }

        private bool HandleAnnotationMouseUp(MouseButtons button, Point releasePixel)
        {
            if (!HasEditableImage || button != MouseButtons.Left)
            {
                return false;
            }

            if (isDrawingAnnotation)
            {
                CompleteAnnotationDraft(releasePixel);
                return true;
            }

            if (activeAnnotationHandle != AnnotationHandle.None)
            {
                if (annotationChangedDuringDrag)
                {
                    CommitAnnotationUndo();
                }

                activeAnnotationHandle = AnnotationHandle.None;
                annotationSnapshotBeforeEdit = null;
                annotationChangedDuringDrag = false;
                highlighterResizeOriginalPoints = null;
                return true;
            }

            return false;
        }

        private void ApplyAnnotationHandleDrag(Point currentPixel)
        {
            if (selectedAnnotation == null)
            {
                return;
            }

            var clamped = ClampPointToImage(currentPixel);
            var target = selectedAnnotation;

            // Highlighter corner-drag scales the whole polyline about the opposite corner rather
            // than moving a single vertex.
            if (target.Type == AnnotationType.Highlighter && IsCornerHandle(activeAnnotationHandle))
            {
                ScaleHighlighterByHandle(target, activeAnnotationHandle, clamped);
                return;
            }

            switch (activeAnnotationHandle)
            {
                case AnnotationHandle.Move:
                    // Move applies to the WHOLE multi-selection so users can shift-click
                    // several items and drag them together. Resize handles below remain
                    // single-target — multi-resize would require bounding-box scaling that's
                    // out of scope for this slice.
                    var delta = clamped.Subtract(annotationDragOriginPixel);
                    if (delta.X == 0 && delta.Y == 0)
                    {
                        return;
                    }

                    delta = ClampMultiSelectionMoveDelta(delta);

                    if (delta.X == 0 && delta.Y == 0)
                    {
                        annotationDragOriginPixel = clamped;
                        return;
                    }

                    TranslateSelectionBy(delta);
                    annotationDragOriginPixel = clamped;
                    break;
                case AnnotationHandle.ArrowStart:
                    target.Start = clamped;
                    break;
                case AnnotationHandle.ArrowEnd:
                    target.End = clamped;
                    break;
                case AnnotationHandle.RectTopLeft:
                    target.Start = new Point(clamped.X, clamped.Y);
                    break;
                case AnnotationHandle.RectTopRight:
                    target.Start = new Point(target.Start.X, clamped.Y);
                    target.End = new Point(clamped.X, target.End.Y);
                    break;
                case AnnotationHandle.RectBottomLeft:
                    target.Start = new Point(clamped.X, target.Start.Y);
                    target.End = new Point(target.End.X, clamped.Y);
                    break;
                case AnnotationHandle.RectBottomRight:
                    target.End = new Point(clamped.X, clamped.Y);
                    break;
                default:
                    break;
            }

            if (target.Type == AnnotationType.Rectangle)
            {
                NormalizeRectangleAnnotation(target);
            }
        }

        private static bool IsCornerHandle(AnnotationHandle handle) =>
            handle is AnnotationHandle.RectTopLeft or AnnotationHandle.RectTopRight
                   or AnnotationHandle.RectBottomLeft or AnnotationHandle.RectBottomRight;

        /// <summary>
        /// Scale a highlighter's whole polyline about the corner opposite the dragged handle. The
        /// scale is taken from the ORIGINAL geometry captured at drag-start (see
        /// <see cref="highlighterResizeOriginalPoints"/>), so repeated mouse-moves don't accumulate
        /// rounding drift. A signed scale lets the stroke mirror if the cursor crosses the anchor.
        /// </summary>
        private void ScaleHighlighterByHandle(AnnotationShape target, AnnotationHandle handle, Point clamped)
        {
            if (highlighterResizeOriginalPoints == null || target.Points == null)
            {
                return;
            }

            var ob = highlighterResizeOriginalBounds;
            // anchor = fixed opposite corner; dragged = the original position of the grabbed corner.
            Point anchor, dragged;
            switch (handle)
            {
                case AnnotationHandle.RectTopLeft:
                    anchor = new Point(ob.Right, ob.Bottom); dragged = new Point(ob.Left, ob.Top); break;
                case AnnotationHandle.RectTopRight:
                    anchor = new Point(ob.Left, ob.Bottom); dragged = new Point(ob.Right, ob.Top); break;
                case AnnotationHandle.RectBottomLeft:
                    anchor = new Point(ob.Right, ob.Top); dragged = new Point(ob.Left, ob.Bottom); break;
                default: // RectBottomRight
                    anchor = new Point(ob.Left, ob.Top); dragged = new Point(ob.Right, ob.Bottom); break;
            }

            double denomX = dragged.X - anchor.X; // ±original width
            double denomY = dragged.Y - anchor.Y; // ±original height
            if (Math.Abs(denomX) < 1 || Math.Abs(denomY) < 1)
            {
                return; // degenerate (zero-area) original bounds — nothing sensible to scale
            }

            // New offset of the dragged corner from the anchor, floored so the stroke can't collapse.
            int dx = clamped.X - anchor.X;
            int dy = clamped.Y - anchor.Y;
            if (Math.Abs(dx) < 2) dx = dx < 0 ? -2 : 2;
            if (Math.Abs(dy) < 2) dy = dy < 0 ? -2 : 2;

            double sx = dx / denomX;
            double sy = dy / denomY;

            var pts = target.Points;
            for (int i = 0; i < highlighterResizeOriginalPoints.Count; i++)
            {
                var op = highlighterResizeOriginalPoints[i];
                int nx = anchor.X + (int)Math.Round((op.X - anchor.X) * sx);
                int ny = anchor.Y + (int)Math.Round((op.Y - anchor.Y) * sy);
                pts[i] = new Point(nx, ny);
            }
            target.Start = pts[0];
            target.End = pts[pts.Count - 1];
        }

        /// <summary>
        /// Restrict a proposed move-delta so the bounding box of the entire multi-selection
        /// (shapes + texts) stays inside the image. Text positions are treated as single
        /// points — matches the existing single-text-drag clamping behaviour.
        /// </summary>
        private Point ClampMultiSelectionMoveDelta(Point delta)
        {
            if (pictureBox1?.Image == null) return delta;
            if (selectedShapes.Count == 0 && selectedTexts.Count == 0) return delta;

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var shape in selectedShapes)
            {
                // GetBounds covers the full freehand path for highlighters; Start/End for the rest.
                var b = shape.GetBounds();
                minX = Math.Min(minX, b.Left);
                maxX = Math.Max(maxX, b.Right);
                minY = Math.Min(minY, b.Top);
                maxY = Math.Max(maxY, b.Bottom);
            }
            foreach (var text in selectedTexts)
            {
                minX = Math.Min(minX, text.Position.X);
                maxX = Math.Max(maxX, text.Position.X);
                minY = Math.Min(minY, text.Position.Y);
                maxY = Math.Max(maxY, text.Position.Y);
            }

            var bounds = GetImageBounds();
            delta.X = Math.Max(bounds.Left - minX, Math.Min(bounds.Right - maxX, delta.X));
            delta.Y = Math.Max(bounds.Top - minY, Math.Min(bounds.Bottom - maxY, delta.Y));
            return delta;
        }

        /// <summary>
        /// Translate every selected shape and text annotation by the given delta. Caller is
        /// responsible for clamping the delta and recording an undo snapshot before/after.
        /// </summary>
        private void TranslateSelectionBy(Point delta)
        {
            foreach (var shape in selectedShapes)
            {
                shape.Start = shape.Start.Add(delta);
                shape.End = shape.End.Add(delta);
                if (shape.Points != null)
                {
                    for (int i = 0; i < shape.Points.Count; i++)
                    {
                        shape.Points[i] = shape.Points[i].Add(delta);
                    }
                }
            }
            foreach (var text in selectedTexts)
            {
                text.Position = text.Position.Add(delta);
            }
        }

        /// <summary>
        /// Remove every selected shape and text annotation in one combined undo step.
        /// Caller is responsible for ensuring this is the intended action (e.g. Delete
        /// pressed outside text-editing mode with multi-selection present).
        /// </summary>
        private void DeleteMultiSelection()
        {
            bool hadShapes = selectedShapes.Count > 0;
            bool hadTexts = selectedTexts.Count > 0;
            if (!hadShapes && !hadTexts) return;

            var shapesBefore = hadShapes ? CloneAnnotations() : null;
            var textsBefore = hadTexts ? CloneTextAnnotations() : null;

            foreach (var shape in selectedShapes.ToList())
            {
                annotationShapes.Remove(shape);
            }
            foreach (var text in selectedTexts.ToList())
            {
                textAnnotations.Remove(text);
            }

            SelectAnnotation(null);
            SelectTextAnnotation(null);
            activeTextAnnotation = null;

            var shapesAfter = hadShapes ? CloneAnnotations() : null;
            var textsAfter = hadTexts ? CloneTextAnnotations() : null;
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false,
                shapesBefore, shapesAfter,
                textsBefore, textsAfter);
            pictureBox1?.Invalidate();
        }

        private void CompleteAnnotationDraft(Point pixelPoint)
        {
            if (!isDrawingAnnotation || workingAnnotation == null)
            {
                return;
            }

            if (workingAnnotation.Type == AnnotationType.Highlighter)
            {
                var pts = workingAnnotation.Points!;
                var clamped = ClampPointToImage(pixelPoint);
                var last = pts[pts.Count - 1];
                if (clamped.X != last.X || clamped.Y != last.Y)
                {
                    pts.Add(clamped);
                }
                // Round jitter (moving average), then drop redundant samples (RDP).
                var processed = SimplifyPolyline(SmoothPolyline(pts, 3), 1.5);
                workingAnnotation.Points = processed;
                workingAnnotation.Start = processed[0];
                workingAnnotation.End = processed[processed.Count - 1];
            }
            else
            {
                workingAnnotation.End = ClampPointToImage(pixelPoint);
                if (workingAnnotation.Type == AnnotationType.Rectangle)
                {
                    NormalizeRectangleAnnotation(workingAnnotation);
                }
            }

            if (!workingAnnotation.IsValid())
            {
                annotationShapes.Remove(workingAnnotation);
            }
            else
            {
                SelectAnnotation(workingAnnotation);
                CommitAnnotationUndo();
            }

            isDrawingAnnotation = false;
            workingAnnotation = null;
            annotationSnapshotBeforeEdit = null;
            annotationChangedDuringDrag = false;
            annotationTranslateModeActive = false;
            annotationDraftAnchorPixel = Point.Empty;
            pictureBox1.Invalidate();
        }

        private void NormalizeRectangleAnnotation(AnnotationShape annotation)
        {
            if (annotation.Type != AnnotationType.Rectangle)
            {
                return;
            }

            var rect = RectangleExt.fromPoints(annotation.Start, annotation.End);
            annotation.Start = new Point(rect.Left, rect.Top);
            annotation.End = new Point(rect.Right, rect.Bottom);
        }

        private bool BeginAnnotationTranslation()
        {
            if (!isDrawingAnnotation || workingAnnotation == null || annotationTranslateModeActive)
            {
                return false;
            }

            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                return false;
            }

            annotationTranslateModeActive = true;
            annotationTranslationOriginPixel = GetCursorPixelPosition();
            annotationTranslationStartSnapshot = workingAnnotation.Start;
            annotationTranslationEndSnapshot = workingAnnotation.End;
            annotationTranslationAnchorSnapshot = annotationDraftAnchorPixel;
            return true;
        }

        private void ApplyAnnotationTranslation(Point currentPixel)
        {
            if (!annotationTranslateModeActive || workingAnnotation == null)
            {
                return;
            }

            var delta = currentPixel.Subtract(annotationTranslationOriginPixel);
            if (delta.X == 0 && delta.Y == 0)
            {
                return;
            }

            var candidateStart = annotationTranslationStartSnapshot.Add(delta);
            var candidateEnd = annotationTranslationEndSnapshot.Add(delta);
            var candidateAnchor = annotationTranslationAnchorSnapshot.Add(delta);

            var clampOffset = CalculateShapeClampOffset(candidateStart, candidateEnd);
            if (clampOffset != Point.Empty)
            {
                candidateStart = candidateStart.Add(clampOffset);
                candidateEnd = candidateEnd.Add(clampOffset);
                candidateAnchor = candidateAnchor.Add(clampOffset);
            }

            workingAnnotation.Start = candidateStart;
            workingAnnotation.End = candidateEnd;
            annotationDraftAnchorPixel = candidateAnchor;

        }

        private Point CalculateShapeClampOffset(Point start, Point end)
        {
            if (pictureBox1?.Image == null)
            {
                return Point.Empty;
            }

            var bounds = GetImageBounds();
            int minX = Math.Min(start.X, end.X);
            int maxX = Math.Max(start.X, end.X);
            int minY = Math.Min(start.Y, end.Y);
            int maxY = Math.Max(start.Y, end.Y);

            int offsetX = 0;
            if (minX < bounds.Left)
            {
                offsetX = bounds.Left - minX;
            }
            else if (maxX > bounds.Right)
            {
                offsetX = bounds.Right - maxX;
            }

            int offsetY = 0;
            if (minY < bounds.Top)
            {
                offsetY = bounds.Top - minY;
            }
            else if (maxY > bounds.Bottom)
            {
                offsetY = bounds.Bottom - maxY;
            }

            if (offsetX == 0 && offsetY == 0)
            {
                return Point.Empty;
            }

            return new Point(offsetX, offsetY);
        }

        private static Point ConstrainPointToSquare(Point anchor, Point current)
        {
            int dx = current.X - anchor.X;
            int dy = current.Y - anchor.Y;

            if (dx == 0 && dy == 0)
            {
                return current;
            }

            int absDx = Math.Abs(dx);
            int absDy = Math.Abs(dy);
            int size = Math.Max(absDx, absDy);

            int signX = dx == 0 ? (dy >= 0 ? 1 : -1) : Math.Sign(dx);
            int signY = dy == 0 ? (dx >= 0 ? 1 : -1) : Math.Sign(dy);

            return new Point(anchor.X + size * signX, anchor.Y + size * signY);
        }

        private Point GetCursorPixelPosition()
        {
            if (pictureBox1 == null)
            {
                return Point.Empty;
            }

            var cursorClient = pictureBox1.PointToClient(Cursor.Position);
            return FormCoordToPixel(cursorClient);
        }

        private Point ClampPointToImage(Point point)
        {
            if (pictureBox1.Image == null)
            {
                return point;
            }

            var bounds = GetImageBounds();
            int clampedX = Math.Max(bounds.Left, Math.Min(bounds.Right, point.X));
            int clampedY = Math.Max(bounds.Top, Math.Min(bounds.Bottom, point.Y));
            return new Point(clampedX, clampedY);
        }

        private static Point ClampPointToBounds(Point point, Rectangle bounds)
        {
            int clampedX = Math.Max(bounds.Left, Math.Min(bounds.Right, point.X));
            int clampedY = Math.Max(bounds.Top, Math.Min(bounds.Bottom, point.Y));
            return new Point(clampedX, clampedY);
        }

        private void CommitAnnotationUndo()
        {
            if (annotationSnapshotBeforeEdit == null && textAnnotationSnapshotBeforeEdit == null)
            {
                return;
            }

            // Combine shape + text diffs into a single undo step so a multi-selection drag
            // is undone with one Ctrl+Z. Each half is captured only when we actually staged
            // a snapshot on the corresponding type at drag-start.
            var shapesAfter = annotationSnapshotBeforeEdit != null ? CloneAnnotations() : null;
            var textsAfter = textAnnotationSnapshotBeforeEdit != null ? CloneTextAnnotations() : null;
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false,
                annotationSnapshotBeforeEdit, shapesAfter,
                textAnnotationSnapshotBeforeEdit, textsAfter);
            annotationSnapshotBeforeEdit = null;
            textAnnotationSnapshotBeforeEdit = null;
        }

        private void ApplyAnnotationState(List<AnnotationShape>? source)
        {
            if (source == null)
            {
                return;
            }

            annotationShapes.Clear();
            foreach (var annotation in source)
            {
                annotationShapes.Add(annotation.Clone());
            }

            SyncSelectedAnnotation();
        }

        private void ApplyCropToAnnotations(Point cropOrigin, Size newSize)
        {
            if (annotationShapes.Count == 0)
            {
                return;
            }

            var newBounds = new Rectangle(Point.Empty, newSize);
            var updated = new List<AnnotationShape>();

            foreach (var shape in annotationShapes)
            {
                var clone = shape.Clone();
                clone.Start = clone.Start.Subtract(cropOrigin);
                clone.End = clone.End.Subtract(cropOrigin);

                if (clone.Type == AnnotationType.Highlighter)
                {
                    var path = clone.Points;
                    if (path == null || path.Count < 2)
                    {
                        continue;
                    }
                    for (int i = 0; i < path.Count; i++)
                    {
                        path[i] = ClampPointToBounds(path[i].Subtract(cropOrigin), newBounds);
                    }
                    // Drop strokes that collapsed to (effectively) a single point after clamping.
                    if (!clone.IsValid())
                    {
                        continue;
                    }
                    clone.Start = path[0];
                    clone.End = path[path.Count - 1];
                    updated.Add(clone);
                    continue;
                }

                if (clone.Type == AnnotationType.Rectangle)
                {
                    var rect = RectangleExt.fromPoints(clone.Start, clone.End);
                    var intersection = Rectangle.Intersect(newBounds, rect);
                    if (intersection.Width < 2 || intersection.Height < 2)
                    {
                        continue;
                    }

                    clone.Start = new Point(intersection.Left, intersection.Top);
                    clone.End = new Point(intersection.Right, intersection.Bottom);
                }
                else
                {
                    var rect = RectangleExt.fromPoints(clone.Start, clone.End);
                    if (!rect.IntersectsWith(newBounds))
                    {
                        continue;
                    }

                    clone.Start = ClampPointToBounds(clone.Start, newBounds);
                    clone.End = ClampPointToBounds(clone.End, newBounds);
                }

                updated.Add(clone);
            }

            annotationShapes.Clear();
            annotationShapes.AddRange(updated);
            SyncSelectedAnnotation();
        }

        private void InitializeAnnotationToolbar()
        {
            if (lineThicknessComboBox != null)
            {
                lineThicknessComboBox.Items.AddRange(new object[] { "1", "2", "3", "4", "5", "6", "8", "10" });
                int defaultIndex = lineThicknessComboBox.Items.IndexOf(annotationLineThickness.ToString());
                lineThicknessComboBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 1; // Default to "2"
            }

            if (arrowSizeComboBox != null)
            {
                arrowSizeComboBox.Items.AddRange(new object[] { "0.5", "0.75", "1", "1.25", "1.5", "2", "2.5", "3" });
                int defaultIndex = arrowSizeComboBox.Items.IndexOf(annotationArrowSize.ToString());
                arrowSizeComboBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 2; // Default to "1"
            }

            if (highlighterThicknessComboBox != null)
            {
                highlighterThicknessComboBox.Items.AddRange(new object[] { "8", "12", "16", "20", "24" });
                int defaultIndex = highlighterThicknessComboBox.Items.IndexOf(annotationHighlighterThickness.ToString());
                highlighterThicknessComboBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 1; // Default to "12"
            }

            if (highlighterOpacityTrackBar != null)
            {
                int percent = HighlighterOpacityToPercent(annotationHighlighterOpacity);
                highlighterOpacityTrackBar.Value = Math.Clamp(percent, highlighterOpacityTrackBar.Minimum, highlighterOpacityTrackBar.Maximum);
            }
            UpdateHighlighterOpacityValueLabel(HighlighterOpacityToPercent(annotationHighlighterOpacity));

            UpdateAnnotationColorButtonAppearance();
        }

        private void UpdateHighlighterOpacityValueLabel(int? percent, bool mixed = false)
        {
            if (highlighterOpacityValueLabel == null)
            {
                return;
            }

            highlighterOpacityValueLabel.Text = mixed
                ? "Mixed"
                : $"{Math.Clamp(percent ?? 0, 0, 100)}%";
        }

        private void annotationColorButton_Click(object? sender, EventArgs e)
        {
            // Choose the dialog's starting color: use the unanimous selection color when
            // all selected items agree, fall back to the primary's color or tool default.
            using var dialog = new ColorDialog
            {
                Color = GetRepresentativeSelectionColor() ?? ActiveToolDefaultColor,
                FullOpen = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            ActiveToolDefaultColor = dialog.Color;
            ApplyColorToSelection(dialog.Color);
            UpdateAnnotationColorButtonAppearance();
        }

        /// <summary>
        /// Push one undo step that applies the colour to every selected shape and text
        /// annotation. Items in the selection that don't have an applicable colour slot
        /// are silently skipped.
        /// </summary>
        private void ApplyColorToSelection(Color color)
        {
            bool anyShapeChange = selectedShapes.Count > 0;
            bool anyTextChange = selectedTexts.Count > 0;
            if (!anyShapeChange && !anyTextChange)
            {
                return;
            }

            List<AnnotationShape>? shapesBefore = null;
            List<TextAnnotation>? textsBefore = null;
            if (anyShapeChange) shapesBefore = CloneAnnotations();
            if (anyTextChange) textsBefore = CloneTextAnnotations();

            foreach (var shape in selectedShapes)
            {
                shape.Color = color;
            }
            foreach (var text in selectedTexts)
            {
                text.TextColor = color;
            }

            // Push as a single combined undo step so a single Ctrl+Z reverts the whole
            // multi-target colour change.
            var shapesAfter = anyShapeChange ? CloneAnnotations() : null;
            var textsAfter = anyTextChange ? CloneTextAnnotations() : null;
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false,
                shapesBefore, shapesAfter, textsBefore, textsAfter);
            pictureBox1?.Invalidate();
        }

        /// <summary>
        /// Returns the colour shared by every selected item (shapes + texts), or null
        /// when the selection contains a mix of colours or is empty.
        /// </summary>
        private Color? GetRepresentativeSelectionColor()
        {
            Color? candidate = null;
            foreach (var shape in selectedShapes)
            {
                if (candidate == null) candidate = shape.Color;
                else if (candidate.Value.ToArgb() != shape.Color.ToArgb()) return null;
            }
            foreach (var text in selectedTexts)
            {
                if (candidate == null) candidate = text.TextColor;
                else if (candidate.Value.ToArgb() != text.TextColor.ToArgb()) return null;
            }
            return candidate;
        }

        private void UpdateAnnotationColorButtonAppearance()
        {
            if (annotationColorButton == null)
            {
                return;
            }

            int selectionCount = selectedShapes.Count + selectedTexts.Count;
            var representative = GetRepresentativeSelectionColor();

            if (selectionCount > 1 && representative == null)
            {
                // Mixed colours across the multi-selection — neutral swatch + "Mixed".
                annotationColorButton.BackColor = SystemColors.Control;
                annotationColorButton.ForeColor = SystemColors.ControlText;
                annotationColorButton.Text = "Mixed";
                return;
            }

            var swatch = representative ?? ActiveToolDefaultColor;
            annotationColorButton.BackColor = swatch;
            annotationColorButton.ForeColor = GetContrastColor(swatch);
            annotationColorButton.Text = "Color";
        }

        private void lineThicknessComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (isSyncingAnnotationToolbarControls)
            {
                return;
            }
            if (lineThicknessComboBox?.SelectedItem is string thicknessStr &&
                float.TryParse(thicknessStr, out float thickness) && thickness > 0)
            {
                annotationLineThickness = thickness;
                ApplyLineThicknessToSelection(thickness);
            }
        }

        private void highlighterThicknessComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (isSyncingAnnotationToolbarControls)
            {
                return;
            }
            if (highlighterThicknessComboBox?.SelectedItem is string thicknessStr &&
                float.TryParse(thicknessStr, out float thickness) && thickness > 0)
            {
                annotationHighlighterThickness = thickness;
                ApplyHighlighterThicknessToSelection(thickness);
            }
        }

        private void highlighterOpacityTrackBar_ValueChanged(object? sender, EventArgs e)
        {
            if (highlighterOpacityTrackBar == null)
            {
                return;
            }

            int percent = highlighterOpacityTrackBar.Value;
            UpdateHighlighterOpacityValueLabel(percent);

            if (isSyncingAnnotationToolbarControls)
            {
                return;
            }

            annotationHighlighterOpacity = HighlighterOpacityFromPercent(percent);
            ApplyHighlighterOpacityToSelection(annotationHighlighterOpacity);
        }

        private void arrowSizeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (isSyncingAnnotationToolbarControls)
            {
                return;
            }
            if (arrowSizeComboBox?.SelectedItem is string sizeStr &&
                float.TryParse(sizeStr, out float size) && size > 0)
            {
                annotationArrowSize = size;
                ApplyArrowSizeToSelection(size);
            }
        }

        /// <summary>
        /// Write the line thickness to every selected shape (text annotations don't have
        /// a line-width slot and are silently skipped). Shapes that already match the
        /// requested value are excluded so the undo step records a real change.
        /// </summary>
        private void ApplyLineThicknessToSelection(float thickness)
        {
            // Highlighters have their own thickness combo (8–24); the line combo governs
            // arrows and rectangles only.
            var targets = selectedShapes
                .Where(s => s.Type != AnnotationType.Highlighter && s.LineThickness != thickness)
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var before = CloneAnnotations();
            foreach (var shape in targets)
            {
                shape.LineThickness = thickness;
            }
            var after = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false, before, after);
            pictureBox1?.Invalidate();
        }

        /// <summary>
        /// Write the thickness to every selected highlighter shape (other types are silently
        /// skipped). Highlighters that already match the requested value are excluded so the
        /// undo step records a real change.
        /// </summary>
        private void ApplyHighlighterThicknessToSelection(float thickness)
        {
            var targets = selectedShapes
                .Where(s => s.Type == AnnotationType.Highlighter && s.LineThickness != thickness)
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var before = CloneAnnotations();
            foreach (var shape in targets)
            {
                shape.LineThickness = thickness;
            }
            var after = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false, before, after);
            pictureBox1?.Invalidate();
        }

        private void ApplyHighlighterOpacityToSelection(float opacity)
        {
            float clampedOpacity = ClampHighlighterOpacity(opacity);
            var targets = selectedShapes
                .Where(s => s.Type == AnnotationType.Highlighter && Math.Abs(s.Opacity - clampedOpacity) > 0.0001f)
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var before = CloneAnnotations();
            foreach (var shape in targets)
            {
                shape.Opacity = clampedOpacity;
            }
            var after = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false, before, after);
            pictureBox1?.Invalidate();
        }

        /// <summary>
        /// Write the arrow head size to every selected arrow shape (rects and texts are
        /// silently skipped). Arrows that already match the requested value are excluded.
        /// </summary>
        private void ApplyArrowSizeToSelection(float size)
        {
            var targets = selectedShapes
                .Where(s => s.Type == AnnotationType.Arrow && s.ArrowSize != size)
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var before = CloneAnnotations();
            foreach (var shape in targets)
            {
                shape.ArrowSize = size;
            }
            var after = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false, before, after);
            pictureBox1?.Invalidate();
        }

        private void UpdateAnnotationToolbarFromSelection()
        {
            // Programmatic pushes — the handlers must not interpret these as user edits
            // and write them back into the selection.
            isSyncingAnnotationToolbarControls = true;
            try
            {
                if (lineThicknessComboBox != null && selectedShapes.Count > 0)
                {
                    float? unanimous = GetUnanimousLineThickness();
                    if (unanimous.HasValue)
                    {
                        int index = lineThicknessComboBox.Items.IndexOf(unanimous.Value.ToString());
                        lineThicknessComboBox.SelectedIndex = index >= 0 ? index : -1;
                    }
                    else
                    {
                        // Mixed thicknesses across the selection — show the combobox blank
                        // (no value) as the indeterminate state. Picking a value from the
                        // dropdown will apply it to every selected shape.
                        lineThicknessComboBox.SelectedIndex = -1;
                    }
                }

                if (highlighterThicknessComboBox != null && AnyHighlighterInSelection())
                {
                    float? unanimous = GetUnanimousHighlighterThickness();
                    if (unanimous.HasValue)
                    {
                        int index = highlighterThicknessComboBox.Items.IndexOf(unanimous.Value.ToString());
                        highlighterThicknessComboBox.SelectedIndex = index >= 0 ? index : -1;
                    }
                    else
                    {
                        highlighterThicknessComboBox.SelectedIndex = -1;
                    }
                }

                if (highlighterOpacityTrackBar != null)
                {
                    if (AnyHighlighterInSelection())
                    {
                        float? unanimous = GetUnanimousHighlighterOpacity();
                        if (unanimous.HasValue)
                        {
                            int percent = HighlighterOpacityToPercent(unanimous.Value);
                            highlighterOpacityTrackBar.Value = Math.Clamp(percent, highlighterOpacityTrackBar.Minimum, highlighterOpacityTrackBar.Maximum);
                            UpdateHighlighterOpacityValueLabel(percent);
                        }
                        else
                        {
                            int percent = HighlighterOpacityToPercent(annotationHighlighterOpacity);
                            highlighterOpacityTrackBar.Value = Math.Clamp(percent, highlighterOpacityTrackBar.Minimum, highlighterOpacityTrackBar.Maximum);
                            UpdateHighlighterOpacityValueLabel(null, mixed: true);
                        }
                    }
                    else if (activeDrawingTool == DrawingTool.Highlighter)
                    {
                        int percent = HighlighterOpacityToPercent(annotationHighlighterOpacity);
                        highlighterOpacityTrackBar.Value = Math.Clamp(percent, highlighterOpacityTrackBar.Minimum, highlighterOpacityTrackBar.Maximum);
                        UpdateHighlighterOpacityValueLabel(percent);
                    }
                }

                if (arrowSizeComboBox != null && AnyArrowInSelection())
                {
                    float? unanimous = GetUnanimousArrowSize();
                    if (unanimous.HasValue)
                    {
                        int index = arrowSizeComboBox.Items.IndexOf(unanimous.Value.ToString());
                        arrowSizeComboBox.SelectedIndex = index >= 0 ? index : -1;
                    }
                    else
                    {
                        arrowSizeComboBox.SelectedIndex = -1;
                    }
                }
            }
            finally
            {
                isSyncingAnnotationToolbarControls = false;
            }

            UpdateAnnotationColorButtonAppearance();
        }

        private float? GetUnanimousLineThickness()
        {
            float? candidate = null;
            foreach (var shape in selectedShapes)
            {
                if (shape.Type == AnnotationType.Highlighter) continue;
                if (candidate == null) candidate = shape.LineThickness;
                else if (candidate.Value != shape.LineThickness) return null;
            }
            return candidate;
        }

        private float? GetUnanimousHighlighterThickness()
        {
            float? candidate = null;
            foreach (var shape in selectedShapes)
            {
                if (shape.Type != AnnotationType.Highlighter) continue;
                if (candidate == null) candidate = shape.LineThickness;
                else if (candidate.Value != shape.LineThickness) return null;
            }
            return candidate;
        }

        private float? GetUnanimousHighlighterOpacity()
        {
            float? candidate = null;
            foreach (var shape in selectedShapes)
            {
                if (shape.Type != AnnotationType.Highlighter) continue;
                if (candidate == null) candidate = shape.Opacity;
                else if (Math.Abs(candidate.Value - shape.Opacity) > 0.0001f) return null;
            }
            return candidate;
        }

        private bool AnyHighlighterInSelection() =>
            selectedShapes.Any(s => s.Type == AnnotationType.Highlighter);

        private bool AnyNonHighlighterShapeInSelection() =>
            selectedShapes.Any(s => s.Type != AnnotationType.Highlighter);

        private float? GetUnanimousArrowSize()
        {
            float? candidate = null;
            foreach (var shape in selectedShapes)
            {
                if (shape.Type != AnnotationType.Arrow) continue;
                if (candidate == null) candidate = shape.ArrowSize;
                else if (candidate.Value != shape.ArrowSize) return null;
            }
            return candidate;
        }

        private bool AnyArrowInSelection() =>
            selectedShapes.Any(s => s.Type == AnnotationType.Arrow);

    }
}
