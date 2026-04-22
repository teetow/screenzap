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
using System.Threading.Tasks;
using System.Windows.Forms;
using FontAwesome.Sharp;
using TextDetection;
using screenzap.Components;
using screenzap.Components.Shared;
using screenzap.lib;

namespace screenzap
{
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

    public partial class ImageEditor : Form, IClipboardDocumentPresenter
    {
        internal Func<TextEditor>? RequestTextEditor { get; set; }
        private EditorHostServices? hostServices;
        private bool isHostedView;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                HandleClipboardUpdated();
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

    private readonly UndoRedo undoStack = new UndoRedo();

        private const string WindowTitleBase = "Screenzap Image Editor";
        private const int OptimizeTextBlurRadius = 100;
        private const int ExpandCanvasPaddingPixels = 8;
        private const int InternalClipboardSuppressionMilliseconds = 1500;

        private DateTime? bufferTimestamp;
    private string? currentSavePath;
        private bool isPlaceholderImage;
    private bool hasUnsavedChanges;
    /// <summary>Invoked when the editor's content is dirtied (e.g. by an edit, tool apply or undo push).</summary>
    internal Action? ContentEditedCallback;
    private void MarkDirtyAndNotify()
    {
        hasUnsavedChanges = true;
        try { ContentEditedCallback?.Invoke(); }
        catch (Exception ex) { lib.Logger.Log($"ContentEditedCallback threw: {ex.Message}"); }
    }
    private bool clipboardHasPendingReload;
        private string? expectedInternalClipboardSignature;
            private DateTime? suppressClipboardAutoReloadUntilUtc;
        private Bitmap? internalClipboardImage;
        private ToolStrip? toolsToolStrip;
        private ToolStrip? textOptionsToolStrip;
        private ToolStrip? annotationOptionsToolStrip;
        private ClipboardReloadTarget pendingReloadTarget = ClipboardReloadTarget.None;
        internal Func<bool>? ConfirmReloadWhenDirtyOverrideForDiagnostics { get; set; }
        internal Func<Image?>? ClipboardImageProviderForDiagnostics { get; set; }
        internal Func<string?>? ClipboardTextProviderForDiagnostics { get; set; }
    internal Func<Image, bool>? ClipboardImageWriterForDiagnostics { get; set; }

        private bool HasEditableImage => pictureBox1.Image != null && !isPlaceholderImage;
        internal ViewportMetrics ViewportDiagnostics => pictureBox1?.Metrics ?? default;
        private enum ClipboardReloadTarget
        {
            None,
            Image,
            Text
        }

        private int ToolbarHeight
        {
            get
            {
                int height = mainToolStrip?.Height ?? 0;
                if (censorToolStrip != null && censorToolStrip.Visible)
                {
                    height += censorToolStrip.Height;
                }
                if (straightenToolStrip != null && straightenToolStrip.Visible)
                {
                    height += straightenToolStrip.Height;
                }

                return height;
            }
        }

        private void ResizeWindowToImage(Size imageSize)
        {
            if (isHostedView)
            {
                return;
            }

            var toolbarHeight = ToolbarHeight;
            var toolsWidth = toolsToolStrip?.Width ?? 0;
            var secondaryToolbarPreferredWidth = Math.Max(censorToolStrip?.PreferredSize.Width ?? 0, straightenToolStrip?.PreferredSize.Width ?? 0);
            var toolbarPreferredWidth = Math.Max(mainToolStrip?.PreferredSize.Width ?? 0, secondaryToolbarPreferredWidth);
            toolbarPreferredWidth = Math.Max(toolbarPreferredWidth, toolsWidth + 200);

            var targetWidth = Math.Max(Math.Max(imageSize.Width + toolsWidth, MinimumSize.Width), toolbarPreferredWidth);
            var targetHeight = Math.Max(imageSize.Height + toolbarHeight, MinimumSize.Height);
            ClientSize = new Size(targetWidth, targetHeight);
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
            return pictureBox1?.PixelToClient(pt) ?? pt;
        }

        private Rectangle PixelToFormCoord(Rectangle rect)
        {
            return pictureBox1?.PixelToClient(rect) ?? rect;
        }

        private PointF PixelToFormCoordF(Point pt)
        {
            return pictureBox1?.PixelToClientF(pt) ?? new PointF(pt.X, pt.Y);
        }

        private RectangleF PixelToFormCoordF(Rectangle rect)
        {
            return pictureBox1?.PixelToClientF(rect) ?? new RectangleF(rect.Location, rect.Size);
        }

        private Point FormCoordToPixel(Point pt)
        {
            return pictureBox1?.ClientToPixel(pt) ?? pt;
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
            InitializeToolbarLayout();
            ConfigureToolbarIcons();

            MouseWheel += ImageEditor_MouseWheel;
            pictureBox1.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            pictureBox1.ZoomChanged += pictureBox1_ZoomChanged;
            pictureBox1.MouseDoubleClick += pictureBox1_MouseDoubleClick;

            ClearSelection();

            if (censorToolStrip != null)
            {
                censorToolStrip.Visible = false;
            }

            if (straightenToolStrip != null)
            {
                straightenToolStrip.Visible = false;
            }

            if (textOptionsToolStrip != null)
            {
                textOptionsToolStrip.Visible = false;
            }

            if (annotationOptionsToolStrip != null)
            {
                annotationOptionsToolStrip.Visible = false;
            }

            UpdateCensorToolbarState();
            UpdateDrawingToolButtons();
            UpdateTextToolButtons();
            PositionOverlayToolStrips();
        }
        
        private void canvasPanel_SizeChanged(object? sender, EventArgs e)
        {
            HandleResize();
        }

        private void ImageEditor_ClientSizeChanged(object? sender, EventArgs e)
        {
            HandleResize();
        }

        private void pictureBox1_SizeChanged(object? sender, EventArgs e)
        {
            HandleResize();
        }

        private void ConfigureToolbarIcons()
        {
            ConfigureIconButton(saveToolStripButton, IconChar.FloppyDisk);
            ConfigureIconButton(saveAsToolStripButton, IconChar.FilePen);
            ConfigureIconButton(cropToolStripButton, IconChar.CropSimple);
            ConfigureIconButton(expandCanvasToolStripButton, IconChar.Expand);
            ConfigureIconButton(flipHorizontalToolStripButton, IconChar.LeftRight);
            ConfigureIconButton(flipVerticalToolStripButton, IconChar.UpDown);
            ConfigureIconButton(rotateToolStripButton, IconChar.ArrowRotateRight);
            ConfigureIconButton(replaceToolStripButton, IconChar.Eraser);
            ConfigureIconButton(optimizeTextToolStripButton, IconChar.Magic);
            ConfigureIconButton(straightenToolStripButton, IconChar.Rotate);
            ConfigureIconButton(arrowToolStripButton, IconChar.ArrowRightLong);
            ConfigureIconButton(rectangleToolStripButton, IconChar.VectorSquare);
            ConfigureIconButton(textToolStripButton, IconChar.Font);
            ConfigureIconButton(censorToolStripButton, IconChar.UserSecret);
            ConfigureIconButton(copyClipboardToolStripButton, IconChar.Copy);
            ConfigureIconButton(reloadToolStripButton, IconChar.Rotate);
            ConfigureIconButton(selectAllToolStripButton, IconChar.ObjectGroup);
            ConfigureIconButton(selectNoneToolStripButton, IconChar.SquareXmark);
            ConfigureIconButton(applyCensorToolStripButton, IconChar.Check);
            ConfigureIconButton(cancelCensorToolStripButton, IconChar.Xmark);
            ConfigureIconButton(straightenApplyButton, IconChar.Check);
            ConfigureIconButton(straightenCancelButton, IconChar.Xmark);
            UpdateReloadIndicator();
            UpdateTraceButtonState();
            InitializeTextToolbar();
            InitializeAnnotationToolbar();
            ConfigureToolRailButtons();
            InitColorCorrector();
        }

