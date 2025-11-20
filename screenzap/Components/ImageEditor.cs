using screenzap.lib;
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
using System.Windows.Forms;
using TextDetection;
using FontAwesome.Sharp;

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

    enum DrawingTool
    {
        None,
        Arrow,
        Rectangle
    }

    enum AnnotationHandle
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

    enum AnnotationType
    {
        Arrow,
        Rectangle
    }

    internal sealed class AnnotationShape
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public AnnotationType Type { get; init; }
        public Point Start { get; set; }
        public Point End { get; set; }
        public bool Selected { get; set; }

        public AnnotationShape Clone()
        {
            return new AnnotationShape
            {
                Id = Id,
                Type = Type,
                Start = Start,
                End = End,
                Selected = Selected
            };
        }

        public Rectangle GetBounds()
        {
            return RectangleExt.fromPoints(Start, End);
        }

        public bool IsValid()
        {
            if (Type == AnnotationType.Arrow)
            {
                return !Start.Equals(End);
            }

            var bounds = GetBounds();
            return bounds.Width > 1 && bounds.Height > 1;
        }
    }


    public partial class ImageEditor : Form
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                Image? imgData = Clipboard.GetImage();
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

        private sealed class CensorRegion
        {
            public CensorRegion(Rectangle bounds, float confidence)
            {
                Bounds = bounds;
                Confidence = confidence;
            }

            public Rectangle Bounds { get; }

            public float Confidence { get; }

            public bool Selected { get; set; }
        }

    private readonly UndoRedo undoStack = new UndoRedo();
    private readonly List<CensorRegion> censorRegions = new List<CensorRegion>();
    private bool isCensorToolActive;
    private bool suppressConfidenceEvents;
    private float currentConfidenceThreshold;
    private Bitmap? censorPreviewBuffer;
    private readonly List<AnnotationShape> annotationShapes = new List<AnnotationShape>();
    private DrawingTool activeDrawingTool = DrawingTool.None;
    private bool isDrawingAnnotation;
    private AnnotationShape? workingAnnotation;
    private AnnotationShape? selectedAnnotation;
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

        private const string WindowTitleBase = "Screenzap Image Editor";

        private DateTime? bufferTimestamp;
    private string? currentSavePath;
        private bool isPlaceholderImage;

        private bool HasEditableImage => pictureBox1.Image != null && !isPlaceholderImage;

        private int ToolbarHeight
        {
            get
            {
                int height = mainToolStrip?.Height ?? 0;
                if (censorToolStrip != null && censorToolStrip.Visible)
                {
                    height += censorToolStrip.Height;
                }

                return height;
            }
        }

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

                var captionFont = SystemFonts.CaptionFont ?? SystemFonts.DefaultFont;
                gr.DrawString("No image data in clipboard", captionFont, Brushes.White, new PointF(320, 100), sf);

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

        private PointF PixelToFormCoordF(Point pt)
        {
            float scale = (float)ZoomLevel;
            return new PointF(pt.X * scale, pt.Y * scale);
        }

        private RectangleF PixelToFormCoordF(Rectangle rect)
        {
            float scale = (float)ZoomLevel;
            return new RectangleF(rect.Left * scale, rect.Top * scale, rect.Width * scale, rect.Height * scale);
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
            ConfigureToolbarIcons();

            MouseWheel += ImageEditor_MouseWheel;
            pictureBox1.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

            ClearSelection();

            if (censorToolStrip != null)
            {
                censorToolStrip.Visible = false;
            }

            UpdateCensorToolbarState();
            UpdateDrawingToolButtons();
        }

        private void ConfigureToolbarIcons()
        {
            ConfigureIconButton(saveToolStripButton, IconChar.FloppyDisk);
            ConfigureIconButton(saveAsToolStripButton, IconChar.FilePen);
            ConfigureIconButton(cropToolStripButton, IconChar.CropSimple);
            ConfigureIconButton(replaceToolStripButton, IconChar.Eraser);
            ConfigureIconButton(arrowToolStripButton, IconChar.ArrowRightLong);
            ConfigureIconButton(rectangleToolStripButton, IconChar.VectorSquare);
            ConfigureIconButton(censorToolStripButton, IconChar.UserSecret);
            ConfigureIconButton(copyClipboardToolStripButton, IconChar.Copy);
            ConfigureIconButton(selectAllToolStripButton, IconChar.ObjectGroup);
            ConfigureIconButton(selectNoneToolStripButton, IconChar.SquareXmark);
            ConfigureIconButton(applyCensorToolStripButton, IconChar.Check);
            ConfigureIconButton(cancelCensorToolStripButton, IconChar.Xmark);
        }

        private static void ConfigureIconButton(IconToolStripButton? button, IconChar icon)
        {
            if (button == null)
            {
                return;
            }

            button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            button.IconChar = icon;
            button.IconColor = SystemColors.ControlText;
            button.IconFont = IconFont.Auto;
            button.IconSize = 18;
            button.ImageScaling = ToolStripItemImageScaling.None;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(2, 0, 2, 0);
        }

        public ImageEditor()
        {
            Init();
            ShowPlaceholder();
        }

        public ImageEditor(Image image)
        {
            ArgumentNullException.ThrowIfNull(image);
            Init();
            LoadImage(image);
        }
        internal void LoadImage(Image? imgData)
        {
            LoadImage(imgData, false);
        }

        internal void LoadImage(Image? imgData, bool treatAsPlaceholder)
        {
            if (imgData == null)
                return;

            isPlaceholderImage = treatAsPlaceholder;
            bufferTimestamp = treatAsPlaceholder ? (DateTime?)null : (ClipboardMetadata.LastCaptureTimestamp ?? DateTime.Now);
            currentSavePath = null;

            DeactivateCensorTool(false);
            ClearSelection();
            ResetZoom();
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
            UpdateDrawingToolButtons();

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

            if (Visible)
            {
                BeginInvoke(new Action(() =>
                {
                    Focus();
                    pictureBox1?.Focus();
                }));
            }
        }

        internal void ShowAndFocus()
        {
            if (!Visible)
            {
                Show();
            }
            else if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            BringToFront();
            Activate();
            Focus();
            pictureBox1?.Focus();
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

        private List<AnnotationShape> CloneAnnotations()
        {
            return annotationShapes.Select(a => a.Clone()).ToList();
        }

        private void SelectAnnotation(AnnotationShape? target)
        {
            foreach (var annotation in annotationShapes)
            {
                annotation.Selected = annotation == target;
            }

            selectedAnnotation = target;
            pictureBox1?.Invalidate();
        }

        private void SyncSelectedAnnotation()
        {
            selectedAnnotation = annotationShapes.FirstOrDefault(a => a.Selected);
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
        }

        private void ToggleDrawingTool(DrawingTool tool)
        {
            if (!HasEditableImage)
            {
                return;
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

            float scale = surface == AnnotationSurface.Screen ? (float)ZoomLevel : 1f;
            float strokeWidth = Math.Max(1f, 2f * scale);

            var previousSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            try
            {
                foreach (var annotation in annotationShapes)
                {
                    DrawAnnotationShape(graphics, annotation, surface, strokeWidth);
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

        private void DrawAnnotationShape(Graphics graphics, AnnotationShape annotation, AnnotationSurface surface, float strokeWidth)
        {
            var penColor = annotation.Selected ? Color.OrangeRed : Color.Red;
            using var pen = new Pen(penColor, strokeWidth)
            {
                Alignment = System.Drawing.Drawing2D.PenAlignment.Center
            };

            if (annotation.Type == AnnotationType.Arrow)
            {
                float arrowScale = surface == AnnotationSurface.Screen ? (float)ZoomLevel : 1f;
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

        private void DrawAnnotationHandles(Graphics graphics, AnnotationShape annotation)
        {
            if (!annotation.Selected)
            {
                return;
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

            var handle = HitTestAnnotationHandle(formPoint);
            if (handle != AnnotationHandle.None)
            {
                annotationSnapshotBeforeEdit = CloneAnnotations();
                activeAnnotationHandle = handle;
                annotationDragOriginPixel = pixelPoint;
                annotationChangedDuringDrag = false;
                return true;
            }

            if (activeDrawingTool != DrawingTool.None)
            {
                annotationSnapshotBeforeEdit = CloneAnnotations();
                var annotationType = activeDrawingTool == DrawingTool.Arrow ? AnnotationType.Arrow : AnnotationType.Rectangle;
                var clampedPoint = ClampPointToImage(pixelPoint);
                workingAnnotation = new AnnotationShape
                {
                    Type = annotationType,
                    Start = clampedPoint,
                    End = clampedPoint,
                    Selected = true
                };
                annotationShapes.Add(workingAnnotation);
                SelectAnnotation(workingAnnotation);
                isDrawingAnnotation = true;
                annotationDraftAnchorPixel = clampedPoint;
                annotationTranslateModeActive = false;
                annotationChangedDuringDrag = false;
                return true;
            }

            var hit = HitTestAnnotation(pixelPoint, formPoint);
            if (hit != null)
            {
                SelectAnnotation(hit);
                annotationSnapshotBeforeEdit = CloneAnnotations();
                activeAnnotationHandle = AnnotationHandle.Move;
                annotationDragOriginPixel = pixelPoint;
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
                    if (annotationTranslateModeActive)
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
                    Cursor = Cursors.Cross;
                    return true;
                }

                var hit = HitTestAnnotation(pixelPoint, formPoint);
                if (hit != null)
                {
                    Cursor = Cursors.Hand;
                    return true;
                }
            }

            return false;
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

            switch (activeAnnotationHandle)
            {
                case AnnotationHandle.Move:
                    var delta = clamped.Subtract(annotationDragOriginPixel);
                    if (delta.X == 0 && delta.Y == 0)
                    {
                        return;
                    }

                    if (pictureBox1.Image != null)
                    {
                        var bounds = GetImageBounds();
                        int minX = Math.Min(target.Start.X, target.End.X);
                        int maxX = Math.Max(target.Start.X, target.End.X);
                        int minY = Math.Min(target.Start.Y, target.End.Y);
                        int maxY = Math.Max(target.Start.Y, target.End.Y);

                        delta.X = Math.Max(bounds.Left - minX, Math.Min(bounds.Right - maxX, delta.X));
                        delta.Y = Math.Max(bounds.Top - minY, Math.Min(bounds.Bottom - maxY, delta.Y));
                    }

                    if (delta.X == 0 && delta.Y == 0)
                    {
                        annotationDragOriginPixel = clamped;
                        return;
                    }

                    target.Start = target.Start.Add(delta);
                    target.End = target.End.Add(delta);
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

        private void CompleteAnnotationDraft(Point pixelPoint)
        {
            if (!isDrawingAnnotation || workingAnnotation == null)
            {
                return;
            }

            workingAnnotation.End = ClampPointToImage(pixelPoint);
            if (workingAnnotation.Type == AnnotationType.Rectangle)
            {
                NormalizeRectangleAnnotation(workingAnnotation);
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
            if (annotationSnapshotBeforeEdit == null)
            {
                return;
            }

            var afterState = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, null, null, Selection, Selection, false, annotationSnapshotBeforeEdit, afterState);
            annotationSnapshotBeforeEdit = null;
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

        private void ReleaseCensorPreviewBuffer()
        {
            var existing = censorPreviewBuffer;
            censorPreviewBuffer = null;
            existing?.Dispose();
        }

        private void ShowCensorProgressIndicator()
        {
            UseWaitCursor = true;
            if (censorProgressBar != null)
            {
                censorProgressBar.Visible = true;
            }
            Application.DoEvents();
        }

        private void HideCensorProgressIndicator()
        {
            UseWaitCursor = false;
            if (censorProgressBar != null)
            {
                censorProgressBar.Visible = false;
            }
        }

        private bool BuildCensorPreviewBuffer()
        {
            ReleaseCensorPreviewBuffer();

            if (pictureBox1.Image == null || censorRegions.Count == 0)
            {
                return false;
            }

            var working = new Bitmap(pictureBox1.Image.Width, pictureBox1.Image.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(working))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(pictureBox1.Image, Point.Empty);
            }

            using (var bufferGraphics = Graphics.FromImage(working))
            {
                bufferGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                bufferGraphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                foreach (var region in censorRegions)
                {
                    var clamped = ClampToImage(region.Bounds);
                    if (clamped.Width <= 0 || clamped.Height <= 0)
                    {
                        continue;
                    }

                    var regionSnapshot = CaptureRegion(clamped);
                    if (regionSnapshot == null)
                    {
                        continue;
                    }

                    using (regionSnapshot)
                    using (var scrambled = GenerateCensoredBitmap(regionSnapshot, clamped))
                    {
                        bufferGraphics.DrawImage(scrambled, clamped);
                    }
                }
            }

            censorPreviewBuffer = working;
            return true;
        }

        private bool ActivateCensorTool()
        {
            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return false;
            }

            ShowCensorProgressIndicator();
            var previousCursor = Cursor.Current;
            var hasSelection = !Selection.IsEmpty;
            var detectionZone = hasSelection ? ClampToImage(Selection) : GetImageBounds();
            if (hasSelection && (detectionZone.Width <= 0 || detectionZone.Height <= 0))
            {
                HideCensorProgressIndicator();
                return false;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;

                if (hasSelection)
                {
                    using var selectionBitmap = CaptureRegion(detectionZone);
                    if (selectionBitmap == null)
                    {
                        MessageBox.Show(this, "Failed to capture the selected region.", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        censorRegions.Clear();
                        ReleaseCensorPreviewBuffer();
                        UpdateCensorToolbarState();
                        return false;
                    }

                    var detected = TextRegionDetector.FindTextRegionsDetailed(selectionBitmap);
                    Rectangle refinedBounds;
                    float combinedConfidence = 0f;

                    if (detected.Count > 0)
                    {
                        refinedBounds = detected[0].Bounds;
                        combinedConfidence = detected[0].Confidence;

                        for (int i = 1; i < detected.Count; i++)
                        {
                            refinedBounds = Rectangle.Union(refinedBounds, detected[i].Bounds);
                            combinedConfidence = Math.Max(combinedConfidence, detected[i].Confidence);
                        }
                    }
                    else
                    {
                        refinedBounds = new Rectangle(Point.Empty, selectionBitmap.Size);
                    }

                    if (refinedBounds.Width <= 0 || refinedBounds.Height <= 0)
                    {
                        MessageBox.Show(this, "No text regions were detected inside the selection.", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        censorRegions.Clear();
                        ReleaseCensorPreviewBuffer();
                        UpdateCensorToolbarState();
                        return false;
                    }

                    var translated = new Rectangle(
                        detectionZone.Left + refinedBounds.Left,
                        detectionZone.Top + refinedBounds.Top,
                        refinedBounds.Width,
                        refinedBounds.Height);

                    translated = ClampToImage(translated);
                    if (translated.Width <= 0 || translated.Height <= 0)
                    {
                        MessageBox.Show(this, "No text regions were detected inside the selection.", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        censorRegions.Clear();
                        ReleaseCensorPreviewBuffer();
                        UpdateCensorToolbarState();
                        return false;
                    }

                    censorRegions.Clear();
                    censorRegions.Add(new CensorRegion(translated, combinedConfidence));
                }
                else
                {
                    using (var detectionSource = new Bitmap(pictureBox1.Image))
                    {
                        var detected = TextRegionDetector.FindTextRegionsDetailed(detectionSource);
                        if (detected.Count == 0)
                        {
                            MessageBox.Show(this, "No text regions were detected.", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            censorRegions.Clear();
                            ReleaseCensorPreviewBuffer();
                            UpdateCensorToolbarState();
                            return false;
                        }

                        censorRegions.Clear();
                        foreach (var region in detected)
                        {
                            float confidence = float.IsNaN(region.Confidence) ? 0f : Math.Max(0f, region.Confidence);
                            censorRegions.Add(new CensorRegion(region.Bounds, confidence));
                        }
                    }
                }

                BuildCensorPreviewBuffer();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to detect text regions.\n{ex.Message}", "Censor Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                censorRegions.Clear();
                ReleaseCensorPreviewBuffer();
                UpdateCensorToolbarState();
                return false;
            }
            finally
            {
                HideCensorProgressIndicator();
                Cursor.Current = previousCursor;
            }

            foreach (var region in censorRegions)
            {
                region.Selected = false;
            }

            isCensorToolActive = true;
            currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar?.Value ?? 0);
            suppressConfidenceEvents = true;

            if (confidenceTrackBar != null)
            {
                confidenceTrackBar.Value = confidenceTrackBar.Minimum;
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
            }

            suppressConfidenceEvents = false;

            if (censorToolStrip != null)
            {
                censorToolStrip.Visible = true;
            }

            UpdateCensorToolbarState();
            ClearSelection();
            pictureBox1.Invalidate();
            return true;
        }

        private void DeactivateCensorTool(bool applySelections)
        {
            HideCensorProgressIndicator();

            if (applySelections && isCensorToolActive)
            {
                ApplyCensorRegions();
            }

            isCensorToolActive = false;
            censorRegions.Clear();
            ReleaseCensorPreviewBuffer();
            currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar?.Value ?? 0);
            Cursor = Cursors.Default;

            suppressConfidenceEvents = true;
            if (confidenceTrackBar != null)
            {
                confidenceTrackBar.Value = confidenceTrackBar.Minimum;
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
                confidenceTrackBar.Enabled = false;
            }
            suppressConfidenceEvents = false;

            if (censorToolStrip != null)
            {
                censorToolStrip.Visible = false;
            }

            UpdateCensorToolbarState();
            ClearSelection();
            pictureBox1.Invalidate();
            UpdateCommandUI();
        }

        private void ApplyCensorRegions()
        {
            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return;
            }

            var selectedRegions = new List<Rectangle>();
            foreach (var region in censorRegions.Where(r => r.Selected))
            {
                var clamped = ClampToImage(region.Bounds);
                if (clamped.Width > 0 && clamped.Height > 0)
                {
                    selectedRegions.Add(clamped);
                }
            }

            if (selectedRegions.Count == 0)
            {
                return;
            }

            var previousSelection = Selection;

            Rectangle combinedRegion = selectedRegions[0];
            for (int i = 1; i < selectedRegions.Count; i++)
            {
                combinedRegion = Rectangle.Union(combinedRegion, selectedRegions[i]);
            }

            combinedRegion = ClampToImage(combinedRegion);
            var beforeSnapshot = CaptureRegion(combinedRegion);
            if (beforeSnapshot == null)
            {
                return;
            }

            Bitmap? afterSnapshot = null;

            try
            {
                using (var gImg = Graphics.FromImage(pictureBox1.Image))
                {
                    gImg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    gImg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    gImg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;

                    if (censorPreviewBuffer != null)
                    {
                        foreach (var regionBounds in selectedRegions)
                        {
                            gImg.DrawImage(censorPreviewBuffer, regionBounds, regionBounds, GraphicsUnit.Pixel);
                        }
                    }
                    else
                    {
                        foreach (var regionBounds in selectedRegions)
                        {
                            using var beforeRegion = CaptureRegion(regionBounds);
                            if (beforeRegion == null)
                            {
                                continue;
                            }

                            using var afterRegion = GenerateCensoredBitmap(beforeRegion, regionBounds);
                            gImg.DrawImage(afterRegion, regionBounds);
                        }
                    }
                }

                afterSnapshot = CaptureRegion(combinedRegion);
                if (afterSnapshot == null)
                {
                    return;
                }

                Selection = previousSelection;
                PushUndoStep(combinedRegion, beforeSnapshot, afterSnapshot, previousSelection, Selection);
                pictureBox1.Invalidate();
                UpdateCommandUI();
                beforeSnapshot = null;
                afterSnapshot = null;
            }
            finally
            {
                Selection = previousSelection;
                beforeSnapshot?.Dispose();
                afterSnapshot?.Dispose();
            }
        }

        private float CalculateConfidenceThreshold(int sliderValue)
        {
            var normalized = Math.Clamp(sliderValue / 100f, 0f, 1f);
            return 1f - normalized;
        }

        private void UpdateCensorToolbarState()
        {
            int sliderValue = confidenceTrackBar?.Value ?? 0;
            var threshold = CalculateConfidenceThreshold(sliderValue);
            if (confidenceValueLabel != null)
            {
                int thresholdPercent = (int)Math.Round(threshold * 100f, MidpointRounding.AwayFromZero);
                confidenceValueLabel.Text = "≥ " + thresholdPercent.ToString(CultureInfo.InvariantCulture) + "%";
            }

            bool anyRegions = censorRegions.Count > 0;
            bool anySelected = censorRegions.Any(r => r.Selected);

            if (selectAllToolStripButton != null)
            {
                selectAllToolStripButton.Enabled = anyRegions;
            }

            if (selectNoneToolStripButton != null)
            {
                selectNoneToolStripButton.Enabled = anyRegions;
            }

            if (applyCensorToolStripButton != null)
            {
                applyCensorToolStripButton.Enabled = anySelected;
            }

            if (confidenceTrackBar != null)
            {
                confidenceTrackBar.Enabled = isCensorToolActive && anyRegions;
            }

            if (confidenceToolStripHost != null)
            {
                confidenceToolStripHost.Enabled = isCensorToolActive && anyRegions;
            }
        }

        private CensorRegion? FindRegionAtPixel(Point pixel)
        {
            for (int i = censorRegions.Count - 1; i >= 0; i--)
            {
                if (censorRegions[i].Bounds.Contains(pixel))
                {
                    return censorRegions[i];
                }
            }

            return null;
        }

        private void confidenceTrackBar_ValueChanged(object? sender, EventArgs e)
        {
            if (confidenceTrackBar == null)
            {
                return;
            }

            if (suppressConfidenceEvents)
            {
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
                UpdateCensorToolbarState();
                return;
            }

            if (!isCensorToolActive)
            {
                currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);
                UpdateCensorToolbarState();
                return;
            }

            currentConfidenceThreshold = CalculateConfidenceThreshold(confidenceTrackBar.Value);

            foreach (var region in censorRegions)
            {
                float confidence = float.IsNaN(region.Confidence) ? 0f : region.Confidence;
                bool meetsThreshold = confidence >= currentConfidenceThreshold;

                if (confidence <= 0f && currentConfidenceThreshold <= 0f)
                {
                    meetsThreshold = true;
                }

                region.Selected = meetsThreshold;
            }

            UpdateCensorToolbarState();
            pictureBox1.Invalidate();
        }

        private void selectAllToolStripButton_Click(object? sender, EventArgs e)
        {
            if (!isCensorToolActive)
            {
                return;
            }

            foreach (var region in censorRegions)
            {
                region.Selected = true;
            }

            UpdateCensorToolbarState();
            pictureBox1.Invalidate();
        }

        private void selectNoneToolStripButton_Click(object? sender, EventArgs e)
        {
            if (!isCensorToolActive)
            {
                return;
            }

            foreach (var region in censorRegions)
            {
                region.Selected = false;
            }

            UpdateCensorToolbarState();
            pictureBox1.Invalidate();
        }

        private void applyCensorToolStripButton_Click(object? sender, EventArgs e)
        {
            DeactivateCensorTool(true);
        }

        private void cancelCensorToolStripButton_Click(object? sender, EventArgs e)
        {
            DeactivateCensorTool(false);
        }

        private Bitmap? CaptureRegion(Rectangle region)
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

        private void PushUndoStep(Rectangle region, Bitmap? before, Bitmap? after, Rectangle selectionBefore, Rectangle selectionAfter, bool replacesImage = false, List<AnnotationShape>? shapesBefore = null, List<AnnotationShape>? shapesAfter = null)
        {
            bool hasBitmapChange = before != null && after != null;
            bool hasShapeChange = shapesBefore != null && shapesAfter != null;

            if (!hasBitmapChange && !hasShapeChange)
            {
                before?.Dispose();
                after?.Dispose();
                return;
            }

            if (hasBitmapChange && !replacesImage && (region.Width <= 0 || region.Height <= 0))
            {
                before?.Dispose();
                after?.Dispose();
                return;
            }

            undoStack.Push(new ImageUndoStep(region, before, after, selectionBefore, selectionAfter, replacesImage, shapesBefore, shapesAfter));
        }

        private void ApplyUndoStep(ImageUndoStep step, bool applyAfterState)
        {
            if (step == null)
            {
                return;
            }

            var source = applyAfterState ? step.After : step.Before;
            var shapeState = applyAfterState ? step.ShapesAfter : step.ShapesBefore;

            if (source != null)
            {
                if (pictureBox1.Image == null)
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
            }

            Selection = applyAfterState ? step.SelectionAfter : step.SelectionBefore;
            ApplyAnnotationState(shapeState);
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

        private void ImageEditor_MouseWheel(object? sender, MouseEventArgs e)
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
            if (HandleAnnotationMouseMove(cursorPixel, e.Location, e.Button))
            {
                base.OnMouseMove(e);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (rzMode != ResizeMode.None)
                {
                    // we're currently resizing the selection
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

            if (isCensorToolActive)
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

            Bitmap? after = null;

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

                int rngSeed = HashCode.Combine(selectionBounds.Left, selectionBounds.Top, selectionBounds.Width, selectionBounds.Height);
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
            var annotationStateBefore = CloneAnnotations();

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

            ApplyCropToAnnotations(clampedSelection.Location, clampedSelection.Size);
            var annotationStateAfter = CloneAnnotations();

            PushUndoStep(Rectangle.Empty, beforeImage, afterSnapshot, selectionBefore, selectionAfter, true, annotationStateBefore, annotationStateAfter);

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
            if (censorToolStripButton != null)
            {
                censorToolStripButton.Enabled = enable;
            }
            if (copyClipboardToolStripButton != null)
            {
                copyClipboardToolStripButton.Enabled = enable;
            }

            UpdateDrawingToolButtons();
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

        private Bitmap BuildCompositeImage()
        {
            if (pictureBox1.Image == null)
            {
                throw new InvalidOperationException("No image is currently loaded.");
            }

            var composite = new Bitmap(pictureBox1.Image);

            if (annotationShapes.Count == 0)
            {
                return composite;
            }

            using (var graphics = Graphics.FromImage(composite))
            {
                DrawAnnotations(graphics, AnnotationSurface.Image);
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

                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                if (isDrawingAnnotation || selectedAnnotation != null || activeDrawingTool != DrawingTool.None)
                {
                    CancelAnnotationPreview();
                    SelectAnnotation(null);
                    activeDrawingTool = DrawingTool.None;
                    UpdateDrawingToolButtons();
                    pictureBox1.Invalidate();
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
                pictureBox1.Invalidate();
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
                    MoveInPixel = MouseOutPixel;
                }

                return;
            }

            else if (e.KeyCode == Keys.C && e.Control == true)
            {
                if (!Selection.IsEmpty && HasEditableImage && pictureBox1.Image != null)
                {
                    using var composite = BuildCompositeImage();
                    var bmp = new Bitmap(Selection.Width, Selection.Height);
                    using (var gr = Graphics.FromImage(bmp))
                    {
                        gr.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        gr.DrawImage(composite, new Rectangle(Point.Empty, bmp.Size), Selection, GraphicsUnit.Pixel);
                    }

                    Clipboard.SetImage(bmp);
                    ClearSelection();
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
                annotationTranslateModeActive = false;
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

        private void censorToolStripButton_Click(object sender, EventArgs e)
        {
            if (isCensorToolActive)
            {
                pictureBox1?.Focus();
                return;
            }

            if (ActivateCensorTool())
            {
                pictureBox1?.Focus();
            }
        }

        private bool CopyImageToClipboard()
        {
            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return false;
            }

            try
            {
                using var snapshot = BuildCompositeImage();
                Clipboard.SetImage(snapshot);
                ClipboardMetadata.LastCaptureTimestamp = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to copy the image to the clipboard.\n{ex.Message}", "Clipboard Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void copyClipboardToolStripButton_Click(object sender, EventArgs e)
        {
            if (CopyImageToClipboard())
            {
                pictureBox1?.Focus();
            }
        }

        private void arrowToolStripButton_Click(object sender, EventArgs e)
        {
            ToggleDrawingTool(DrawingTool.Arrow);
        }

        private void rectangleToolStripButton_Click(object sender, EventArgs e)
        {
            ToggleDrawingTool(DrawingTool.Rectangle);
        }


    }
}
