using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using screenzap.Components.Shared;
using screenzap.lib;

namespace screenzap
{
    public partial class ImageEditor
    {
        private Point MouseInPixel;
        private Point MouseOutPixel;
        private Point rubberBandStartClient;
        private bool isDrawingRubberBand;
        private bool isMovingSelection;
        private Point MoveInPixel;
        private bool isCtrlStampingSelection;
        private bool isAltCloningSelection;
        private bool selectionStampApplied;
        private Bitmap? selectionStampSource;
        private Bitmap? selectionStampBeforeImage;
        private Point selectionStampLastLocation;
        private Rectangle selectionStampSelectionBefore;
        private Bitmap? selectionCloneSource;
        private Bitmap? selectionCloneBeforeImage;
        private Rectangle selectionCloneSelectionBefore;
        private bool selectionCloneApplied;
        private bool isCtrlResizingSelection;
        private Bitmap? selectionResizeSource;
        private Bitmap? selectionResizeBeforeImage;
        private Rectangle selectionResizeSelectionBefore;

        private Rectangle _selection;
        private SelectionMetrics _selectionMetrics = SelectionMetrics.Empty;
        private Rectangle Selection
        {
            get => _selection;
            set
            {
                _selection = value;
                UpdateSelectionMetrics();
                UpdateStatusBar();
            }
        }

        internal SelectionMetrics SelectionDiagnostics => _selectionMetrics;

        internal void SetSelectionForDiagnostics(Rectangle selection)
        {
            Selection = selection;
        }

        private void UpdateSelectionMetrics()
        {
            var bounds = GetImageBounds();
            _selectionMetrics = SelectionMetrics.From(_selection, bounds);
        }
        private Rectangle SelectionGrabOrigin;

        private ResizeMode rzMode;
        private bool isPanning;

        private void DrawMarchingAntsRectangle(Graphics graphics, Rectangle rect, float lineWidth)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            using (var whitePen = new Pen(Color.White, lineWidth))
            using (var blackPen = new Pen(Color.Black, lineWidth))
            {
                whitePen.DashStyle = DashStyle.Custom;
                blackPen.DashStyle = DashStyle.Custom;
                whitePen.DashPattern = new[] { 2f, 2f };
                blackPen.DashPattern = new[] { 2f, 2f };
                whitePen.DashOffset = 0f;
                blackPen.DashOffset = 2f;

                graphics.DrawRectangle(whitePen, rect);
                graphics.DrawRectangle(blackPen, rect);
            }
        }

        private static readonly Dictionary<ResizeMode, Cursor> ResizeCursors = new Dictionary<ResizeMode, Cursor>
        {
            { ResizeMode.ResizeTL, Cursors.SizeNWSE},
            { ResizeMode.ResizeT, Cursors.SizeNS},
            { ResizeMode.ResizeTR, Cursors.SizeNESW},

            { ResizeMode.ResizeL, Cursors.SizeWE},
            { ResizeMode.ResizeR, Cursors.SizeWE},

            { ResizeMode.ResizeBL, Cursors.SizeNESW},
            { ResizeMode.ResizeB, Cursors.SizeNS},
            { ResizeMode.ResizeBR, Cursors.SizeNWSE},

            { ResizeMode.Move, Cursors.SizeAll }
        };

        private readonly int rzTolerance = 5;

        private decimal _zoomlevel = 1;
        private decimal ZoomLevel
        {
            get => pictureBox1?.ZoomLevel ?? _zoomlevel;
            set
            {
                _zoomlevel = value;
                if (pictureBox1 != null)
                {
                    pictureBox1.ZoomLevel = value;
                    _zoomlevel = pictureBox1.ZoomLevel;
                }
            }
        }

        private readonly decimal[] ZoomLevels =
        {
            0.025m, 0.05m, 0.1m,
            0.2m, 0.3m, 0.4m, 0.5m,
            0.75m, 1m, 1.25m, 1.5m,
            2m, 3m, 4m, 5m, 6m, 7m, 8m,
            12m, 16m, 24m, 32m, 48m, 64m
        };

        private decimal FindZoomIn(decimal level)
        {
            var levels = ZoomLevels.OrderBy(n => n).Where(n => n > level);
            return levels.Count() > 0 ? levels.ElementAt(0) : level;
        }

        private decimal FindZoomOut(decimal level)
        {
            var levels = ZoomLevels.OrderByDescending(n => n).Where(n => n < level);
            return levels.Count() > 0 ? levels.ElementAt(0) : level;
        }

        private void ClearSelection()
        {
            Selection = new Rectangle(0, 0, 0, 0);
            UpdateCommandUI();
        }

