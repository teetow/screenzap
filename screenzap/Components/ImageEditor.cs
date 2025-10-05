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

        private UndoRedo undoStack;

    private const string WindowTitleBase = "Screenzap Image Editor";

    private DateTime? bufferTimestamp;
    private string currentSavePath;
    private bool isPlaceholderImage;

    private bool HasEditableImage => pictureBox1.Image != null && !isPlaceholderImage;

    private int ToolbarHeight => mainToolStrip?.Height ?? 0;

    private Size GetCanvasSize() => new Size(ClientSize.Width, Math.Max(0, ClientSize.Height - ToolbarHeight));

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
            pictureBox1.Location = new Point(0, ToolbarHeight);
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
            var canvasSize = GetCanvasSize();

            int newLeft = 0;
            int newTop = ToolbarHeight;

            if (pictureBox1.Width < canvasSize.Width)
            {
                newLeft = (canvasSize.Width - pictureBox1.Width) / 2;
            }

            if (pictureBox1.Height < canvasSize.Height)
            {
                newTop = ToolbarHeight + (canvasSize.Height - pictureBox1.Height) / 2;
            }

            pictureBox1.Location = new Point(newLeft, newTop);
            Invalidate();
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
            var targetPixel = FormCoordToPixel(e.Location.Subtract(pictureBox1.Location));

            var pol = e.Delta / Math.Abs(e.Delta);

            ZoomLevel = pol > 0 ? FindZoomIn(ZoomLevel) : FindZoomOut(ZoomLevel);

            var newTargetPixel = FormCoordToPixel(e.Location.Subtract(pictureBox1.Location));
            var offset = PixelToFormCoord(targetPixel.Subtract(newTargetPixel));
            pictureBox1.Location = pictureBox1.Location.Subtract(offset);

            var canvasSize = GetCanvasSize();
            if (pictureBox1.Width <= canvasSize.Width && pictureBox1.Height <= canvasSize.Height)
            {
                HandleResize();
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

        private void CensorSelection()
        {
            if (Selection.IsEmpty || pictureBox1.Image == null)
            {
                return;
            }

            var clampedSelection = ClampToImage(Selection);
            if (clampedSelection.Width <= 0 || clampedSelection.Height <= 0)
            {
                return;
            }

            var selectionBefore = Selection;
            var before = CaptureRegion(clampedSelection);
            if (before == null)
            {
                return;
            }

            try
            {
                using (var smearRow = new Bitmap(clampedSelection.Width, 1, PixelFormat.Format32bppArgb))
                {
                    using (var gRow = Graphics.FromImage(smearRow))
                    {
                        int sampleOffset = clampedSelection.Height > 1 ? clampedSelection.Height / 2 : 0;
                        sampleOffset = Math.Max(0, Math.Min(clampedSelection.Height - 1, sampleOffset));
                        var sourceRect = new Rectangle(clampedSelection.Left, clampedSelection.Top + sampleOffset, clampedSelection.Width, 1);
                        gRow.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        gRow.DrawImage(pictureBox1.Image, new Rectangle(Point.Empty, smearRow.Size), sourceRect, GraphicsUnit.Pixel);
                    }

                    using (var gImg = Graphics.FromImage(pictureBox1.Image))
                    {
                        gImg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        gImg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        gImg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        gImg.DrawImage(smearRow, clampedSelection);
                    }
                }

                var after = CaptureRegion(clampedSelection);
                if (after == null)
                {
                    before.Dispose();
                    return;
                }

                PushUndoStep(clampedSelection, before, after, selectionBefore, Selection);
                pictureBox1.Invalidate();
                UpdateCommandUI();
            }
            catch
            {
                before.Dispose();
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

            else if (e.KeyCode == Keys.C)
            {
                if (e.Control == true)
                {
                    var bmp = new Bitmap(Selection.Width, Selection.Height);
                    var gr = Graphics.FromImage(bmp);
                    gr.DrawImage(pictureBox1.Image, Selection.X * -1, Selection.Y * -1);
                    Clipboard.SetImage(bmp);
                    ClearSelection();
                }
                else
                {
                    CensorSelection();

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


    }
}
