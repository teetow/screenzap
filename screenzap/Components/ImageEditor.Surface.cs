using screenzap.lib;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace screenzap;

// Production boundary between document editing and the window that presents it. No native
// child HWND is embedded in WinUI: the existing renderer supplies pixels and accepts input.
public partial class ImageEditor
{
    private bool externalSurface;
    private Point externalPointerPosition;
    private Point CurrentPointerInViewport() => externalSurface ? externalPointerPosition : pictureBox1.PointToClient(Cursor.Position);
    internal event Action? SurfaceInvalidated;
    internal Action? CanvasFocusRequested { get; set; }
    internal string SurfaceCursor => UseWaitCursor ? "Wait" : Cursor == Cursors.Cross ? "Cross" : Cursor == Cursors.IBeam ? "IBeam" : Cursor == Cursors.Hand ? "Hand" : Cursor == Cursors.SizeAll ? "SizeAll" : Cursor == Cursors.SizeNS ? "SizeNorthSouth" : Cursor == Cursors.SizeWE ? "SizeWestEast" : Cursor == Cursors.SizeNWSE ? "SizeNorthwestSoutheast" : Cursor == Cursors.SizeNESW ? "SizeNortheastSouthwest" : "Arrow";
    internal Size SurfaceViewportSize => pictureBox1.ClientSize;
    internal Size SurfaceImageSize => pictureBox1.Image?.Size ?? Size.Empty;
    internal decimal SurfaceZoom => pictureBox1.ZoomLevel;
    internal bool SurfaceTransparency => pictureBox1.AlphaViewEnabled;

    internal void AttachExternalSurface()
    {
        externalSurface = true;
        // Retain the private controls as compatibility state for document algorithms, but
        // remove their layout from the surface. They cannot obscure or resize the canvas.
        Controls.Clear();
        canvasPanel.Controls.Clear();
        Controls.Add(canvasPanel);
        canvasPanel.Dock = DockStyle.Fill;
        canvasPanel.Controls.Add(pictureBox1);
        pictureBox1.Dock = DockStyle.Fill;
        pictureBox1.BackColor = Color.FromArgb(24, 26, 29);
        pictureBox1.Invalidated += (_, _) => SurfaceInvalidated?.Invoke();
        _ = Handle;
        _ = pictureBox1.Handle;
    }

    private void RequestCanvasFocus()
    {
        if (externalSurface) CanvasFocusRequested?.Invoke();
        else pictureBox1?.Focus();
    }

    internal void ResizeSurface(Size size)
    {
        if (size.Width < 1 || size.Height < 1 || pictureBox1.ClientSize == size) return;
        Dock = DockStyle.None;
        ClientSize = size;
        canvasPanel.Bounds = new Rectangle(Point.Empty, size);
        pictureBox1.Bounds = new Rectangle(Point.Empty, size);
        pictureBox1.Invalidate();
    }

