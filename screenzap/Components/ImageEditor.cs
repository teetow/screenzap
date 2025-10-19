using screenzap.lib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace screenzap
{
    enum SelectionMode
    {
        None,
        Selecting,
    }

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


    public partial class ImageEditor : Form
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                Image imgData = Clipboard.GetImage();
                if (imgData != null)
                {
                    LoadImage(imgData);
                }
                else
                {
                    ShowPlaceholder();
                }

            }
            //Called for any unhandled messages
            base.WndProc(ref m);
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;  // Turn on WS_EX_COMPOSITED
                return cp;
            }
        }

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

        private UndoRedo undoStack;

    private const string WindowTitleBase = "Screenzap Image Editor";

    private DateTime? bufferTimestamp;
    private string currentSavePath;
    private bool isPlaceholderImage;

    private bool HasEditableImage => pictureBox1.Image != null && !isPlaceholderImage;

    private int ToolbarHeight => mainToolStrip?.Height ?? 0;

    private Size GetCanvasSize()
    {
        if (canvasPanel != null)
        {
            return canvasPanel.ClientSize;
        }

        return new Size(ClientSize.Width, Math.Max(0, ClientSize.Height - ToolbarHeight));
    }

    Point MouseInPixel;
        Point MouseOutPixel;
        bool isDrawingRubberBand;
        bool isMovingSelection;
        Point MoveInPixel;

        private Rectangle Selection;
        private Rectangle SelectionGrabOrigin;

        private void ClearSelection()
        {
            Selection = new Rectangle(0, 0, 0, 0);
            UpdateCommandUI();
        }

        ResizeMode rzMode;

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
                Selection.Location = new Point(SelectionGrabOrigin.X - Delta.X, SelectionGrabOrigin.Y - Delta.Y);
            }
        }

        //Point PanIn;
        bool isPanning;

        static readonly Dictionary<ResizeMode, Cursor> ResizeCursors = new Dictionary<ResizeMode, Cursor>
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

        readonly int rzTolerance = 5;

        decimal _zoomlevel = 1;
        private decimal ZoomLevel
        {
            get { return _zoomlevel; }
            set
            {
                _zoomlevel = value;
                if (pictureBox1.Image != null)
                {
                    pictureBox1.Size = pictureBox1.Image.Size.Multiply(_zoomlevel);
                }
            }
        }


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

                gr.DrawString("No image data in clipboard", SystemFonts.CaptionFont, Brushes.White, new PointF(320, 100), sf);

                ResetZoom();
                LoadImage(bmp, true);
            }
        }

        private Point PixelToFormCoord(Point pt)
        {
            return pt.Multiply(ZoomLevel);
        }
        private Rectangle PixelToFormCoord(Rectangle rect)
        {
            return RectangleExt.fromPoints(PixelToFormCoord(rect.Location), PixelToFormCoord(rect.Location.Add(rect.Size)));
        }

        private Point FormCoordToPixel(Point pt)
        {
            return pt.Divide(ZoomLevel);
        }

        private Rectangle GetNormalizedRect(Point a, Point b)
        {
            return new Rectangle(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X),
                Math.Abs(a.Y - b.Y)
            );
        }

        private void Init()
        {
            NativeMethods.AddClipboardFormatListener(Handle);

            BackColor = Color.Gray;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

            InitializeComponent();

            MouseWheel += ImageEditor_MouseWheel;
            pictureBox1.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

            undoStack = new UndoRedo();
            ClearSelection();
        }

        public ImageEditor()
        {
            Init();
            ShowPlaceholder();
        }

        public ImageEditor(Image image)
        {
            Init();
            LoadImage(image);
        }
        internal void LoadImage(Image imgData)
        {
            LoadImage(imgData, false);
        }

        internal void LoadImage(Image imgData, bool treatAsPlaceholder)
        {
            if (imgData == null)
                return;

            isPlaceholderImage = treatAsPlaceholder;
            bufferTimestamp = treatAsPlaceholder ? (DateTime?)null : (ClipboardMetadata.LastCaptureTimestamp ?? DateTime.Now);
            currentSavePath = null;

            ClearSelection();
            ResetZoom();

            var replacementImage = new Bitmap(imgData);

            if (pictureBox1.Image != null)
            {
                pictureBox1.Image.Dispose();
            }

            pictureBox1.Image = replacementImage;
            pictureBox1.Location = Point.Empty;
            pictureBox1.Size = imgData.Size;

            var toolbarHeight = ToolbarHeight;
            var targetWidth = Math.Max(imgData.Size.Width, MinimumSize.Width);
            var targetHeight = Math.Max(imgData.Size.Height + toolbarHeight, MinimumSize.Height);
            ClientSize = new Size(targetWidth, targetHeight);

            HandleResize();

            undoStack.Clear();

            UpdateCommandUI();
            UpdateWindowTitle();
        }

        internal void ResetZoom()
        {
            ZoomLevel = 1;
        }

        internal void HandleResize()
        {
            ClampImageLocationWithinCanvas();
            Invalidate();
        }

        private void ClampImageLocationWithinCanvas()
        {
            if (pictureBox1.Image == null)
            {
                return;
            }

            var canvasSize = GetCanvasSize();
            if (canvasSize.Width <= 0 || canvasSize.Height <= 0)
            {
                return;
            }

            var constrainedLeft = pictureBox1.Location.X;
            if (pictureBox1.Width <= canvasSize.Width)
            {
                constrainedLeft = (canvasSize.Width - pictureBox1.Width) / 2;
            }
            else
            {
                var minLeft = canvasSize.Width - pictureBox1.Width;
                constrainedLeft = Math.Min(0, Math.Max(minLeft, constrainedLeft));
            }

            var constrainedTop = pictureBox1.Location.Y;
            if (pictureBox1.Height <= canvasSize.Height)
            {
                constrainedTop = (canvasSize.Height - pictureBox1.Height) / 2;
            }
            else
            {
                var minTop = canvasSize.Height - pictureBox1.Height;
                constrainedTop = Math.Min(0, Math.Max(minTop, constrainedTop));
            }

            pictureBox1.Location = new Point(constrainedLeft, constrainedTop);
        }

        private Rectangle GetImageBounds()
        {
            return pictureBox1.Image == null
                ? Rectangle.Empty
                : new Rectangle(Point.Empty, pictureBox1.Image.Size);
        }

        private Rectangle ClampToImage(Rectangle region)
        {
            if (pictureBox1.Image == null)
            {
                return Rectangle.Empty;
            }

            var bounds = GetImageBounds();
            var intersection = Rectangle.Intersect(bounds, region);
            return intersection;
        }

        private Bitmap CaptureRegion(Rectangle region)
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

        private void PushUndoStep(Rectangle region, Bitmap before, Bitmap after, Rectangle selectionBefore, Rectangle selectionAfter, bool replacesImage = false)
        {
            if (before == null || after == null)
            {
                before?.Dispose();
                after?.Dispose();
                return;
            }

            if (!replacesImage && (region.Width <= 0 || region.Height <= 0))
            {
                before.Dispose();
                after.Dispose();
                return;
            }

            undoStack.Push(new ImageUndoStep(region, before, after, selectionBefore, selectionAfter, replacesImage));
        }

        private void ApplyUndoStep(ImageUndoStep step, bool applyAfterState)
        {
            if (step == null || pictureBox1.Image == null)
            {
                return;
            }

            var source = applyAfterState ? step.After : step.Before;
            if (source == null)
            {
                return;
            }

            if (step.ReplacesImage)
            {
                var replacement = new Bitmap(source);
                pictureBox1.Image?.Dispose();
                pictureBox1.Image = replacement;
                pictureBox1.Size = pictureBox1.Image.Size.Multiply(ZoomLevel);
                ClientSize = new Size(Math.Max(pictureBox1.Image.Width, MinimumSize.Width), Math.Max(pictureBox1.Image.Height + ToolbarHeight, MinimumSize.Height));
                HandleResize();
            }
            else
            {
                var region = ClampToImage(step.Region);
                if (region.Width <= 0 || region.Height <= 0)
                {
                    return;
                }

                using (var g = Graphics.FromImage(pictureBox1.Image))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(source, region);
                }
            }

            Selection = applyAfterState ? step.SelectionAfter : step.SelectionBefore;
            UpdateCommandUI();
            pictureBox1.Invalidate();
        }

        readonly decimal[] ZoomLevels = {
           0.25m, 0.5m, 0.75m, 1m, 1.25m, 1.5m, 2m, 3m, 4m, 5m, 6m, 7m, 8m };

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

        private void ImageEditor_MouseWheel(object sender, MouseEventArgs e)
        {
            if (pictureBox1.Image == null)
                return;
            if (canvasPanel == null)
                return;
            var cursorScreen = PointToScreen(e.Location);
            var cursorInPanel = canvasPanel.PointToClient(cursorScreen);
            var targetPixel = FormCoordToPixel(cursorInPanel.Subtract(pictureBox1.Location));

            var pol = e.Delta / Math.Abs(e.Delta);

            ZoomLevel = pol > 0 ? FindZoomIn(ZoomLevel) : FindZoomOut(ZoomLevel);

            cursorInPanel = canvasPanel.PointToClient(cursorScreen);
            var newTargetPixel = FormCoordToPixel(cursorInPanel.Subtract(pictureBox1.Location));
            var offset = PixelToFormCoord(targetPixel.Subtract(newTargetPixel));
            pictureBox1.Location = pictureBox1.Location.Subtract(offset);

            var canvasSize = GetCanvasSize();
            if (pictureBox1.Width <= canvasSize.Width && pictureBox1.Height <= canvasSize.Height)
            {
                HandleResize();
            }
            else
            {
                ClampImageLocationWithinCanvas();
            }
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
            // corners
            if (IsClose(formPt, new Point(formSelection.Right, formSelection.Top))) return ResizeMode.ResizeTR;
            if (IsClose(formPt, new Point(formSelection.Right, formSelection.Bottom))) return ResizeMode.ResizeBR;
            if (IsClose(formPt, new Point(formSelection.Left, formSelection.Bottom))) return ResizeMode.ResizeBL;
            if (IsClose(formPt, new Point(formSelection.Left, formSelection.Top))) return ResizeMode.ResizeTL;

            // top and bottom edges
            if (IsWithin(formPt.X, formSelection.Left, formSelection.Right))
            {
                if (IsClose(formPt.Y, formSelection.Top)) return ResizeMode.ResizeT;
                if (IsClose(formPt.Y, formSelection.Bottom)) return ResizeMode.ResizeB;
            }

            // left and right edges
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
            MouseInPixel = FormCoordToPixel(e.Location);
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
            if (e.Button == MouseButtons.Left)
            {
                if (rzMode != ResizeMode.None)
                {
                    // we're currently resizing the selection
                    SetSelectionEdge(FormCoordToPixel(e.Location));
                    pictureBox1.Invalidate();
                }

                if (isDrawingRubberBand)
                {
                    if (isMovingSelection)
                    {
                        var MoveOffset = FormCoordToPixel(e.Location).Subtract(MoveInPixel);
                        MouseInPixel = MouseInPixel.Add(MoveOffset);
                        MouseOutPixel = MouseOutPixel.Add(MoveOffset);
                        MoveInPixel = FormCoordToPixel(e.Location);
                    }
                    else if (ModifierKeys.HasFlag(Keys.Shift))
                    {
                        Rectangle currentRubberBand = RectangleExt.fromPoints(MouseInPixel, FormCoordToPixel(e.Location));
                        var square = Math.Max(currentRubberBand.Width, currentRubberBand.Height);
                        MouseOutPixel = MouseInPixel.Add(square);
                    }
                    else
                    {
                        MouseOutPixel = FormCoordToPixel(e.Location);

                    }
                    if (ModifierKeys.HasFlag(Keys.ControlKey))
                    {
                        var delta = MouseOutPixel.Subtract(MouseInPixel);
                        // round to 16 pixels
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
                var ofs = new Size(e.Location.Subtract(MouseInPixel));
                pictureBox1.Location += ofs;
                ClampImageLocationWithinCanvas();
            }
            else // no mouse button held
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
            base.OnPaint(e);
        }

        private void ImageEditor_ResizeEnd(object sender, EventArgs e)
        {
            HandleResize();
        }

        private void ImageEditor_Paint(object sender, PaintEventArgs e)
        {
            //e.Graphics.DrawString($"{ZoomLevel}x", SystemFonts.CaptionFont, Brushes.White, 0, 0);
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

            Bitmap after = null;

            try
            {
                after = GenerateCensoredBitmap(before, clampedSelection);

                using (var gImg = Graphics.FromImage(pictureBox1.Image))
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

                int rngSeed = unchecked(selectionBounds.Left * 397 ^ selectionBounds.Top * 911 ^ selectionBounds.Width * 31 ^ selectionBounds.Height * 17 ^ Environment.TickCount);
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

        private bool ExecuteReplaceWithBackground()
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

            Bitmap after = null;

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
                            bool isBorder = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                            filled[idx] = isBorder;
                            if (isBorder)
                            {
                                queue.Enqueue(new Point(x, y));
                            }
                        }
                    }

                    int[] offsets = new[] { -1, 1, -stridePixels, stridePixels };

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
                                int[] neighborOffsets = { 0, -1, 1, -stridePixels, stridePixels };
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

                                int newColor =
                                    ((a / count) << 24) |
                                    ((r / count) << 16) |
                                    ((g / count) << 8) |
                                    (b / count);

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

                using (var gImg = Graphics.FromImage(pictureBox1.Image))
                {
                    gImg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    gImg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
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

            var beforeImage = new Bitmap(pictureBox1.Image);

            Bitmap afterSnapshot = new Bitmap(clampedSelection.Width, clampedSelection.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(afterSnapshot))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(pictureBox1.Image, new Rectangle(Point.Empty, clampedSelection.Size), clampedSelection, GraphicsUnit.Pixel);
            }

            var newImage = new Bitmap(afterSnapshot);

            pictureBox1.Image?.Dispose();
            pictureBox1.Image = newImage;
            pictureBox1.Size = pictureBox1.Image.Size.Multiply(ZoomLevel);

            ClientSize = new Size(
                Math.Max(pictureBox1.Image.Width, MinimumSize.Width),
                Math.Max(pictureBox1.Image.Height + ToolbarHeight, MinimumSize.Height));

            HandleResize();

            Selection = selectionAfter;
            isPlaceholderImage = false;

            PushUndoStep(Rectangle.Empty, beforeImage, afterSnapshot, selectionBefore, selectionAfter, true);

            UpdateCommandUI();
            pictureBox1.Invalidate();
            return true;
        }

        private void UpdateCommandUI()
        {
            bool enable = HasEditableImage;
            if (saveToolStripButton != null)
            {
                saveToolStripButton.Enabled = enable;
            }
            if (saveAsToolStripButton != null)
            {
                saveAsToolStripButton.Enabled = enable;
            }
            if (cropToolStripButton != null)
            {
                cropToolStripButton.Enabled = enable && !Selection.IsEmpty;
            }
            if (replaceToolStripButton != null)
            {
                replaceToolStripButton.Enabled = enable && !Selection.IsEmpty;
            }
        }

        private void UpdateWindowTitle()
        {
            string title = WindowTitleBase;

            if (HasEditableImage)
            {
                if (!string.IsNullOrWhiteSpace(currentSavePath))
                {
                    title = $"{WindowTitleBase} - {Path.GetFileName(currentSavePath)}";
                }
                else if (bufferTimestamp.HasValue)
                {
                    title = $"{WindowTitleBase} - {bufferTimestamp.Value:yyyy-MM-dd HH:mm:ss}";
                }
            }

            Text = title;
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
            } while (File.Exists(candidate));

            return candidate;
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

                using (var bmp = new Bitmap(pictureBox1.Image))
                {
                    bmp.Save(targetPath, ImageFormat.Png);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save image.\n{ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                UpdateCommandUI();
                UpdateWindowTitle();
                return true;
            }

            if (generatedPath)
            {
                currentSavePath = null;
            }

            return false;
        }

        private bool ExecuteSaveAs()
        {
            if (!HasEditableImage)
            {
                return false;
            }

            string defaultPath = EnsureUniquePath(BuildDefaultSavePath());

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.DefaultExt = "png";
                dialog.Filter = "PNG Image|*.png|All Files|*.*";
                dialog.FileName = Path.GetFileName(defaultPath);
                dialog.InitialDirectory = Path.GetDirectoryName(defaultPath);
                dialog.OverwritePrompt = true;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (PersistImage(dialog.FileName))
                    {
                        currentSavePath = dialog.FileName;
                        UpdateCommandUI();
                        UpdateWindowTitle();
                        return true;
                    }
                }
            }

            return false;
        }

        private void ImageEditor_KeyDown(object sender, KeyEventArgs e)
        {
            //Console.WriteLine(e.Modifiers);

            if (isDrawingRubberBand && e.KeyCode == Keys.Space)
            {
                isMovingSelection = true;
                MoveInPixel = MouseOutPixel;
            }

            else if (e.KeyCode == Keys.C && e.Control == true)
            {
                var bmp = new Bitmap(Selection.Width, Selection.Height);
                var gr = Graphics.FromImage(bmp);
                gr.DrawImage(pictureBox1.Image, Selection.X * -1, Selection.Y * -1);
                Clipboard.SetImage(bmp);
                ClearSelection();
            }
            else if (e.KeyCode == Keys.E && e.Control == true)
            {
                if (CensorSelection())
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
                    handled = ExecuteSaveAs();
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
            else if (e.KeyCode == Keys.T && e.Control)
            {
                if (ExecuteCrop())
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
                    var undoStep = undoStack.Undo();
                    if (undoStep != null)
                    {
                        ApplyUndoStep(undoStep, false);
                        handled = true;
                    }
                }

                if (handled)
                {
                    UpdateCommandUI();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
        }
        private void ImageEditor_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                isMovingSelection = false;
            }
        }

        private void saveToolStripButton_Click(object sender, EventArgs e)
        {
            ExecuteSave();
        }

        private void saveAsToolStripButton_Click(object sender, EventArgs e)
        {
            ExecuteSaveAs();
        }

        private void cropToolStripButton_Click(object sender, EventArgs e)
        {
            ExecuteCrop();
        }

        private void replaceToolStripButton_Click(object sender, EventArgs e)
        {
            ExecuteReplaceWithBackground();
        }


    }
}
