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
            0.25m, 0.5m, 0.75m, 1m, 1.25m, 1.5m, 2m, 3m, 4m, 5m, 6m, 7m, 8m
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
                var newLocation = new Point(SelectionGrabOrigin.X - Delta.X, SelectionGrabOrigin.Y - Delta.Y);
                Selection = new Rectangle(newLocation, Selection.Size);
            }
        }

        private bool IsCtrlModifierDown() => (ModifierKeys & Keys.Control) == Keys.Control;
        private bool IsAltModifierDown() => (ModifierKeys & Keys.Alt) == Keys.Alt;

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

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
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

                    SetSelectionEdge(cursorPixel);

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
                        Rectangle currentRubberBand = RectangleExt.fromPoints(MouseInPixel, cursorPixel);
                        var square = Math.Max(currentRubberBand.Width, currentRubberBand.Height);
                        MouseOutPixel = MouseInPixel.Add(square);
                    }
                    else
                    {
                        MouseOutPixel = cursorPixel;

                    }
                    if (ModifierKeys.HasFlag(Keys.ControlKey))
                    {
                        var delta = MouseOutPixel.Subtract(MouseInPixel);
                        delta = new Point(
                            (int)(Math.Round(delta.X / 16.0) * 16),
                            (int)(Math.Round(delta.Y / 16.0) * 16)
                        );

                        MouseOutPixel = delta;
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

            if (e.Button == MouseButtons.Left && isDrawingRubberBand)
            {
                Selection = GetNormalizedRect(MouseInPixel, MouseOutPixel);
                isDrawingRubberBand = false;
                pictureBox1.Invalidate();
                UpdateCommandUI();
            }
            if (e.Button == MouseButtons.Left)
            {
                EndSelectionStampGesture();
                EndSelectionCloneGesture();
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
                DrawMarchingAntsRectangle(e.Graphics, PixelToFormCoord(RectangleExt.fromPoints(MouseInPixel, MouseOutPixel)), 2f);
            }
            else if (!Selection.IsEmpty)
            {
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

            DrawAnnotations(e.Graphics, AnnotationSurface.Screen);
            DrawTextAnnotations(e.Graphics, AnnotationSurface.Screen);
            DrawStraightenOverlay(e.Graphics);

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