    internal void RenderSurface(Graphics graphics) => pictureBox1.Render(graphics);
    internal void SurfacePointer(int phase, Point point, MouseButtons button, int clicks = 1)
    {
        externalPointerPosition = point;
        var args = new MouseEventArgs(button, clicks, point.X, point.Y, 0);
        switch (phase)
        {
            case 0: pictureBox1_MouseDown(pictureBox1, args); break;
            case 1: pictureBox1_MouseMove(pictureBox1, args); break;
            case 2: pictureBox1_MouseUp(pictureBox1, args); break;
            case 3: pictureBox1_MouseDoubleClick(pictureBox1, args); break;
        }
        SurfaceInvalidated?.Invoke();
    }
    internal void SurfacePointerExit()
    {
        SetHoveredAnnotation(null);
        SetHoveredAnnotationHandle(AnnotationHandle.None);
        SetHoveredTextAnnotation(null);
        Cursor = Cursors.Default;
    }
    internal void SurfaceWheel(Point point, int delta)
    {
        pictureBox1.ZoomAround(delta > 0 ? FindZoomIn(SurfaceZoom) : FindZoomOut(SurfaceZoom), point);
        _zoomlevel = pictureBox1.ZoomLevel;
    }
    internal bool SurfaceKey(Keys keys, bool up = false)
    {
        if (up) { ImageEditor_KeyUp(this, new KeyEventArgs(keys)); return false; }
        var message = new Message();
        if (ProcessCmdKey(ref message, keys)) return true;
        string? beforeText = activeTextAnnotation?.Text;
        var args = new KeyEventArgs(keys);
        ImageEditor_KeyDown(this, args);
        if (beforeText != null && activeTextAnnotation != null && beforeText != activeTextAnnotation.Text) MarkDirtyAndNotify();
        return args.Handled || args.SuppressKeyPress;
    }
    internal void SurfaceCharacter(char character) { if (HandleTextToolKeyPress(new KeyPressEventArgs(character))) MarkDirtyAndNotify(); }
    internal bool SurfaceEditingText => activeTextAnnotation?.IsEditing == true;
    internal void SurfaceFinalizeText() => FinalizeActiveTextAnnotation();
    internal void SurfaceSetZoom(decimal zoom) { pictureBox1.ZoomAround(zoom, new Point(pictureBox1.Width / 2, pictureBox1.Height / 2)); _zoomlevel = pictureBox1.ZoomLevel; }
    internal bool SurfaceResizeImage(Size size, bool nearest) => ExecuteResizeImage(size, nearest ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic);
    internal void SurfaceAddEmoji(string emoji) => AddEmojiAtClientPoint(emoji, new Point(pictureBox1.Width / 2, pictureBox1.Height / 2));
    internal void SurfaceSuspendText()
    {
        if (activeTextAnnotation != null && !activeTextAnnotation.IsValid())
        {
            textAnnotationSnapshotBeforeEdit = null;
            FinalizeActiveTextAnnotation(); SyncSelectedTextAnnotation();
        }
        else SuspendTextEditingForUiFocus();
    }
    internal void SurfaceApplyTool(bool apply)
    {
        switch (CurrentTool)
        {
            case ActiveTool.Straighten: DeactivateStraightenTool(apply); break;
            case ActiveTool.FreeRotate: DeactivateFreeRotateTool(apply); break;
            case ActiveTool.Censor: DeactivateCensorTool(apply); break;
        }
        pictureBox1.Invalidate();
    }
    internal void SurfaceAngle(float angle) { freeRotateAngleDeg = angle; UpdateRotateToolbarState(); pictureBox1.Invalidate(); }
    internal float SurfaceAngleValue => freeRotateAngleDeg;
    internal bool SurfaceCanApply => CurrentTool switch { ActiveTool.Straighten => straightenApplyButton.Enabled, ActiveTool.Censor => censorRegions.Any(r => r.Selected), _ => true };
    internal string SurfaceInspectorKind => CurrentTool is ActiveTool.Straighten or ActiveTool.Censor or ActiveTool.FreeRotate ? CurrentTool.ToString() : selectedTexts.Count > 0 || CurrentTool == ActiveTool.Text ? "Text" : selectedAnnotation?.Type.ToString() ?? (HasSelectedLayer ? "Layer" : CurrentTool.ToString());
    internal Color SurfaceColor => SurfaceInspectorKind == "Text" ? selectedTextAnnotation?.TextColor ?? textToolColor : GetRepresentativeSelectionColor() ?? ActiveToolDefaultColor;
    internal float SurfaceWidth => selectedAnnotation?.LineThickness ?? (CurrentTool == ActiveTool.Highlighter ? annotationHighlighterThickness : annotationLineThickness);
    internal decimal SurfaceArrowSize => selectedAnnotation?.ArrowSize ?? annotationArrowSize;
    internal float SurfaceOpacity => selectedAnnotation?.Opacity ?? annotationHighlighterOpacity;
    internal string[] SurfaceFontChoices
    {
        get
        {
            EnsureFontChoicesLoaded();
            return fontComboBox.Items.Cast<string>().Concat(fontVariantMap!.Values.SelectMany(variants => variants.Select(variant => variant.FullName))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
        }
    }
    internal string SurfaceFont => selectedTextAnnotation?.FontFamily ?? textToolFontFamily;
    internal float SurfaceFontSize => selectedTextAnnotation?.FontSize ?? textToolFontSize;
    internal float SurfaceOutline => selectedTextAnnotation?.OutlineThickness ?? textToolOutlineThickness;
    internal Color SurfaceOutlineColor => selectedTextAnnotation?.OutlineColor ?? textToolOutlineColor;
    internal FontStyle SurfaceFontStyle => selectedTextAnnotation?.FontStyle ?? textToolFontStyle;
    internal void SurfaceStyle(string property, object value)
    {
        if (SurfaceInspectorKind == "Text")
        {
            bool unchanged = property switch { "Color" => value.Equals(SurfaceColor), "Font" => value.Equals(SurfaceFont), "Size" => value.Equals(SurfaceFontSize), "Outline" => value.Equals(SurfaceOutline), "OutlineColor" => value.Equals(SurfaceOutlineColor), "Style" => value.Equals(SurfaceFontStyle), _ => false };
            if (unchanged) return;
            var before = CloneTextAnnotations();
            foreach (var text in selectedTexts)
            {
                switch (property)
                {
                    case "Color": text.TextColor = (Color)value; break;
                    case "Font": text.FontFamily = (string)value; break;
                    case "Size": text.FontSize = (float)value; break;
                    case "Outline": text.OutlineThickness = (float)value; break;
                    case "OutlineColor": text.OutlineColor = (Color)value; break;
                    case "Style": text.FontStyle = (FontStyle)value; break;
                }
            }
            switch (property)
            {
                case "Color": textToolColor = (Color)value; break;
                case "Font": textToolFontFamily = (string)value; textToolFontVariant = null; break;
                case "Size": textToolFontSize = (float)value; break;
                case "Outline": textToolOutlineThickness = (float)value; break;
                case "OutlineColor": textToolOutlineColor = (Color)value; break;
                case "Style": textToolFontStyle = (FontStyle)value; break;
            }
            if (selectedTexts.Count > 0) PushTextUndoStep(before, CloneTextAnnotations());
            SaveTextToolSettings();
        }
        else
        {
            switch (property)
            {
                case "Color": ActiveToolDefaultColor = (Color)value; ApplyColorToSelection((Color)value); break;
                case "Width":
                    if (SurfaceInspectorKind == "Highlighter") { annotationHighlighterThickness = (float)value; ApplyHighlighterThicknessToSelection((float)value); }
                    else { annotationLineThickness = (float)value; ApplyLineThicknessToSelection((float)value); }
                    break;
                case "Arrow": annotationArrowSize = Convert.ToDecimal(value); ApplyArrowSizeToSelection(annotationArrowSize); break;
                case "Opacity": annotationHighlighterOpacity = (float)value; ApplyHighlighterOpacityToSelection((float)value); break;
            }
        }
        pictureBox1.Invalidate();
    }
}

public partial class ImageEditor
{
    internal Bitmap? SurfaceCopyBase() { FinalizeActiveTextAnnotation(); return pictureBox1.Image == null ? null : new Bitmap(pictureBox1.Image); }
    internal long SurfaceRevision => deJpegRevision;
    internal int SurfaceCensorDirection => (int)censorDirection;
    internal int SurfaceCensorIterations => censorIterations;
    internal int SurfaceCensorSmear => censorSmear;
    internal void SurfaceCensorSettings(int? direction, int? iterations, int? smear)
    {
        if (direction.HasValue) censorDirection = (CensorDirection)direction.Value;
        if (iterations.HasValue) censorIterations = iterations.Value;
        if (smear.HasValue) censorSmear = smear.Value;
        UpdateCensorToolbarState(); RebuildCensorPreviewAfterParamChange();
    }
    internal void SurfaceSelectCensor(bool select) { if (select) selectAllToolStripButton_Click(this, EventArgs.Empty); else selectNoneToolStripButton_Click(this, EventArgs.Empty); }
    internal int SurfaceLayerCount => imageLayers.Count;
    internal ImageLayer? SurfaceSelectedLayer => HasSelectedLayer ? imageLayers[selectedLayerIndex] : null;
    internal void SurfaceSelectLayer(int index)
    {
        if (index < 0 || index >= imageLayers.Count) return;
        SelectLayerFromPanel(imageLayers[index]);
    }
    internal void SurfaceLayerProperty(string property, float value)
    {
        switch (property)
        {
            case "X": ApplySelectedLayerPosition(value, true); break;
            case "Y": ApplySelectedLayerPosition(value, false); break;
            case "Width": ApplySelectedLayerDimension(value, true); break;
            case "Height": ApplySelectedLayerDimension(value, false); break;
            case "Angle": ApplySelectedLayerAngle(value); break;
        }
    }
    internal void SurfaceColorCorrection(float exposure, float contrast, float saturation, float gamma)
    {
        if (pictureBox1.Image == null) return;
        FinalizeActiveTextAnnotation();
        using var source = new Bitmap(pictureBox1.Image);
        using var result = ColorCorrectionProcessor.Apply(source, Selection, exposure, contrast, saturation, gamma);
        ApplyDeJpeg(result, deJpegRevision); // Same guarded full-canvas replacement and undo contract.
    }
}

public partial class ImageEditor
{
    internal Action<string, string>? SurfaceNotification { get; set; }
    private void NotifySurface(string message, string title, MessageBoxIcon icon)
    {
        if (externalSurface && SurfaceNotification != null) SurfaceNotification(title, message);
        else MessageBox.Show(this, message, title, MessageBoxButtons.OK, icon);
    }
    internal string[] SurfaceEmojiTiles => emojiRecentStore.Tiles;
    internal bool SurfaceDropEmoji(string emoji, Point point) => AddEmojiAtClientPoint(emoji, point);
    internal bool SurfaceDropImage(Bitmap image, Point point) => AddFloatingImageLayer(image, pictureBox1.ClientToPixel(point));
    internal ImageLayer SurfaceLayerAt(int index) => imageLayers[index];
    internal bool SurfaceLayerAspectLock { get => LayerAspectRatioLocked; set { if (layerAspectLockCheckBox != null) layerAspectLockCheckBox.Checked = value; } }
    internal void SurfaceLayerAction(int index, string action)
    {
        if (index < 0 || index >= imageLayers.Count) return;
        var layer = imageLayers[index];
        switch (action)
        {
            case "Show": ToggleLayerMuteFromPanel(layer); break;
            case "Merge": ApplyLayerFromPanel(layer); break;
            case "Delete": DeleteLayerFromPanel(layer); break;
            case "Up": MoveLayerToRowSlot(layer, Math.Max(0, imageLayers.Count - 2 - index)); break;
            case "Down": MoveLayerToRowSlot(layer, Math.Min(imageLayers.Count - 1, imageLayers.Count - index)); break;
        }
    }
    internal Rectangle SurfaceSelection => Selection;
    internal Bitmap SurfacePreviewComposite(Bitmap image)
    {
        var result = new Bitmap(image);
        using var graphics = Graphics.FromImage(result);
        DrawImageLayers(graphics, AnnotationSurface.Image); DrawAnnotations(graphics, AnnotationSurface.Image); DrawTextAnnotations(graphics, AnnotationSurface.Image);
        return result;
    }
    internal bool SurfaceSaveAs(string path) => SaveImageAs(path);
}