        private void ConfigureToolRailButtons()
        {
            foreach (var button in new[] { arrowToolStripButton, rectangleToolStripButton, textToolStripButton, censorToolStripButton, straightenToolStripButton })
            {
                if (button == null)
                {
                    continue;
                }

                button.DisplayStyle = ToolStripItemDisplayStyle.Image;
                button.AutoSize = false;
                button.Size = new Size(32, 32);
                button.Margin = new Padding(2);
                button.Padding = Padding.Empty;
                button.ImageScaling = ToolStripItemImageScaling.None;
            }

            if (toolsToolStrip != null)
            {
                toolsToolStrip.AutoSize = false;
                toolsToolStrip.Width = 40;
            }
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

        private void UpdateReloadIndicator()
        {
            if (reloadToolStripButton == null || reloadNotificationLabel == null)
            {
                return;
            }

            reloadNotificationLabel.Visible = clipboardHasPendingReload && !isHostedView;
            reloadNotificationLabel.Text = clipboardHasPendingReload ? "●" : string.Empty;
            reloadToolStripButton.IconColor = clipboardHasPendingReload ? Color.OrangeRed : SystemColors.ControlText;
            hostServices?.SetReloadIndicator?.Invoke(clipboardHasPendingReload);
        }

        private void ClearClipboardNotification()
        {
            clipboardHasPendingReload = false;
            pendingReloadTarget = ClipboardReloadTarget.None;
            UpdateReloadIndicator();
        }

        private static string BuildTextClipboardSignature(string text)
        {
            return $"text:{text.Length}:{StringComparer.Ordinal.GetHashCode(text)}";
        }

        private static string BuildImageClipboardSignature(Image image)
        {
            Bitmap? ownedBitmap = null;
            var bitmap = image as Bitmap;
            if (bitmap == null)
            {
                ownedBitmap = new Bitmap(image);
                bitmap = ownedBitmap;
            }

            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash ^= (uint)bitmap.Width;
                hash *= 1099511628211UL;
                hash ^= (uint)bitmap.Height;
                hash *= 1099511628211UL;

                int stepX = Math.Max(1, bitmap.Width / 8);
                int stepY = Math.Max(1, bitmap.Height / 8);

                for (int y = 0; y < bitmap.Height; y += stepY)
                {
                    for (int x = 0; x < bitmap.Width; x += stepX)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        hash ^= (uint)pixel.ToArgb();
                        hash *= 1099511628211UL;
                    }
                }

                var signature = $"image:{bitmap.Width}x{bitmap.Height}:{hash:X16}";
                ownedBitmap?.Dispose();
                return signature;
            }
        }

        private string? TryBuildClipboardSignature(ClipboardReloadTarget target)
        {
            try
            {
                if (target == ClipboardReloadTarget.Image)
                {
                    if (!Clipboard.ContainsImage())
                    {
                        return null;
                    }

                    using var image = Clipboard.GetImage();
                    if (image == null)
                    {
                        return null;
                    }

                    return BuildImageClipboardSignature(image);
                }

                if (target == ClipboardReloadTarget.Text)
                {
                    if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
                    {
                        return null;
                    }

                    var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    return BuildTextClipboardSignature(text ?? string.Empty);
                }
            }
            catch (ExternalException)
            {
            }

            return null;
        }

        private void TrackInternalClipboardImageWrite(Image image)
        {
            BeginInternalClipboardWriteSuppression(BuildImageClipboardSignature(image));
        }

        private void TrackInternalClipboardTextWrite(string text)
        {
            BeginInternalClipboardWriteSuppression(BuildTextClipboardSignature(text));
        }

        private void BeginInternalClipboardWriteSuppression(string signature)
        {
            expectedInternalClipboardSignature = signature;
            suppressClipboardAutoReloadUntilUtc = DateTime.UtcNow.AddMilliseconds(InternalClipboardSuppressionMilliseconds);
        }

        private bool IsInternalClipboardSuppressionActive()
        {
            if (suppressClipboardAutoReloadUntilUtc.HasValue && DateTime.UtcNow > suppressClipboardAutoReloadUntilUtc.Value)
            {
                suppressClipboardAutoReloadUntilUtc = null;
                expectedInternalClipboardSignature = null;
                return false;
            }

            return suppressClipboardAutoReloadUntilUtc.HasValue && DateTime.UtcNow <= suppressClipboardAutoReloadUntilUtc.Value;
        }

        private bool IsExpectedInternalClipboardUpdate(ClipboardReloadTarget target)
        {
            if (string.IsNullOrEmpty(expectedInternalClipboardSignature))
            {
                return false;
            }

            var signature = TryBuildClipboardSignature(target);
            if (signature == null)
            {
                return false;
            }

            if (!string.Equals(signature, expectedInternalClipboardSignature, StringComparison.Ordinal))
            {
                return false;
            }

            expectedInternalClipboardSignature = null;
            return true;
        }

        private void HandleClipboardUpdated()
        {
            ClipboardReloadTarget detectedTarget = ClipboardReloadTarget.None;
            try
            {
                if (Clipboard.ContainsImage())
                {
                    detectedTarget = ClipboardReloadTarget.Image;
                }
                else if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    detectedTarget = ClipboardReloadTarget.Text;
                }
            }
            catch (ExternalException)
            {
                return;
            }

            if (detectedTarget == ClipboardReloadTarget.None)
            {
                return;
            }

            if (IsExpectedInternalClipboardUpdate(detectedTarget))
            {
                ClearClipboardNotification();
                return;
            }

            if (IsInternalClipboardSuppressionActive())
            {
                ClearClipboardNotification();
                return;
            }

            pendingReloadTarget = detectedTarget;

            if (!hasUnsavedChanges)
            {
                ReloadFromClipboard(showEmptyClipboardMessage: false);
                return;
            }