        private void SetSelectionEdge(Point pixel)
        {
            if (rzMode == ResizeMode.ResizeTL)
                Selection = RectangleExt.fromPoints(pixel, new Point(Selection.Right, Selection.Bottom));

            if (rzMode == ResizeMode.ResizeT)
                Selection = RectangleExt.fromPoints(new Point(Selection.Left, pixel.Y), new Point(Selection.Right, Selection.Bottom));

            if (rzMode == ResizeMode.ResizeTR)
                Selection = RectangleExt.fromPoints(
                    new Point(Selection.Left, pixel.Y),
                    new Point(pixel.X, Selection.Bottom)
                );

            if (rzMode == ResizeMode.ResizeL)
                Selection = RectangleExt.fromPoints(
                    new Point(pixel.X, Selection.Y),
                    new Point(Selection.Right, Selection.Bottom)
                );

            if (rzMode == ResizeMode.ResizeR)
                Selection = RectangleExt.fromPoints(
                    new Point(Selection.Left, Selection.Top),
                    new Point(pixel.X, Selection.Bottom)
                );

            if (rzMode == ResizeMode.ResizeBL)
                Selection = RectangleExt.fromPoints(
                    new Point(pixel.X, Selection.Top),
                    new Point(Selection.Right, pixel.Y)
                );

            if (rzMode == ResizeMode.ResizeB)
                Selection = RectangleExt.fromPoints(
                    new Point(Selection.Left, Selection.Top),
                    new Point(Selection.Right, pixel.Y)
                );

            if (rzMode == ResizeMode.ResizeBR)
                Selection = RectangleExt.fromPoints(
                    new Point(Selection.Left, Selection.Top),
                    pixel
                );

            if (rzMode == ResizeMode.Move)
            {
                var Delta = MouseInPixel.Subtract(pixel);
                // The marquee moves freely, past the canvas edge if taken there — pixel
                // operations (stamp/clone/copy/crop/...) all clip to the image themselves.
                Selection = new Rectangle(
                    new Point(SelectionGrabOrigin.X - Delta.X, SelectionGrabOrigin.Y - Delta.Y),
                    Selection.Size);
                return;
            }

            Selection = ClampToImage(Selection);
        }

        private bool isCtrlHeld_TestOverride;
        private bool isAltHeld_TestOverride;

        private bool IsCtrlModifierDown() => isCtrlHeld_TestOverride || (ModifierKeys & Keys.Control) == Keys.Control;
        private bool IsAltModifierDown() => isAltHeld_TestOverride || (ModifierKeys & Keys.Alt) == Keys.Alt;
        private bool IsShiftModifierDown() => isShiftHeld_TestOverride || (ModifierKeys & Keys.Shift) == Keys.Shift;

        /// <summary>
        /// A stamp/clone gesture can only pick up on-canvas pixels. When it starts on a marquee
        /// that rests partially off-canvas (left there by a previous gesture), snap the marquee
        /// to the captured region so the stamped content stays aligned with the ants.
        /// </summary>
        private void ReanchorSelectionToCapturedRegion(Rectangle capturedRegion)
        {
            if (Selection != capturedRegion)
            {
                Selection = capturedRegion;
                SelectionGrabOrigin = capturedRegion;
            }
        }

        private Point ApplyCloneStampAxisLock(Point cursorPixel)
        {
            if (rzMode != ResizeMode.Move || !IsShiftModifierDown() || (!isCtrlStampingSelection && !isAltCloningSelection))
            {
                return cursorPixel;
            }

            var dx = cursorPixel.X - MouseInPixel.X;
            var dy = cursorPixel.Y - MouseInPixel.Y;
            if (dx == 0 && dy == 0)
            {
                return cursorPixel;
            }

            return Math.Abs(dx) >= Math.Abs(dy)
                ? new Point(cursorPixel.X, MouseInPixel.Y)
                : new Point(MouseInPixel.X, cursorPixel.Y);
        }

        private void BeginSelectionStampGesture()
        {
            if (isCtrlStampingSelection || !HasEditableImage || Selection.IsEmpty || pictureBox1.Image == null)
            {
                return;
            }

            var sourceRegion = ClampToImage(Selection);
            if (sourceRegion.Width <= 0 || sourceRegion.Height <= 0)
            {
                return;
            }

            selectionStampSource = CaptureRegion(sourceRegion);
            if (selectionStampSource == null)
            {
                return;
            }

            ReanchorSelectionToCapturedRegion(sourceRegion);

            selectionStampBeforeImage = new Bitmap(pictureBox1.Image);
            selectionStampSelectionBefore = Selection;
            selectionStampLastLocation = Selection.Location;
            selectionStampApplied = false;
            isCtrlStampingSelection = true;
        }

