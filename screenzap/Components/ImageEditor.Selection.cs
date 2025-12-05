using System;
using System.Collections.Generic;
using System.Drawing;
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

        private Rectangle _selection;
        private SelectionMetrics _selectionMetrics = SelectionMetrics.Empty;
        private Rectangle Selection
        {
            get => _selection;
            set
            {
                _selection = value;
                UpdateSelectionMetrics();
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
                    SetSelectionEdge(cursorPixel);
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
                var pen = new Pen(Pens.White.Brush, 2);
                e.Graphics.DrawRectangle(pen, PixelToFormCoord(RectangleExt.fromPoints(MouseInPixel, MouseOutPixel)));
            }
            else if (!Selection.IsEmpty)
            {
                var pen = new Pen(Pens.Cyan.Brush, 2);
                e.Graphics.DrawRectangle(pen, PixelToFormCoord(Selection));
            }

            DrawAnnotations(e.Graphics, AnnotationSurface.Screen);
            DrawTextAnnotations(e.Graphics, AnnotationSurface.Screen);

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