            clipboardHasPendingReload = true;
            UpdateReloadIndicator();
        }

        public ImageEditor()
        {
            Init();
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            Dock = DockStyle.Fill;
            ShowPlaceholder();
        }

        public ImageEditor(Image image)
        {
            ArgumentNullException.ThrowIfNull(image);
            Init();
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            Dock = DockStyle.Fill;
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

            LogViewportDebug($"=== LoadImage START: imgData.Size={imgData.Size}, treatAsPlaceholder={treatAsPlaceholder} ===");
            LogViewportDebug($"LoadImage: current pictureBox1.ClientSize={pictureBox1.ClientSize}, panOffset={pictureBox1.Metrics.PanOffset}");

            isPlaceholderImage = treatAsPlaceholder;
            bufferTimestamp = treatAsPlaceholder ? (DateTime?)null : (ClipboardMetadata.LastCaptureTimestamp ?? DateTime.Now);
            currentSavePath = null;
            pendingReloadTarget = ClipboardReloadTarget.None;

            DeactivateCensorTool(false);
            DeactivateStraightenTool(false);
            ClearSelection();
            LogViewportDebug($"LoadImage: About to call ResetZoom");
            ResetZoom();
            LogViewportDebug($"LoadImage: After ResetZoom, panOffset={pictureBox1.Metrics.PanOffset}");
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
            
            // Reset text annotations
            textAnnotations.Clear();
            activeTextAnnotation = null;
            selectedTextAnnotation = null;
            isTextToolActive = false;
            textAnnotationSnapshotBeforeEdit = null;
            textAnnotationChangedDuringDrag = false;
            isTextAnnotationDragging = false;
            
            UpdateDrawingToolButtons();
            UpdateTextToolButtons();
            UpdateTextToolbarVisibility();

            var replacementImage = new Bitmap(imgData);
            var oldImageSize = pictureBox1.GetImagePixelSize();
            LogViewportDebug($"LoadImage: new image size={replacementImage.Size}, pictureBox1.ClientSize={pictureBox1.ClientSize}");
            LogViewportDebug($"LoadImage: old image size={oldImageSize}");

            if (pictureBox1.Image != null)
            {
                pictureBox1.Image.Dispose();
            }

            LogViewportDebug($"LoadImage: About to set pictureBox1.Image");
            pictureBox1.Image = replacementImage;
            LogViewportDebug($"LoadImage: After Image setter, panOffset={pictureBox1.Metrics.PanOffset}");
            
            LogViewportDebug($"LoadImage: Calling explicit CenterImage");
            pictureBox1.CenterImage();
            LogViewportDebug($"LoadImage: After CenterImage, panOffset={pictureBox1.Metrics.PanOffset}");
            _zoomlevel = pictureBox1.ZoomLevel;

            // Only resize window when not hosted (standalone mode)
            if (!isHostedView)
            {
                LogViewportDebug($"LoadImage: Calling ResizeWindowToImage({imgData.Size})");
                ResizeWindowToImage(imgData.Size);
                LogViewportDebug($"LoadImage: After ResizeWindowToImage, pictureBox1.ClientSize={pictureBox1.ClientSize}, panOffset={pictureBox1.Metrics.PanOffset}");
            }

            LogViewportDebug($"LoadImage: Calling HandleResize");
            HandleResize();
            LogViewportDebug($"LoadImage: After HandleResize, panOffset={pictureBox1.Metrics.PanOffset}");
            LogViewportDebug($"LoadImage: === END ===" );

            undoStack.Clear();
            hasUnsavedChanges = false;
            ClearClipboardNotification();

            UpdateCommandUI();
            UpdateWindowTitle();
            UpdateStatusBar();

            if (Visible)
            {
                // Re-center after layout settles to handle any deferred resize events
                BeginInvoke(new Action(() =>
                {
                    pictureBox1?.CenterImage();
                    Focus();
                    pictureBox1?.Focus();
                }));
            }
        }

        internal void ShowAndFocus()
        {
            if (hostServices?.ActivatePresenter != null)
            {
                hostServices.ActivatePresenter(this);
            }
            else
            {
                if (!Visible)
                {
                    Show();
                }

                Activate();
            }

            Focus();
            pictureBox1?.Focus();
        }

        internal void AdoptWindowGeometry(Form? source)
        {
            if (source == null)
            {
                return;
            }

            StartPosition = FormStartPosition.Manual;
            var referenceBounds = source.WindowState == FormWindowState.Normal
                ? source.Bounds
                : source.RestoreBounds;

            if (referenceBounds.Width > 0 && referenceBounds.Height > 0)
            {
                WindowState = FormWindowState.Normal;
                Bounds = referenceBounds;
            }

            if (source.WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }
        }

        private static void LogViewportDebug(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [ImageEditor] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Screenzap", "viewport-debug.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch { }
        }

        internal void ResetZoom()
        {
            ZoomLevel = 1;
            pictureBox1?.CenterImage();
        }

        internal void HandleResize()
        {
            ClampImageLocationWithinCanvas();
            PositionOverlayToolStrips();
            Invalidate();
        }

        private void InitializeToolbarLayout()
        {
            if (mainToolStrip == null)
            {
                return;
            }

            toolsToolStrip = new ToolStrip
            {
                Name = "toolsToolStrip",
                Dock = DockStyle.Left,
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = false,
                Width = 40,
                ImageScalingSize = mainToolStrip.ImageScalingSize,
                LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow,
                Padding = new Padding(2),
                CanOverflow = false
            };

            MoveToolStripItem(mainToolStrip, toolsToolStrip, arrowToolStripButton);
            MoveToolStripItem(mainToolStrip, toolsToolStrip, rectangleToolStripButton);
            MoveToolStripItem(mainToolStrip, toolsToolStrip, textToolStripButton);
            MoveToolStripItem(mainToolStrip, toolsToolStrip, censorToolStripButton);
            MoveToolStripItem(mainToolStrip, toolsToolStrip, straightenToolStripButton);

            foreach (var button in new[] { arrowToolStripButton, rectangleToolStripButton, textToolStripButton, censorToolStripButton, straightenToolStripButton })
            {
                if (button != null)
                {
                    button.DisplayStyle = ToolStripItemDisplayStyle.Image;
                    button.TextImageRelation = TextImageRelation.ImageBeforeText;
                    button.AutoToolTip = true;
                }
            }

            textOptionsToolStrip = new ToolStrip
            {
                Name = "textOptionsToolStrip",
                Dock = DockStyle.None,
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = true,
                ImageScalingSize = mainToolStrip.ImageScalingSize,
                Padding = new Padding(4, 2, 0, 2),
                Visible = false,
                CanOverflow = false
            };

            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, textToolSeparator);
            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, fontComboBox);
            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, fontVariantComboBox);
            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, fontSizeComboBox);
            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, boldButton);
            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, italicButton);
            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, underlineButton);
            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, textColorButton);
            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, outlineColorButton);
            MoveToolStripItem(mainToolStrip, textOptionsToolStrip, outlineThicknessComboBox);
            NormalizeToolStripItemAlignment(textOptionsToolStrip);

            annotationOptionsToolStrip = new ToolStrip
            {
                Name = "annotationOptionsToolStrip",
                Dock = DockStyle.None,
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = true,
                ImageScalingSize = mainToolStrip.ImageScalingSize,
                Padding = new Padding(4, 2, 0, 2),
                Visible = false,
                CanOverflow = false
            };

            MoveToolStripItem(mainToolStrip, annotationOptionsToolStrip, annotationToolSeparator);
            MoveToolStripItem(mainToolStrip, annotationOptionsToolStrip, lineThicknessLabel);
            MoveToolStripItem(mainToolStrip, annotationOptionsToolStrip, lineThicknessComboBox);
            MoveToolStripItem(mainToolStrip, annotationOptionsToolStrip, arrowSizeLabel);
            MoveToolStripItem(mainToolStrip, annotationOptionsToolStrip, arrowSizeComboBox);
            NormalizeToolStripItemAlignment(annotationOptionsToolStrip);

            Controls.Add(toolsToolStrip);
            Controls.Add(textOptionsToolStrip);
            Controls.Add(annotationOptionsToolStrip);
            toolsToolStrip.BringToFront();
            mainToolStrip.BringToFront();
        }

        private static void MoveToolStripItem(ToolStrip? source, ToolStrip? destination, ToolStripItem? item)
        {
            if (source == null || destination == null || item == null)
            {
                return;
            }

            if (source.Items.Contains(item))
            {
                source.Items.Remove(item);
            }

            destination.Items.Add(item);
        }

        private static void NormalizeToolStripItemAlignment(ToolStrip? strip)
        {
            if (strip == null)
            {
                return;
            }

            foreach (ToolStripItem item in strip.Items)
            {
                item.Alignment = ToolStripItemAlignment.Left;
            }
        }

        private void PositionOverlayToolStrips()
        {
            int leftInset = (toolsToolStrip?.Visible == true ? toolsToolStrip.Width : 0) + 6;
            int topInset = (mainToolStrip?.Bottom ?? 0) + 4;

            if (annotationOptionsToolStrip != null)
            {
                annotationOptionsToolStrip.Location = new Point(leftInset, topInset);
                annotationOptionsToolStrip.BringToFront();
            }

            if (textOptionsToolStrip != null)
            {
                textOptionsToolStrip.Location = new Point(leftInset, topInset);
                textOptionsToolStrip.BringToFront();
            }

            if (censorToolStrip != null)
            {
                censorToolStrip.Location = new Point(leftInset, topInset);
                censorToolStrip.BringToFront();
            }

            if (straightenToolStrip != null)
            {
                straightenToolStrip.Location = new Point(leftInset, topInset);
                straightenToolStrip.BringToFront();
            }
        }

        private void ClampImageLocationWithinCanvas()
        {
            pictureBox1?.ClampPan();
        }

        private void RecenterViewportAfterImageChange(bool resizeWindow)
        {
            if (pictureBox1 == null)
            {
                return;
            }

            if (resizeWindow)
            {
                ResizeWindowToImage(pictureBox1.GetImagePixelSize());
            }

            RealignViewportAfterCanvasMutation();
        }

        private void RealignViewportAfterCanvasMutation()
        {
            if (pictureBox1 == null)
            {
                return;
            }

            HandleResize();
            pictureBox1.CenterImage();

            if (!IsHandleCreated)
            {
                pictureBox1.Invalidate();
                return;
            }

            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || pictureBox1 == null || pictureBox1.IsDisposed)
                {
                    return;
                }

                HandleResize();
                pictureBox1.CenterImage();
                pictureBox1.Invalidate();
            }));
        }

        private Rectangle GetImageBounds()
        {
            var imageSize = pictureBox1.GetImagePixelSize();
            return imageSize.IsEmpty
                ? Rectangle.Empty
                : new Rectangle(Point.Empty, imageSize);
        }

        private Rectangle ClampToImage(Rectangle region)
        {
            var imageSize = pictureBox1.GetImagePixelSize();
            if (imageSize.IsEmpty)
            {
                return Rectangle.Empty;
            }

            var bounds = GetImageBounds();
            var intersection = Rectangle.Intersect(bounds, region);
            return intersection;
        }

        private bool ExecuteReplaceWithBackground()
        {
            if (!HasEditableImage || Selection.IsEmpty)
            {
                return false;
            }

            var imageSize = pictureBox1.GetImagePixelSize();
            if (imageSize.IsEmpty)
            {
                return false;
            }

            var clampedSelection = ClampToImage(Selection);
            if (clampedSelection.Width <= 0 || clampedSelection.Height <= 0)
            {
                return false;
            }

            var sourceEdges = ReplaceBackgroundInterpolation.DetermineSourceEdges(clampedSelection, imageSize);
            if (!sourceEdges.HasAnySource)
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
                            bool isBorder =
                                (x == 0 && sourceEdges.UseLeft) ||
                                (y == 0 && sourceEdges.UseTop) ||
                                (x == width - 1 && sourceEdges.UseRight) ||
                                (y == height - 1 && sourceEdges.UseBottom);
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

                var targetImage = pictureBox1.Image;
                if (targetImage == null)
                {
                    before.Dispose();
                    after?.Dispose();
                    return false;
                }

                using (var gImg = Graphics.FromImage(targetImage))
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

        private bool ExecuteOptimizeForText()
        {
            if (!HasEditableImage)
            {
                return false;
            }

            var targetRegion = Selection.IsEmpty ? GetImageBounds() : ClampToImage(Selection);
            if (targetRegion.Width <= 0 || targetRegion.Height <= 0)
            {
                return false;
            }

            var selectionBefore = Selection;
            var before = CaptureRegion(targetRegion);
            if (before == null)
            {
                return false;
            }

            Bitmap? after = null;

            try
            {
                after = CreateOptimizedForTextCopy(before);

                var targetImage = pictureBox1.Image;
                if (targetImage == null)
                {
                    before.Dispose();
                    after?.Dispose();
                    return false;
                }

                using (var gImg = Graphics.FromImage(targetImage))
                {
                    gImg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    gImg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    gImg.DrawImage(after, targetRegion);
                }

                PushUndoStep(targetRegion, before, after, selectionBefore, Selection);
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

        private bool ExecuteStraighten()
        {
            return ActivateStraightenTool();
        }

        private bool ExecuteExpandCanvas()
        {
            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return false;
            }

            var sourceImage = pictureBox1.Image;
            if (sourceImage.Width <= 0 || sourceImage.Height <= 0)
            {
                return false;
            }

            var padding = ExpandCanvasPaddingPixels;
            var beforeImage = new Bitmap(sourceImage);
            var selectionBefore = Selection;
            var annotationStateBefore = CloneAnnotations();

            var expandedWidth = sourceImage.Width + (padding * 2);
            var expandedHeight = sourceImage.Height + (padding * 2);
            var afterSnapshot = new Bitmap(expandedWidth, expandedHeight, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(afterSnapshot))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;

                g.DrawImage(sourceImage, new Rectangle(padding, padding, sourceImage.Width, sourceImage.Height), new Rectangle(0, 0, sourceImage.Width, sourceImage.Height), GraphicsUnit.Pixel);

                g.DrawImage(sourceImage, new Rectangle(0, padding, padding, sourceImage.Height), new Rectangle(0, 0, 1, sourceImage.Height), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(padding + sourceImage.Width, padding, padding, sourceImage.Height), new Rectangle(sourceImage.Width - 1, 0, 1, sourceImage.Height), GraphicsUnit.Pixel);

                g.DrawImage(sourceImage, new Rectangle(padding, 0, sourceImage.Width, padding), new Rectangle(0, 0, sourceImage.Width, 1), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(padding, padding + sourceImage.Height, sourceImage.Width, padding), new Rectangle(0, sourceImage.Height - 1, sourceImage.Width, 1), GraphicsUnit.Pixel);

                g.DrawImage(sourceImage, new Rectangle(0, 0, padding, padding), new Rectangle(0, 0, 1, 1), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(padding + sourceImage.Width, 0, padding, padding), new Rectangle(sourceImage.Width - 1, 0, 1, 1), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(0, padding + sourceImage.Height, padding, padding), new Rectangle(0, sourceImage.Height - 1, 1, 1), GraphicsUnit.Pixel);
                g.DrawImage(sourceImage, new Rectangle(padding + sourceImage.Width, padding + sourceImage.Height, padding, padding), new Rectangle(sourceImage.Width - 1, sourceImage.Height - 1, 1, 1), GraphicsUnit.Pixel);
            }

            var newImage = new Bitmap(afterSnapshot);
            var currentZoom = ZoomLevel;
            pictureBox1.Image?.Dispose();
            pictureBox1.Image = newImage;
            ZoomLevel = currentZoom;
            pictureBox1.ClampPan();

            RecenterViewportAfterImageChange(resizeWindow: true);

            var offset = new Point(padding, padding);
            if (!Selection.IsEmpty)
            {
                Selection = new Rectangle(Selection.Location.Add(offset), Selection.Size);
            }

            for (int index = 0; index < annotationShapes.Count; index++)
            {
                var shape = annotationShapes[index];
                shape.Start = shape.Start.Add(offset);
                shape.End = shape.End.Add(offset);
            }

            for (int index = 0; index < textAnnotations.Count; index++)
            {
                var annotation = textAnnotations[index];
                annotation.Position = annotation.Position.Add(offset);
            }

            SyncSelectedAnnotation();
            SyncSelectedTextAnnotation();

            var selectionAfter = Selection;
            var annotationStateAfter = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, beforeImage, afterSnapshot, selectionBefore, selectionAfter, true, annotationStateBefore, annotationStateAfter);

            UpdateCommandUI();
            UpdateStatusBar();
            pictureBox1.Invalidate();
            return true;
        }

        private static Bitmap CreateOptimizedForTextCopy(Bitmap source)
        {
            using var original = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(original))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            var rect = new Rectangle(0, 0, original.Width, original.Height);
            var data = original.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = data.Stride;
                int length = Math.Abs(stride) * original.Height;
                var sourceBytes = new byte[length];
                Marshal.Copy(data.Scan0, sourceBytes, 0, length);

                var blurred = ApplyGaussianBlur(sourceBytes, original.Width, original.Height, stride, OptimizeTextBlurRadius);
                var output = new byte[length];

                for (int i = 0; i < length; i += 4)
                {
                    byte srcB = sourceBytes[i];
                    byte srcG = sourceBytes[i + 1];
                    byte srcR = sourceBytes[i + 2];

                    int outB = (srcB * 255) / Math.Max(1, (int)blurred[i]);
                    int outG = (srcG * 255) / Math.Max(1, (int)blurred[i + 1]);
                    int outR = (srcR * 255) / Math.Max(1, (int)blurred[i + 2]);

                    output[i] = (byte)Math.Min(255, outB);
                    output[i + 1] = (byte)Math.Min(255, outG);
                    output[i + 2] = (byte)Math.Min(255, outR);
                    output[i + 3] = sourceBytes[i + 3];
                }

                var result = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                var resultData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(output, 0, resultData.Scan0, length);
                }
                finally
                {
                    result.UnlockBits(resultData);
                }

                return result;
            }
            finally
            {
                original.UnlockBits(data);
            }
        }

        private static byte[] ApplyGaussianBlur(byte[] source, int width, int height, int stride, int radius)
        {
            var kernel = BuildGaussianKernel(radius);
            int pixelCount = width * height;
            var tempB = new float[pixelCount];
            var tempG = new float[pixelCount];
            var tempR = new float[pixelCount];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                int rowIndex = y * width;

                for (int x = 0; x < width; x++)
                {
                    float sumB = 0f;
                    float sumG = 0f;
                    float sumR = 0f;

                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = Math.Clamp(x + k, 0, width - 1);
                        int idx = rowOffset + (sx * 4);
                        float weight = kernel[k + radius];
                        sumB += source[idx] * weight;
                        sumG += source[idx + 1] * weight;
                        sumR += source[idx + 2] * weight;
                    }

                    int tempIndex = rowIndex + x;
                    tempB[tempIndex] = sumB;
                    tempG[tempIndex] = sumG;
                    tempR[tempIndex] = sumR;
                }
            }

            var blurred = new byte[source.Length];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    float sumB = 0f;
                    float sumG = 0f;
                    float sumR = 0f;

                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = Math.Clamp(y + k, 0, height - 1);
                        int tempIndex = (sy * width) + x;
                        float weight = kernel[k + radius];
                        sumB += tempB[tempIndex] * weight;
                        sumG += tempG[tempIndex] * weight;
                        sumR += tempR[tempIndex] * weight;
                    }

                    int idx = rowOffset + (x * 4);
                    blurred[idx] = ClampToByte(sumB);
                    blurred[idx + 1] = ClampToByte(sumG);
                    blurred[idx + 2] = ClampToByte(sumR);
                    blurred[idx + 3] = source[idx + 3];
                }
            }

            return blurred;
        }

        private static float[] BuildGaussianKernel(int radius)
        {
            int size = (radius * 2) + 1;
            var kernel = new float[size];
            double sigma = Math.Max(1.0, radius / 3.0);
            double twoSigmaSquared = 2.0 * sigma * sigma;
            double sum = 0.0;

            for (int i = -radius; i <= radius; i++)
            {
                double value = Math.Exp(-(i * i) / twoSigmaSquared);
                kernel[i + radius] = (float)value;
                sum += value;
            }

            if (sum > 0.0)
            {
                for (int i = 0; i < size; i++)
                {
                    kernel[i] = (float)(kernel[i] / sum);
                }
            }

            return kernel;
        }

        private static byte ClampToByte(float value)
        {
            if (value <= 0f)
            {
                return 0;
            }

            if (value >= 255f)
            {
                return 255;
            }

            return (byte)(value + 0.5f);
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

            var sourceImage = pictureBox1.Image;
            if (sourceImage == null)
            {
                return false;
            }

            var beforeImage = new Bitmap(sourceImage);

            Bitmap afterSnapshot = new Bitmap(clampedSelection.Width, clampedSelection.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(afterSnapshot))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(sourceImage, new Rectangle(Point.Empty, clampedSelection.Size), clampedSelection, GraphicsUnit.Pixel);
            }

            var newImage = new Bitmap(afterSnapshot);

            var currentZoom = ZoomLevel;
            pictureBox1.Image?.Dispose();
            pictureBox1.Image = newImage;
            ZoomLevel = currentZoom;
            pictureBox1.ClampPan();

            RecenterViewportAfterImageChange(resizeWindow: true);

            Selection = selectionAfter;
            isPlaceholderImage = false;

            ApplyCropToAnnotations(clampedSelection.Location, clampedSelection.Size);
            ApplyCropToTextAnnotations(clampedSelection.Location, clampedSelection.Size);
            var annotationStateAfter = CloneAnnotations();

            PushUndoStep(Rectangle.Empty, beforeImage, afterSnapshot, selectionBefore, selectionAfter, true, annotationStateBefore, annotationStateAfter);

            UpdateCommandUI();
            UpdateStatusBar();
            pictureBox1.Invalidate();
            return true;
        }

        internal bool ExecuteCropForDiagnostics()
        {
            return ExecuteCrop();
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
            if (expandCanvasToolStripButton != null)
            {
                expandCanvasToolStripButton.Enabled = enable;
            }
            if (flipHorizontalToolStripButton != null)
            {
                flipHorizontalToolStripButton.Enabled = enable;
            }
            if (flipVerticalToolStripButton != null)
            {
                flipVerticalToolStripButton.Enabled = enable;
            }
            if (rotateToolStripButton != null)
            {
                rotateToolStripButton.Enabled = enable;
            }
            if (replaceToolStripButton != null)
            {
                replaceToolStripButton.Enabled = enable && !Selection.IsEmpty;
            }
            if (optimizeTextToolStripButton != null)
            {
                optimizeTextToolStripButton.Enabled = enable;
            }
            if (straightenToolStripButton != null)
            {
                straightenToolStripButton.Enabled = enable;
            }
            if (censorToolStripButton != null)
            {
                censorToolStripButton.Enabled = enable;
            }
            if (copyClipboardToolStripButton != null)
            {
                copyClipboardToolStripButton.Enabled = enable;
            }
            if (reloadToolStripButton != null)
            {
                reloadToolStripButton.Enabled = enable || clipboardHasPendingReload;
            }

            UpdateDrawingToolButtons();
            UpdateTextToolButtons();
            UpdateTraceButtonState();
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

        private void pictureBox1_ZoomChanged(object? sender, EventArgs e)
        {
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            if (resolutionStatusLabel == null || zoomStatusLabel == null || selectionStatusLabel == null)
            {
                return;
            }

            var imageSize = pictureBox1?.GetImagePixelSize() ?? Size.Empty;
            if (imageSize.IsEmpty || !HasEditableImage)
            {
                resolutionStatusLabel.Text = string.Empty;
                zoomStatusLabel.Text = string.Empty;
                selectionStatusLabel.Text = string.Empty;
            }
            else
            {
                resolutionStatusLabel.Text = $"{imageSize.Width} × {imageSize.Height}";
                var zoomPercent = (pictureBox1?.ZoomLevel ?? 1m) * 100;
                zoomStatusLabel.Text = $"{zoomPercent:0}%";

                if (Selection.IsEmpty)
                {
                    selectionStatusLabel.Text = string.Empty;
                }
                else
                {
                    selectionStatusLabel.Text = $"Sel: {Selection.X}, {Selection.Y}  {Selection.Width} × {Selection.Height}";
                }
            }
        }

        private void UpdateRubberBandStatus()
        {
            if (selectionStatusLabel == null)
            {
                return;
            }

            var rubberBand = ClampToImage(GetNormalizedRect(MouseInPixel, MouseOutPixel));
            if (rubberBand.Width > 0 || rubberBand.Height > 0)
            {
                selectionStatusLabel.Text = $"Sel: {rubberBand.X}, {rubberBand.Y}  {rubberBand.Width} × {rubberBand.Height}";
            }
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

            if (annotationShapes.Count == 0 && textAnnotations.Count == 0)
            {
                return composite;
            }

            using (var graphics = Graphics.FromImage(composite))
            {
                DrawAnnotations(graphics, AnnotationSurface.Image);
                DrawTextAnnotations(graphics, AnnotationSurface.Image);
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
                hasUnsavedChanges = false;
                return true;
            }

            if (generatedPath)
            {
                currentSavePath = null;
            }

            return false;
        }

        internal bool ExecuteSaveForDiagnostics()
        {
            return ExecuteSave();
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
                            hasUnsavedChanges = false;
                        return true;
                    }
                }
            }

            return false;
        }

        internal bool ExecuteSaveAsForDiagnostics(string targetPath)
        {
            if (!HasEditableImage || string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            if (!PersistImage(targetPath))
            {
                return false;
            }

            currentSavePath = targetPath;
            UpdateCommandUI();
            UpdateWindowTitle();
            hasUnsavedChanges = false;
            return true;
        }

        internal bool ClipboardHasPendingReloadForDiagnostics => clipboardHasPendingReload;

        internal void SetPendingReloadForDiagnostics(bool hasPendingReload, bool useTextTarget)
        {
            clipboardHasPendingReload = hasPendingReload;
            pendingReloadTarget = hasPendingReload
                ? (useTextTarget ? ClipboardReloadTarget.Text : ClipboardReloadTarget.Image)
                : ClipboardReloadTarget.None;
            UpdateReloadIndicator();
        }

        internal void SetHasUnsavedChangesForDiagnostics(bool hasUnsavedChangesValue)
        {
            hasUnsavedChanges = hasUnsavedChangesValue;
            UpdateWindowTitle();
        }

        internal void ReloadFromClipboardForDiagnostics()
        {
            ReloadFromClipboard();
        }

        internal bool CopySelectionToClipboardForDiagnostics()
        {
            return CopySelectionToClipboard();
        }

        internal bool PasteFromClipboardForDiagnostics()
        {
            return TryPasteImageFromClipboard();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Intercept arrow/navigation keys for text edit mode before WinForms
            // converts them into focus-movement commands (they never reach KeyDown otherwise).
            // Do NOT steal keys when a toolbar control (font picker, size box, etc.) has focus —
            // those controls need arrows/typing for their own purposes.
            if (isTextToolActive && activeTextAnnotation?.IsEditing == true)
            {
                var focused = this.ActiveControl ?? this.FindFocusedControl();
                bool toolbarHasFocus = focused is ToolStrip
                    || (focused != null && focused.Parent is ToolStrip)
                    || (focused is ComboBox cb && cb.Parent?.Parent is ToolStrip);

                if (!toolbarHasFocus)
                {
                    var code = keyData & Keys.KeyCode;
                    if (code == Keys.Left  || code == Keys.Right ||
                        code == Keys.Home  || code == Keys.End)
                    {
                        var ea = new KeyEventArgs(keyData);
                        HandleTextToolKeyDown(ea);
                        if (ea.Handled) return true;
                    }
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Control? FindFocusedControl()
        {
            // Walk the WinForms control tree to find whichever child actually has focus.
            var c = this as Control;
            while (c != null)
            {
                var next = c.GetContainerControl()?.ActiveControl;
                if (next == null || next == c) break;
                c = next;
            }
            return c;
        }

        private void ImageEditor_KeyDown(object sender, KeyEventArgs e)
        {
            //Console.WriteLine(e.Modifiers);

            // Handle text tool keyboard input first
            if (HandleTextToolKeyDown(e))
            {
                return;
            }

            if (isStraightenToolActive)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DeactivateStraightenTool(false);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.Enter)
                {
                    DeactivateStraightenTool(true);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                return;
            }

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
                if (isTextToolActive)
                {
                    FinalizeActiveTextAnnotation();
                    isTextToolActive = false;
                    UpdateTextToolButtons();
                    UpdateTextToolbarVisibility();
                    pictureBox1.Invalidate();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                if (isDrawingAnnotation || selectedAnnotation != null || activeDrawingTool != DrawingTool.None)
                {
                    CancelAnnotationPreview();
                    SelectAnnotation(null);;
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
                    if (pictureBox1 != null)
                    {
                        var cursorInViewport = pictureBox1.PointToClient(Cursor.Position);
                        MoveInPixel = FormCoordToPixel(cursorInViewport);
                    }
                    else
                    {
                        MoveInPixel = MouseOutPixel;
                    }
                }

                return;
            }

            else if (e.KeyCode == Keys.C && e.Control == true)
            {
                CopySelectionToClipboard();
            }
            else if (e.KeyCode == Keys.V && e.Control == true)
            {
                if (TryPasteImageFromClipboard())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.E && e.Modifiers == (Keys.Control | Keys.Shift))
            {
                if (ExecuteExpandCanvas())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
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
            else if (e.KeyCode == Keys.R && e.Control)
            {
                ReloadFromClipboard();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.T && e.Control)
            {
                if (ExecuteCrop())
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.L && e.Control)
            {
                if (ExecuteStraighten())
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

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (HandleTextToolKeyPress(e))
            {
                return;
            }

            base.OnKeyPress(e);
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

        private void expandCanvasToolStripButton_Click(object sender, EventArgs e)
        {
            if (ExecuteExpandCanvas())
            {
                pictureBox1?.Focus();
            }
        }

        private void flipHorizontalToolStripButton_Click(object sender, EventArgs e)
        {
            ExecuteFlip(RotateFlipType.RotateNoneFlipX);
        }

        private void flipVerticalToolStripButton_Click(object sender, EventArgs e)
        {
            ExecuteFlip(RotateFlipType.RotateNoneFlipY);
        }

        private void rotateToolStripButton_Click(object sender, EventArgs e)
        {
            ExecuteRotate90Cw();
        }

        private bool ExecuteRotate90Cw()
        {
            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return false;
            }

            var beforeImage = new Bitmap(pictureBox1.Image);
            var selectionBefore = Selection;
            var annotationStateBefore = CloneAnnotations();
            int width = pictureBox1.Image.Width;
            int height = pictureBox1.Image.Height;

            var rotated = new Bitmap(pictureBox1.Image);
            rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);

            pictureBox1.Image?.Dispose();
            pictureBox1.Image = rotated;

            // CW 90: (x,y) in (W,H) -> (H-1-y, x) in (H,W)
            static Point RotPoint(Point p, int srcHeight) => new Point(srcHeight - 1 - p.Y, p.X);

            if (!Selection.IsEmpty)
            {
                var tl = RotPoint(new Point(Selection.Left, Selection.Bottom), height);
                var br = RotPoint(new Point(Selection.Right, Selection.Top), height);
                Selection = new Rectangle(
                    Math.Min(tl.X, br.X),
                    Math.Min(tl.Y, br.Y),
                    Math.Abs(br.X - tl.X),
                    Math.Abs(br.Y - tl.Y));
            }

            for (int i = 0; i < annotationShapes.Count; i++)
            {
                var shape = annotationShapes[i];
                shape.Start = RotPoint(shape.Start, height);
                shape.End = RotPoint(shape.End, height);
            }

            for (int i = 0; i < textAnnotations.Count; i++)
            {
                var annotation = textAnnotations[i];
                annotation.Position = RotPoint(annotation.Position, height);
            }

            SyncSelectedAnnotation();
            SyncSelectedTextAnnotation();

            var selectionAfter = Selection;
            var annotationStateAfter = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, beforeImage, new Bitmap(rotated), selectionBefore, selectionAfter, true, annotationStateBefore, annotationStateAfter);

            MarkDirtyAndNotify();
            ResizeWindowToImage(rotated.Size);
            UpdateCommandUI();
            UpdateStatusBar();
            pictureBox1.Invalidate();
            _ = width;
            return true;
        }

        private bool ExecuteFlip(RotateFlipType flipType)
        {
            if (!HasEditableImage || pictureBox1.Image == null)
            {
                return false;
            }

            var beforeImage = new Bitmap(pictureBox1.Image);
            var selectionBefore = Selection;
            var annotationStateBefore = CloneAnnotations();
            int width = pictureBox1.Image.Width;
            int height = pictureBox1.Image.Height;

            var flipped = new Bitmap(pictureBox1.Image);
            flipped.RotateFlip(flipType);

            pictureBox1.Image?.Dispose();
            pictureBox1.Image = flipped;

            bool horizontal = flipType == RotateFlipType.RotateNoneFlipX;

            // Mirror selection
            if (!Selection.IsEmpty)
            {
                if (horizontal)
                {
                    Selection = new Rectangle(width - Selection.Right, Selection.Y, Selection.Width, Selection.Height);
                }
                else
                {
                    Selection = new Rectangle(Selection.X, height - Selection.Bottom, Selection.Width, Selection.Height);
                }
            }

            // Mirror annotation shapes
            for (int i = 0; i < annotationShapes.Count; i++)
            {
                var shape = annotationShapes[i];
                if (horizontal)
                {
                    shape.Start = new Point(width - shape.Start.X, shape.Start.Y);
                    shape.End = new Point(width - shape.End.X, shape.End.Y);
                }
                else
                {
                    shape.Start = new Point(shape.Start.X, height - shape.Start.Y);
                    shape.End = new Point(shape.End.X, height - shape.End.Y);
                }
            }

            // Mirror text annotations
            for (int i = 0; i < textAnnotations.Count; i++)
            {
                var annotation = textAnnotations[i];
                if (horizontal)
                {
                    annotation.Position = new Point(width - annotation.Position.X, annotation.Position.Y);
                }
                else
                {
                    annotation.Position = new Point(annotation.Position.X, height - annotation.Position.Y);
                }
            }

            SyncSelectedAnnotation();
            SyncSelectedTextAnnotation();

            var selectionAfter = Selection;
            var annotationStateAfter = CloneAnnotations();
            PushUndoStep(Rectangle.Empty, beforeImage, new Bitmap(flipped), selectionBefore, selectionAfter, true, annotationStateBefore, annotationStateAfter);

            MarkDirtyAndNotify();
            UpdateCommandUI();
            UpdateStatusBar();
            pictureBox1.Invalidate();

            return true;
        }

        private void replaceToolStripButton_Click(object sender, EventArgs e)
        {
            ExecuteReplaceWithBackground();
        }

        private void optimizeTextToolStripButton_Click(object sender, EventArgs e)
        {
            if (ExecuteOptimizeForText())
            {
                pictureBox1?.Focus();
            }
        }

        private void straightenToolStripButton_Click(object sender, EventArgs e)
        {
            if (ExecuteStraighten())
            {
                pictureBox1?.Focus();
            }
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

            using var snapshot = BuildCompositeImage();
            return WriteImageToClipboard(snapshot, "Failed to copy the image to the clipboard.");
        }

        private bool CopySelectionToClipboard()
        {
            if (Selection.IsEmpty || !HasEditableImage || pictureBox1.Image == null)
            {
                return false;
            }

            using var composite = BuildCompositeImage();
            using var selectionBitmap = new Bitmap(Selection.Width, Selection.Height);
            using (var graphics = Graphics.FromImage(selectionBitmap))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImage(composite, new Rectangle(Point.Empty, selectionBitmap.Size), Selection, GraphicsUnit.Pixel);
            }

            internalClipboardImage?.Dispose();
            internalClipboardImage = new Bitmap(selectionBitmap);
            return true;
        }

        private bool WriteImageToClipboard(Image image, string failurePrefix)
        {
            if (ClipboardImageWriterForDiagnostics != null)
            {
                using var diagnosticsImage = new Bitmap(image);
                TrackInternalClipboardImageWrite(diagnosticsImage);
                if (!ClipboardImageWriterForDiagnostics(diagnosticsImage))
                {
                    expectedInternalClipboardSignature = null;
                    suppressClipboardAutoReloadUntilUtc = null;
                    return false;
                }

                ClipboardMetadata.LastCaptureTimestamp = DateTime.Now;
                ClearClipboardNotification();
                return true;
            }

            try
            {
                TrackInternalClipboardImageWrite(image);
                Clipboard.SetImage(image);
                ClipboardMetadata.LastCaptureTimestamp = DateTime.Now;
                ClearClipboardNotification();
                return true;
            }
            catch (ExternalException ex)
            {
                expectedInternalClipboardSignature = null;
                suppressClipboardAutoReloadUntilUtc = null;
                MessageBox.Show(this, $"{failurePrefix}\n{ex.Message}", "Clipboard Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool TryPasteImageFromClipboard()
        {
            Image? clipboardImage = null;

            if (internalClipboardImage != null)
            {
                clipboardImage = new Bitmap(internalClipboardImage);
            }

            try
            {
                if (clipboardImage == null && !Clipboard.ContainsImage())
                {
                    return false;
                }

                if (clipboardImage == null)
                {
                    clipboardImage = Clipboard.GetImage();
                }
            }
            catch (ExternalException ex)
            {
                MessageBox.Show(this, $"Failed to access the clipboard.\n{ex.Message}", WindowTitleBase, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return true;
            }

            if (clipboardImage == null)
            {
                return false;
            }

            using (clipboardImage)
            {
                if (!HasEditableImage || pictureBox1.Image == null)
                {
                    LoadImage(clipboardImage);
                    return true;
                }

                var beforeImage = new Bitmap(pictureBox1.Image);
                var afterImage = new Bitmap(pictureBox1.Image);
                var selectionBefore = Selection;

                var destination = !Selection.IsEmpty
                    ? new Rectangle(Selection.Location, clipboardImage.Size)
                    : new Rectangle(
                        (afterImage.Width - clipboardImage.Width) / 2,
                        (afterImage.Height - clipboardImage.Height) / 2,
                        clipboardImage.Width,
                        clipboardImage.Height);

                var imageBounds = new Rectangle(Point.Empty, afterImage.Size);
                var clampedDestination = Rectangle.Intersect(imageBounds, destination);
                if (clampedDestination.Width <= 0 || clampedDestination.Height <= 0)
                {
                    beforeImage.Dispose();
                    afterImage.Dispose();
                    return false;
                }

                var sourceRect = new Rectangle(
                    clampedDestination.X - destination.X,
                    clampedDestination.Y - destination.Y,
                    clampedDestination.Width,
                    clampedDestination.Height);

                using (var graphics = Graphics.FromImage(afterImage))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.DrawImage(clipboardImage, clampedDestination, sourceRect, GraphicsUnit.Pixel);
                }

                var appliedImage = new Bitmap(afterImage);
                pictureBox1.Image.Dispose();
                pictureBox1.Image = appliedImage;
                Selection = clampedDestination;

                PushUndoStep(Rectangle.Empty, beforeImage, afterImage, selectionBefore, Selection, true);

                isPlaceholderImage = false;
                UpdateCommandUI();
                UpdateStatusBar();
                pictureBox1.Invalidate();
                return true;
            }
        }

        private void ReloadFromClipboard(bool showEmptyClipboardMessage = true)
        {
            if (!ConfirmReloadWhenDirty())
            {
                return;
            }

            if (TryReloadImageFromClipboard())
            {
                return;
            }

            if (TrySwitchToTextEditorFromClipboard())
            {
                return;
            }

            if (showEmptyClipboardMessage)
            {
                MessageBox.Show(this, "Clipboard does not contain image or text data to reload.", WindowTitleBase, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private bool ConfirmReloadWhenDirty()
        {
            if (ConfirmReloadWhenDirtyOverrideForDiagnostics != null)
            {
                return ConfirmReloadWhenDirtyOverrideForDiagnostics();
            }

            if (!hasUnsavedChanges)
            {
                return true;
            }

            var keepEditingButton = new TaskDialogButton("Keep editing");
            var reloadButton = new TaskDialogButton("Reload");

            var page = new TaskDialogPage
            {
                Caption = WindowTitleBase,
                Heading = "Discard unsaved changes?",
                Text = "Reloading from the clipboard will discard unsaved changes.",
                Icon = TaskDialogIcon.Warning,
                Buttons = { keepEditingButton, reloadButton },
                DefaultButton = reloadButton
            };

            return TaskDialog.ShowDialog(this, page) == reloadButton;
        }

        private bool TryReloadImageFromClipboard()
        {
            Image? clipboardImage = null;

            if (ClipboardImageProviderForDiagnostics != null)
            {
                clipboardImage = ClipboardImageProviderForDiagnostics();
            }
            else
            {
                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        clipboardImage = Clipboard.GetImage();
                    }
                }
                catch (ExternalException ex)
                {
                    MessageBox.Show(this, $"Failed to access the clipboard.\n{ex.Message}", WindowTitleBase, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return true;
                }
            }

            if (clipboardImage == null)
            {
                return false;
            }

            using (clipboardImage)
            {
                LoadImage(clipboardImage);
            }

            ClearClipboardNotification();
            return true;
        }

        private bool TrySwitchToTextEditorFromClipboard()
        {
            if (RequestTextEditor == null)
            {
                return false;
            }

            string? clipboardText;
            if (ClipboardTextProviderForDiagnostics != null)
            {
                clipboardText = ClipboardTextProviderForDiagnostics();
            }
            else
            {
                clipboardText = null;
                try
                {
                    if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                    {
                        clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
                    }
                }
                catch (ExternalException ex)
                {
                    MessageBox.Show(this, $"Failed to access the clipboard.\n{ex.Message}", WindowTitleBase, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return true;
                }
            }

            if (clipboardText == null)
            {
                return false;
            }

            var textEditor = RequestTextEditor?.Invoke();
            if (textEditor == null)
            {
                return false;
            }

            textEditor.LoadText(clipboardText);
            if (hostServices?.ActivatePresenter != null)
            {
                hostServices.ActivatePresenter(textEditor);
                textEditor.FocusEditor();
            }
            else
            {
                textEditor.AdoptWindowGeometry(this);
                textEditor.ShowAndFocus();
                if (Visible)
                {
                    Hide();
                }
            }

            ClearClipboardNotification();
            return true;
        }

        private void copyClipboardToolStripButton_Click(object sender, EventArgs e)
        {
            if (CopyImageToClipboard())
            {
                pictureBox1?.Focus();
            }
        }

        private void reloadToolStripButton_Click(object? sender, EventArgs e)
        {
            ReloadFromClipboard();
        }

        private void arrowToolStripButton_Click(object sender, EventArgs e)
        {
            ToggleDrawingTool(DrawingTool.Arrow);
        }

        private void rectangleToolStripButton_Click(object sender, EventArgs e)
        {
            ToggleDrawingTool(DrawingTool.Rectangle);
        }

        private async void tracePosterMenuItem_Click(object? sender, EventArgs e)
        {
            await TraceImageToSvgAsync(lib.ImageTracer.TracingPreset.Poster);
        }

        private async void tracePhotoMenuItem_Click(object? sender, EventArgs e)
        {
            await TraceImageToSvgAsync(lib.ImageTracer.TracingPreset.Photo);
        }

        private async void traceBwMenuItem_Click(object? sender, EventArgs e)
        {
            await TraceImageToSvgAsync(lib.ImageTracer.TracingPreset.BlackAndWhite);
        }

        private void UpdateTraceButtonState()
        {
            if (traceToolStripDropDown == null)
            {
                return;
            }

            traceToolStripDropDown.Enabled = HasEditableImage && lib.ImageTracer.IsAvailable();
            traceToolStripDropDown.ToolTipText = lib.ImageTracer.IsAvailable()
                ? "Trace bitmap to SVG and copy to clipboard"
                : "VTracer not found. Download from github.com/visioncortex/vtracer/releases";
        }

        private async Task TraceImageToSvgAsync(lib.ImageTracer.TracingPreset preset)
        {
            if (!HasEditableImage || pictureBox1?.Image == null)
            {
                return;
            }

            if (!lib.ImageTracer.IsAvailable())
            {
                MessageBox.Show(
                    "VTracer executable not found.\n\n" +
                    "Download vtracer.exe from:\nhttps://github.com/visioncortex/vtracer/releases\n\n" +
                    "Place vtracer.exe in the application folder.",
                    "VTracer Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var originalButtonText = traceToolStripDropDown?.Text;
            try
            {
                if (traceToolStripDropDown != null)
                {
                    traceToolStripDropDown.Enabled = false;
                    traceToolStripDropDown.Text = "Tracing...";
                }

                using var bitmap = new Bitmap(pictureBox1.Image);
                var svg = await lib.ImageTracer.TraceToSvgAsync(bitmap, preset);

                if (!string.IsNullOrEmpty(svg))
                {
                    TrackInternalClipboardTextWrite(svg);
                    Clipboard.SetText(svg);
                }
            }
            catch (Exception ex)
            {
                expectedInternalClipboardSignature = null;
                suppressClipboardAutoReloadUntilUtc = null;
                MessageBox.Show(
                    $"Failed to trace image:\n{ex.Message}",
                    "Trace Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (traceToolStripDropDown != null)
                {
                    traceToolStripDropDown.Text = originalButtonText;
                    UpdateTraceButtonState();
                }
            }
        }

        Control IClipboardDocumentPresenter.View => this;

        string IClipboardDocumentPresenter.DisplayName => "Image";

        void IClipboardDocumentPresenter.AttachHostServices(EditorHostServices services)
        {
            hostServices = services;
            hostServices.SetReloadIndicator?.Invoke(clipboardHasPendingReload);
            ApplyHostChromeVisibility(isHosted: true);
            ContentEditedCallback = () => hostServices?.NotifyContentEdited?.Invoke();
        }

        bool IClipboardDocumentPresenter.CanHandleClipboard(IDataObject dataObject)
        {
            return dataObject?.GetDataPresent(DataFormats.Bitmap, true) == true;
        }

        void IClipboardDocumentPresenter.LoadFromClipboard(IDataObject dataObject)
        {
            if (dataObject == null)
            {
                return;
            }

            var clipboardImage = dataObject.GetData(DataFormats.Bitmap, true) as Image;
            if (clipboardImage is Image img)
            {
                using (img)
                {
                    LoadImage(img);
                }
            }
        }

        bool IClipboardDocumentPresenter.CanExecute(EditorCommandId commandId)
        {
            return commandId switch
            {
                EditorCommandId.Save => saveToolStripButton?.Enabled == true,
                EditorCommandId.SaveAs => saveAsToolStripButton?.Enabled == true,
                EditorCommandId.Copy => copyClipboardToolStripButton?.Enabled == true,
                EditorCommandId.Reload => true,
                EditorCommandId.ExpandCanvas => expandCanvasToolStripButton?.Enabled == true,
                EditorCommandId.Undo => undoStack.CanUndo,
                EditorCommandId.Redo => undoStack.CanRedo,
                EditorCommandId.Find => false,
                _ => false
            };
        }

        bool IClipboardDocumentPresenter.TryExecute(EditorCommandId commandId)
        {
            switch (commandId)
            {
                case EditorCommandId.Save:
                    return ExecuteSave();
                case EditorCommandId.SaveAs:
                    return ExecuteSaveAs();
                case EditorCommandId.Copy:
                    return CopyImageToClipboard();
                case EditorCommandId.Reload:
                    ReloadFromClipboard();
                    return true;
                case EditorCommandId.ExpandCanvas:
                    return ExecuteExpandCanvas();
                case EditorCommandId.Undo:
                    {
                        var step = undoStack.Undo();
                        if (step == null)
                        {
                            return false;
                        }

                        ApplyUndoStep(step, false);
                        UpdateCommandUI();
                        return true;
                    }
                case EditorCommandId.Redo:
                    {
                        var step = undoStack.Redo();
                        if (step == null)
                        {
                            return false;
                        }

                        ApplyUndoStep(step, true);
                        UpdateCommandUI();
                        return true;
                    }
                default:
                    return false;
            }
        }

        void IClipboardDocumentPresenter.OnActivated()
        {
            HandleResize();
            pictureBox1?.Focus();
        }

        void IClipboardDocumentPresenter.OnDeactivated()
        {
            // No-op for now.
        }

        bool IClipboardDocumentPresenter.CanPresent(ClipboardHistoryItem item)
        {
            return item?.Kind == ClipboardItemKind.Image;
        }

        void IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item)
        {
            if (item?.CurrentImage == null) return;
            LoadImage(item.CurrentImage);
            // LoadImage clears the undo stack. Restore any stashed state so the user can keep undoing.
            undoStack.RestoreState(item.UndoSnapshot);
            hasUnsavedChanges = item.IsDirty;
            UpdateCommandUI();
        }

        void IClipboardDocumentPresenter.StashHistoryItemState(ClipboardHistoryItem item)
        {
            if (item == null) return;
            if (pictureBox1?.Image is Bitmap current)
            {
                // Flatten annotations into the stored bitmap so switching back preserves the visual state.
                using var composite = BuildCompositeImage();
                item.UpdateCurrentImage(composite);
            }
            item.UndoSnapshot = undoStack.ExtractState();
        }

        object? IClipboardDocumentPresenter.GetCurrentContent()
        {
            if (pictureBox1?.Image is Bitmap && HasEditableImage)
            {
                return BuildCompositeImage();
            }
            return null;
        }

        private void ApplyHostChromeVisibility(bool isHosted)
        {
            isHostedView = isHosted;

            void ToggleHostItemVisibility(ToolStripItem? item)
            {
                if (item != null)
                {
                    item.Visible = !isHosted;
                }
            }

            ToggleHostItemVisibility(saveToolStripButton);
            ToggleHostItemVisibility(saveAsToolStripButton);
            ToggleHostItemVisibility(copyClipboardToolStripButton);
            ToggleHostItemVisibility(reloadToolStripButton);

            if (reloadNotificationLabel != null && isHosted)
            {
                reloadNotificationLabel.Visible = false;
            }

            if (!isHosted)
            {
                UpdateReloadIndicator();
            }
        }


    }
}