        private void EndSelectionStampGesture()
        {
            if (!isCtrlStampingSelection)
            {
                selectionStampSource?.Dispose();
                selectionStampSource = null;
                selectionStampBeforeImage?.Dispose();
                selectionStampBeforeImage = null;
                selectionStampApplied = false;
                return;
            }

            if (selectionStampApplied && selectionStampBeforeImage != null && pictureBox1.Image != null)
            {
                var afterSnapshot = new Bitmap(pictureBox1.Image);
                PushUndoStep(Rectangle.Empty, selectionStampBeforeImage, afterSnapshot, selectionStampSelectionBefore, Selection, true);
                selectionStampBeforeImage = null;
            }
            else
            {
                selectionStampBeforeImage?.Dispose();
                selectionStampBeforeImage = null;
            }

            selectionStampSource?.Dispose();
            selectionStampSource = null;
            selectionStampApplied = false;
            isCtrlStampingSelection = false;
            UpdateCommandUI();
        }

        private void BeginSelectionCloneGesture()
        {
            if (isAltCloningSelection || !HasEditableImage || Selection.IsEmpty || pictureBox1.Image == null)
            {
                return;
            }

            var sourceRegion = ClampToImage(Selection);
            if (sourceRegion.Width <= 0 || sourceRegion.Height <= 0)
            {
                return;
            }

            selectionCloneSource = CaptureRegion(sourceRegion);
            if (selectionCloneSource == null)
            {
                return;
            }

            ReanchorSelectionToCapturedRegion(sourceRegion);

            selectionCloneBeforeImage = new Bitmap(pictureBox1.Image);
            selectionCloneSelectionBefore = Selection;
            selectionCloneApplied = false;
            isAltCloningSelection = true;
        }

        private void EndSelectionCloneGesture()
        {
            if (!isAltCloningSelection)
            {
                selectionCloneSource?.Dispose();
                selectionCloneSource = null;
                selectionCloneBeforeImage?.Dispose();
                selectionCloneBeforeImage = null;
                selectionCloneApplied = false;
                return;
            }

            if (selectionCloneSource != null && pictureBox1.Image != null && Selection.Location != selectionCloneSelectionBefore.Location)
            {
                using (var g = Graphics.FromImage(pictureBox1.Image))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    StampClonedSelectionAt(g, Selection.Location);
                }
            }

            if (selectionCloneApplied && selectionCloneBeforeImage != null && pictureBox1.Image != null)
            {
                var afterSnapshot = new Bitmap(pictureBox1.Image);
                PushUndoStep(Rectangle.Empty, selectionCloneBeforeImage, afterSnapshot, selectionCloneSelectionBefore, Selection, true);
                selectionCloneBeforeImage = null;
            }
            else
            {
                selectionCloneBeforeImage?.Dispose();
                selectionCloneBeforeImage = null;
            }

            selectionCloneSource?.Dispose();
            selectionCloneSource = null;
            selectionCloneApplied = false;
            isAltCloningSelection = false;
            pictureBox1.Invalidate();
            UpdateCommandUI();
        }

        private void BeginCtrlResizeGesture()
        {
            if (isCtrlResizingSelection || !HasEditableImage || Selection.IsEmpty || pictureBox1.Image == null)
                return;

            var sourceRegion = ClampToImage(Selection);
            if (sourceRegion.Width <= 0 || sourceRegion.Height <= 0)
                return;

            selectionResizeSource = CaptureRegion(sourceRegion);
            if (selectionResizeSource == null)
                return;

            selectionResizeBeforeImage = new Bitmap(pictureBox1.Image);
            selectionResizeSelectionBefore = Selection;
            isCtrlResizingSelection = true;
        }

        private void EndCtrlResizeGesture()
        {
            if (!isCtrlResizingSelection)
            {
                selectionResizeSource?.Dispose();
                selectionResizeSource = null;
                selectionResizeBeforeImage?.Dispose();
                selectionResizeBeforeImage = null;
                return;
            }

            if (selectionResizeSource != null && pictureBox1.Image != null && Selection != selectionResizeSelectionBefore)
            {
                var dest = ClampToImage(Selection);
                if (dest.Width > 0 && dest.Height > 0)
                {
                    using (var g = Graphics.FromImage(pictureBox1.Image))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(selectionResizeSource, dest);
                    }

                    var afterSnapshot = new Bitmap(pictureBox1.Image);
                    PushUndoStep(Rectangle.Empty, selectionResizeBeforeImage, afterSnapshot, selectionResizeSelectionBefore, Selection, true);
                    selectionResizeBeforeImage = null;
                }
                else
                {
                    selectionResizeBeforeImage?.Dispose();
                    selectionResizeBeforeImage = null;
                }
            }
            else
            {
                selectionResizeBeforeImage?.Dispose();
                selectionResizeBeforeImage = null;
            }

            selectionResizeSource?.Dispose();
            selectionResizeSource = null;
            isCtrlResizingSelection = false;
            pictureBox1.Invalidate();
            UpdateCommandUI();
        }

        private void StampClonedSelectionAt(Graphics g, Point location)
        {
            if (selectionCloneSource == null)
            {
                return;
            }

            var destination = new Rectangle(location, selectionCloneSource.Size);
            var clampedDestination = ClampToImage(destination);
            if (clampedDestination.Width <= 0 || clampedDestination.Height <= 0)
            {
                return;
            }

            var sourceRect = new Rectangle(
                clampedDestination.X - destination.X,
                clampedDestination.Y - destination.Y,
                clampedDestination.Width,
                clampedDestination.Height
            );

            g.DrawImage(selectionCloneSource, clampedDestination, sourceRect, GraphicsUnit.Pixel);
            selectionCloneApplied = true;
        }

        private void ApplySelectionStampAlongPath(Point from, Point to)
        {
            if (!isCtrlStampingSelection || selectionStampSource == null || pictureBox1.Image == null)
            {
                return;
            }

            var canvas = pictureBox1.Image;
            using (var g = Graphics.FromImage(canvas))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                foreach (var point in EnumerateLinePoints(from, to))
                {
                    if (point == from)
                    {
                        continue;
                    }

                    StampSelectionAt(g, point);
                }
            }
        }

        private void StampSelectionAt(Graphics g, Point location)
        {
            if (selectionStampSource == null)
            {
                return;
            }

            var destination = new Rectangle(location, selectionStampSource.Size);
            var clampedDestination = ClampToImage(destination);
            if (clampedDestination.Width <= 0 || clampedDestination.Height <= 0)
            {
                return;
            }

            var sourceRect = new Rectangle(
                clampedDestination.X - destination.X,
                clampedDestination.Y - destination.Y,
                clampedDestination.Width,
                clampedDestination.Height
            );

            g.DrawImage(selectionStampSource, clampedDestination, sourceRect, GraphicsUnit.Pixel);
            selectionStampApplied = true;
        }

        private IEnumerable<Point> EnumerateLinePoints(Point from, Point to)
        {
            int x0 = from.X;
            int y0 = from.Y;
            int x1 = to.X;
            int y1 = to.Y;

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                yield return new Point(x0, y0);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = err * 2;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        /// <summary>
        /// Arrow keys act on the active marquee: plain arrows move it without touching pixels,
        /// Ctrl+Arrow stamps (mirrors ctrl-drag) and Alt+Arrow clones (mirrors alt-drag); Shift
        /// accelerates any of them to 10px per press. Each press nudges by image pixels, and a
        /// stamp/clone gesture stays open so repeated presses accumulate into a single undo
        /// step, closed when the modifier key is released (see ImageEditor_KeyUp) or when the
        /// mouse takes over.
        /// </summary>
        private bool TryHandleMarqueeArrowKey(Keys keyData)
        {
            var code = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) == Keys.Control;
            bool alt = (keyData & Keys.Alt) == Keys.Alt;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;

            // Ctrl+Alt combinations are left for the system (e.g. display rotation hotkeys).
            if (ctrl && alt)
                return false;

            if (!HasEditableImage || Selection.IsEmpty || pictureBox1?.Image == null)
                return false;

            if (isStraightenToolActive || isCensorToolActive)
                return false;

            if (activeTextAnnotation?.IsEditing == true)
                return false;

            // A held mouse button means a drag gesture owns the selection right now.
            if (MouseButtons != MouseButtons.None)
                return false;

            var focused = ActiveControl ?? FindFocusedControl();
            if (focused is TextBoxBase || focused is ComboBox || focused is ToolStrip || focused?.Parent is ToolStrip)
                return false;

            int step = shift ? 10 : 1;
            var delta = code switch
            {
                Keys.Left => new Size(-step, 0),
                Keys.Right => new Size(step, 0),
                Keys.Up => new Size(0, -step),
                Keys.Down => new Size(0, step),
                _ => Size.Empty,
            };

            if (delta.IsEmpty)
                return false;

            if (ctrl)
            {
                if (isAltCloningSelection)
                    EndSelectionCloneGesture();

                if (!isCtrlStampingSelection)
                    BeginSelectionStampGesture();

                if (!isCtrlStampingSelection)
                    return false;

                Selection = new Rectangle(Point.Add(Selection.Location, delta), Selection.Size);
                ApplySelectionStampAlongPath(selectionStampLastLocation, Selection.Location);
                selectionStampLastLocation = Selection.Location;
            }
            else if (alt)
            {
                if (isCtrlStampingSelection)
                    EndSelectionStampGesture();

                if (!isAltCloningSelection)
                    BeginSelectionCloneGesture();

                if (!isAltCloningSelection)
                    return false;

                Selection = new Rectangle(Point.Add(Selection.Location, delta), Selection.Size);
            }
            else
            {
                Selection = new Rectangle(Point.Add(Selection.Location, delta), Selection.Size);
            }

            pictureBox1.Invalidate();
            return true;
        }

        private void ImageEditor_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (pictureBox1?.Image == null)
                return;

            var cursorScreen = PointToScreen(e.Location);
            var cursorInViewport = pictureBox1.PointToClient(cursorScreen);
            if (Math.Abs(e.Delta) == 0)
                return;

            var nextZoom = e.Delta > 0 ? FindZoomIn(ZoomLevel) : FindZoomOut(ZoomLevel);
            pictureBox1.ZoomAround(nextZoom, cursorInViewport);
            _zoomlevel = pictureBox1.ZoomLevel;
        }

        private bool IsClose(int a, int b) => Math.Abs(Math.Max(a, b) - Math.Min(a, b)) < rzTolerance;
        private bool IsClose(Point a, Point b) => IsCloseHor(a, b) && IsCloseVer(a, b);
        private bool IsCloseHor(Point a, Point b) => Math.Abs(a.X - b.X) < rzTolerance;
        private bool IsCloseVer(Point a, Point b) => Math.Abs(a.Y - b.Y) < rzTolerance;
        private bool IsWithin(int val, int a, int b) => val >= Math.Min(a, b) && val <= Math.Max(a, b);

        private ResizeMode GetResizeMode(Point pt)
        {
            var formPt = PixelToFormCoord(pt);
            var formSelection = PixelToFormCoord(Selection);
            if (IsClose(formPt, new Point(formSelection.Right, formSelection.Top))) return ResizeMode.ResizeTR;
            if (IsClose(formPt, new Point(formSelection.Right, formSelection.Bottom))) return ResizeMode.ResizeBR;
            if (IsClose(formPt, new Point(formSelection.Left, formSelection.Bottom))) return ResizeMode.ResizeBL;
            if (IsClose(formPt, new Point(formSelection.Left, formSelection.Top))) return ResizeMode.ResizeTL;

            if (IsWithin(formPt.X, formSelection.Left, formSelection.Right))
            {
                if (IsClose(formPt.Y, formSelection.Top)) return ResizeMode.ResizeT;
                if (IsClose(formPt.Y, formSelection.Bottom)) return ResizeMode.ResizeB;
            }

            if (IsWithin(formPt.Y, formSelection.Top, formSelection.Bottom))
            {
                if (IsClose(formPt.X, formSelection.Left)) return ResizeMode.ResizeL;
                if (IsClose(formPt.X, formSelection.Right)) return ResizeMode.ResizeR;
            }

            if (formSelection.Contains(formPt))
                return ResizeMode.Move;

            return ResizeMode.None;
        }

        private void pictureBox1_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                HandleTextToolDoubleClick(FormCoordToPixel(e.Location), e.Location);
            }
        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            // The viewport is not a tab stop, so clicking it does not reliably take focus from
            // sibling controls such as the history thumbnails. Make the user's click authoritative
            // so editor shortcuts are routed back to this ImageEditor.
            pictureBox1.Focus();

            if (isStraightenToolActive)
            {
                if (e.Button == MouseButtons.Left)
                {
                    straightenLineStartPixel = FormCoordToPixel(e.Location);
                    straightenLineEndPixel = straightenLineStartPixel;
                    isStraightenLineDragging = true;
                    UpdateStraightenToolbarState();
                    pictureBox1.Invalidate();
                }

                base.OnMouseDown(e);
                return;
            }

            if (isCensorToolActive)
            {
                if (e.Button == MouseButtons.Left)
                {
                    var pixelPoint = FormCoordToPixel(e.Location);
                    var region = FindRegionAtPixel(pixelPoint);
                    if (region != null)
                    {
                        region.Selected = !region.Selected;
                        UpdateCensorToolbarState();
                        pictureBox1.Invalidate();
                    }
                }

                base.OnMouseDown(e);
                return;
            }

            if (isFreeRotateToolActive)
            {
                if (e.Button == MouseButtons.Left && HitTestFreeRotateHandle(FormCoordToPixel(e.Location)))
                {
                    BeginFreeRotateDrag(FormCoordToPixel(e.Location));
                }

                base.OnMouseDown(e);
                return;
            }

            var cursorPixel = FormCoordToPixel(e.Location);

            if (e.Button == MouseButtons.Left && HandleTextToolMouseDown(cursorPixel, e.Location))
            {
                pictureBox1.Invalidate();
                base.OnMouseDown(e);
                return;
            }

            if (e.Button == MouseButtons.Left && HandleAnnotationMouseDown(cursorPixel, e.Location))
            {
                pictureBox1.Invalidate();
                base.OnMouseDown(e);
                return;
            }

            // Move-mode: in the default mode (no other tool active), clicking on an image layer
            // (or its handle) selects it / starts drag-translate / starts drag-resize.
            // Annotations and text annotations have already had their chance above; layers sit
            // beneath them in the visual stack, so they hit-test last among content types.
            if (e.Button == MouseButtons.Left && TryBeginLayerInteraction(cursorPixel))
            {
                base.OnMouseDown(e);
                return;
            }

            // Click landed on empty canvas in Move mode → deselect any selected content
            // (layer, text annotation, shape annotation) and fall through to the existing
            // rubber-band/resize/pan behavior.
            if (e.Button == MouseButtons.Left)
            {
                bool changed = DeselectImageLayerIfAny();
                if (selectedTextAnnotation != null)
                {
                    SelectTextAnnotation(null);
                    changed = true;
                }
                if (selectedAnnotation != null)
                {
                    SelectAnnotation(null);
                    changed = true;
                }
                if (changed)
                {
                    pictureBox1.Invalidate();
                }
            }

            MouseInPixel = cursorPixel;
            MouseOutPixel = MouseInPixel;

            if (e.Button == MouseButtons.Left)
            {
                rzMode = GetResizeMode(MouseInPixel);

                if (rzMode != ResizeMode.None)
                {
                    SelectionGrabOrigin = Selection;
                    Cursor = ResizeCursors[rzMode];
                }
                else
                {
                    isDrawingRubberBand = true;
                    rubberBandStartClient = e.Location;
                }

            }
            else if (e.Button == MouseButtons.Middle)
            {
                isPanning = true;
                MouseInPixel = e.Location;
                Cursor = Cursors.SizeAll;
            }

            base.OnMouseDown(e);
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (isStraightenToolActive)
            {
                Cursor = Cursors.Cross;
                if (isStraightenLineDragging && e.Button == MouseButtons.Left)
                {
                    straightenLineEndPixel = FormCoordToPixel(e.Location);
                    UpdateStraightenToolbarState();
                    pictureBox1.Invalidate();
                }

                base.OnMouseMove(e);
                return;
            }

            if (isCensorToolActive)
            {
                if (e.Button != MouseButtons.Left)
                {
                    var pixelPoint = FormCoordToPixel(e.Location);
                    Cursor = FindRegionAtPixel(pixelPoint) != null ? Cursors.Hand : Cursors.Default;
                }

                base.OnMouseMove(e);
                return;
            }

            if (isFreeRotateToolActive)
            {
                var pixelPoint = FormCoordToPixel(e.Location);
                if (isFreeRotateDragging && e.Button == MouseButtons.Left)
                {
                    UpdateFreeRotateDrag(pixelPoint);
                }
                else
                {
                    Cursor = HitTestFreeRotateHandle(pixelPoint) ? Cursors.Cross : Cursors.Default;
                }

                base.OnMouseMove(e);
                return;
            }

            var cursorPixel = FormCoordToPixel(e.Location);
            if (HandleTextToolMouseMove(cursorPixel, e.Location, e.Button))
            {
                base.OnMouseMove(e);
                return;
            }

            if (HandleAnnotationMouseMove(cursorPixel, e.Location, e.Button))
            {
                base.OnMouseMove(e);
                return;
            }

            // Move-mode: drag-translate or drag-resize a selected layer.
            if (isLayerInteractionActive && e.Button == MouseButtons.Left)
            {
                UpdateLayerInteraction(cursorPixel);
                Cursor = CursorForLayerHandle(activeLayerHandle);
                base.OnMouseMove(e);
                return;
            }

            // Hover cursor feedback over a selected layer's handles / body.
            if (e.Button == MouseButtons.None && HasSelectedLayer)
            {
                var hoverHandle = HitTestSelectedLayerHandle(cursorPixel);
                if (hoverHandle != ImageLayerHandle.None)
                {
                    Cursor = CursorForLayerHandle(hoverHandle);
                }
                else if (HitTestLayerBody(cursorPixel) is int idx && idx == selectedLayerIndex)
                {
                    Cursor = Cursors.SizeAll;
                }
            }

            if (e.Button == MouseButtons.Left)
            {
                if (rzMode != ResizeMode.None)
                {
                    if (rzMode == ResizeMode.Move && IsCtrlModifierDown() && !isCtrlStampingSelection && !isAltCloningSelection)
                    {
                        BeginSelectionStampGesture();
                    }

                    if (rzMode == ResizeMode.Move && !IsCtrlModifierDown() && IsAltModifierDown() && !isAltCloningSelection)
                    {
                        BeginSelectionCloneGesture();
                    }

                    if (rzMode != ResizeMode.Move && IsCtrlModifierDown() && !isCtrlResizingSelection)
                    {
                        BeginCtrlResizeGesture();
                    }

                    SetSelectionEdge(ApplyCloneStampAxisLock(cursorPixel));

                    if (rzMode == ResizeMode.Move)
                    {
                        if (IsCtrlModifierDown())
                        {
                            if (isCtrlStampingSelection)
                            {
                                ApplySelectionStampAlongPath(selectionStampLastLocation, Selection.Location);
                                selectionStampLastLocation = Selection.Location;
                            }
                        }
                        else if (isCtrlStampingSelection)
                        {
                            selectionStampLastLocation = Selection.Location;
                        }
                    }

                    pictureBox1.Invalidate();
                }

                if (isDrawingRubberBand)
                {
                    if (isMovingSelection)
                    {
                        var MoveOffset = cursorPixel.Subtract(MoveInPixel);
                        MouseInPixel = MouseInPixel.Add(MoveOffset);
                        MouseOutPixel = MouseOutPixel.Add(MoveOffset);
                        MoveInPixel = cursorPixel;
                    }
                    else if (ModifierKeys.HasFlag(Keys.Shift))
                    {
                        var dx = cursorPixel.X - MouseInPixel.X;
                        var dy = cursorPixel.Y - MouseInPixel.Y;
                        var square = Math.Max(Math.Abs(dx), Math.Abs(dy));

                        var dirX = dx == 0 ? (dy < 0 ? -1 : 1) : Math.Sign(dx);
                        var dirY = dy == 0 ? (dx < 0 ? -1 : 1) : Math.Sign(dy);

                        MouseOutPixel = new Point(
                            MouseInPixel.X + (dirX * square),
                            MouseInPixel.Y + (dirY * square));
                    }
                    else
                    {
                        MouseOutPixel = cursorPixel;

                    }
                    if (ModifierKeys.HasFlag(Keys.Control))
                    {
                        var delta = MouseOutPixel.Subtract(MouseInPixel);
                        delta = new Point(
                            (int)(Math.Round(delta.X / 16.0) * 16),
                            (int)(Math.Round(delta.Y / 16.0) * 16)
                        );

                        MouseOutPixel = MouseInPixel.Add(delta);
                    }
                    UpdateRubberBandStatus();
                    pictureBox1.Invalidate();
                }
            }


            else if (e.Button == MouseButtons.Middle && isPanning)
            {
                var ofsPoint = e.Location.Subtract(MouseInPixel);
                pictureBox1.PanBy(new Size(ofsPoint));
                MouseInPixel = e.Location;
            }
            else
            {
                var hoverResizeMode = GetResizeMode(FormCoordToPixel(e.Location));
                if (hoverResizeMode != ResizeMode.None)
                {
                    Cursor = ResizeCursors[hoverResizeMode];
                }
                else
                {
                    Cursor = Cursors.Default;
                }

            }

            base.OnMouseMove(e);
        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            if (isStraightenToolActive)
            {
                if (e.Button == MouseButtons.Left && isStraightenLineDragging)
                {
                    isStraightenLineDragging = false;
                    straightenLineEndPixel = FormCoordToPixel(e.Location);
                    UpdateStraightenToolbarState();
                    pictureBox1.Invalidate();
                }

                base.OnMouseUp(e);
                return;
            }

            Cursor = Cursors.Default;

            if (isCensorToolActive)
            {
                base.OnMouseUp(e);
                return;
            }

            if (isFreeRotateToolActive)
            {
                if (e.Button == MouseButtons.Left && isFreeRotateDragging)
                {
                    EndFreeRotateDrag();
                }

                base.OnMouseUp(e);
                return;
            }

            if (HandleTextToolMouseUp(e.Button, FormCoordToPixel(e.Location)))
            {
                base.OnMouseUp(e);
                return;
            }

            if (HandleAnnotationMouseUp(e.Button, FormCoordToPixel(e.Location)))
            {
                base.OnMouseUp(e);
                return;
            }

            if (isLayerInteractionActive && e.Button == MouseButtons.Left)
            {
                EndLayerInteraction();
                base.OnMouseUp(e);
                return;
            }

            if (e.Button == MouseButtons.Left && isDrawingRubberBand)
            {
                var dx = Math.Abs(e.Location.X - rubberBandStartClient.X);
                var dy = Math.Abs(e.Location.Y - rubberBandStartClient.Y);
                var candidate = ClampToImage(GetNormalizedRect(MouseInPixel, MouseOutPixel));
                Selection = (dx >= 4 || dy >= 4) ? candidate : Rectangle.Empty;
                isDrawingRubberBand = false;
                pictureBox1.Invalidate();
                UpdateCommandUI();
            }
            if (e.Button == MouseButtons.Left)
            {
                EndSelectionStampGesture();
                EndSelectionCloneGesture();
                EndCtrlResizeGesture();
            }
            else if (e.Button == MouseButtons.Middle)
            {
                isPanning = false;
            }

            base.OnMouseUp(e);
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            if (isDrawingRubberBand)
            {
                var rubberBand = ClampToImage(GetNormalizedRect(MouseInPixel, MouseOutPixel));
                DrawMarchingAntsRectangle(e.Graphics, PixelToFormCoord(rubberBand), 2f);
            }
            else if (!Selection.IsEmpty)
            {
                // Deliberately unclamped: a stamp/clone move may hang the marquee past the
                // canvas edge, and it should be drawn where it actually is.
                DrawMarchingAntsRectangle(e.Graphics, PixelToFormCoord(Selection), 2f);
            }

            if (isAltCloningSelection && selectionCloneSource != null)
            {
                var destination = ClampToImage(new Rectangle(Selection.Location, selectionCloneSource.Size));
                if (destination.Width > 0 && destination.Height > 0)
                {
                    var sourceRect = new Rectangle(
                        destination.X - Selection.X,
                        destination.Y - Selection.Y,
                        destination.Width,
                        destination.Height
                    );

                    e.Graphics.DrawImage(selectionCloneSource, PixelToFormCoord(destination), sourceRect, GraphicsUnit.Pixel);
                }
            }

            if (isCtrlResizingSelection && selectionResizeSource != null && !Selection.IsEmpty)
            {
                var dest = PixelToFormCoord(ClampToImage(Selection));
                if (dest.Width > 0 && dest.Height > 0)
                {
                    e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    e.Graphics.DrawImage(selectionResizeSource, dest);
                }
            }

            DrawImageLayers(e.Graphics, AnnotationSurface.Screen);
            DrawAnnotations(e.Graphics, AnnotationSurface.Screen);
            DrawTextAnnotations(e.Graphics, AnnotationSurface.Screen);
            DrawSelectedLayerOverlay(e.Graphics);
            DrawStraightenOverlay(e.Graphics);
            DrawFreeRotateOverlay(e.Graphics);

            if (isCensorToolActive && censorRegions.Count > 0)
            {
                using (var selectedPen = new Pen(Color.Cyan, 2f))
                using (var unselectedPen = new Pen(Color.Gray, 2f))
                using (var inactiveBrush = new SolidBrush(Color.FromArgb(120, Color.Black)))
                using (var selectedOverlayBrush = new SolidBrush(Color.FromArgb(60, Color.Cyan)))
                {
                    unselectedPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

                    foreach (var region in censorRegions)
                    {
                        if (region.Bounds.Width <= 0 || region.Bounds.Height <= 0)
                        {
                            continue;
                        }

                        var rect = PixelToFormCoord(region.Bounds);
                        if (region.Selected)
                        {
                            if (censorPreviewBuffer != null)
                            {
                                e.Graphics.DrawImage(censorPreviewBuffer, rect, region.Bounds, GraphicsUnit.Pixel);
                                e.Graphics.FillRectangle(selectedOverlayBrush, rect);
                            }
                            else
                            {
                                e.Graphics.FillRectangle(selectedOverlayBrush, rect);
                            }
                        }
                        else
                        {
                            e.Graphics.FillRectangle(inactiveBrush, rect);
                        }

                        var pen = region.Selected ? selectedPen : unselectedPen;
                        e.Graphics.DrawRectangle(pen, rect);
                    }
                }
            }
            base.OnPaint(e);
        }

        private void ImageEditor_ResizeEnd(object sender, EventArgs e)
        {
            HandleResize();
        }

        private void ImageEditor_Paint(object sender, PaintEventArgs e)
        {
        }
    }
}
