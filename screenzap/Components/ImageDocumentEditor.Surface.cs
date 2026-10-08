using screenzap.lib;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace screenzap;
// Production boundary between document editing and the window that presents it. No native
// child HWND is embedded in WinUI: the existing renderer supplies pixels and accepts input.
public partial class ImageDocumentEditor
{
    private Point pointerPosition;
    private Point CurrentPointerInViewport() => pointerPosition;
    internal event Action? SurfaceInvalidated;
    internal Action? CanvasFocusRequested { get; set; }
    internal string SurfaceCursor => Cursor == EditorCursor.WaitCursor ? "Wait" : Cursor == EditorCursor.Cross ? "Cross" : Cursor == EditorCursor.IBeam ? "IBeam" : Cursor == EditorCursor.Hand ? "Hand" : Cursor == EditorCursor.SizeAll ? "SizeAll" : Cursor == EditorCursor.SizeNS ? "SizeNorthSouth" : Cursor == EditorCursor.SizeWE ? "SizeWestEast" : Cursor == EditorCursor.SizeNWSE ? "SizeNorthwestSoutheast" : Cursor == EditorCursor.SizeNESW ? "SizeNortheastSouthwest" : "Arrow";
    internal Size SurfaceViewportSize => viewport.ClientSize;
    internal Size SurfaceImageSize => viewport.Image?.Size ?? Size.Empty;
    internal decimal SurfaceZoom => viewport.ZoomLevel;
    internal bool SurfaceTransparency => viewport.AlphaViewEnabled;

    private void RequestCanvasFocus() => CanvasFocusRequested?.Invoke();
    internal void ResizeSurface(Size size)
    {
        if (size.Width > 0 && size.Height > 0)
            viewport.ClientSize = size;
    }

    internal void RenderSurface(Graphics graphics) => viewport.Render(graphics);
    internal void SurfacePointer(int phase, Point point, MouseButtons button, int clicks = 1)
    {
        pointerPosition = point;
        if (phase == 0)
            pointerButtons = button;
        if (phase == 2)
            pointerButtons = MouseButtons.None;
        var args = new MouseEventArgs(button, clicks, point.X, point.Y, 0);
        switch (phase)
        {
            case 0:
                ViewportMouseDown(viewport, args);
                break;
            case 1:
                ViewportMouseMove(viewport, args);
                break;
            case 2:
                ViewportMouseUp(viewport, args);
                break;
            case 3:
                ViewportMouseDoubleClick(viewport, args);
                break;
        }

        SurfaceInvalidated?.Invoke();
    }

    internal void SurfacePointerExit()
    {
        SetHoveredAnnotation(null);
        SetHoveredAnnotationHandle(AnnotationHandle.None);
        SetHoveredTextAnnotation(null);
        Cursor = EditorCursor.Default;
    }

    internal void SurfaceWheel(Point point, int delta)
    {
        viewport.ZoomAround(delta > 0 ? FindZoomIn(SurfaceZoom) : FindZoomOut(SurfaceZoom), point);
        _zoomlevel = viewport.ZoomLevel;
    }

    internal bool SurfaceKey(Keys keys, bool up = false)
    {
        inputModifiers = keys & Keys.Modifiers;
        if (up)
        {
            HandleKeyUp(this, new KeyEventArgs(keys));
            return false;
        }

        if (HandleNavigationKey(keys))
            return true;
        string? beforeText = activeTextAnnotation?.Text;
        var args = new KeyEventArgs(keys);
        HandleKeyDown(this, args);
        if (beforeText != null && activeTextAnnotation != null && beforeText != activeTextAnnotation.Text)
            MarkDirtyAndNotify();
        return args.Handled || args.SuppressKeyPress;
    }

    internal void SurfaceCharacter(char character)
    {
        if (HandleTextToolKeyPress(new KeyPressEventArgs(character)))
            MarkDirtyAndNotify();
    }

    internal bool SurfaceEditingText => activeTextAnnotation?.IsEditing == true;

    internal void SurfaceFinalizeText() => FinalizeActiveTextAnnotation();
    internal void SurfaceSetZoom(decimal zoom)
    {
        viewport.ZoomAround(zoom, new Point(viewport.Width / 2, viewport.Height / 2));
        _zoomlevel = viewport.ZoomLevel;
    }

    internal bool SurfaceResizeImage(Size size, bool nearest) => ExecuteResizeImage(size, nearest ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic);
    internal void SurfaceAddEmoji(string emoji) => AddEmojiAtClientPoint(emoji, new Point(viewport.Width / 2, viewport.Height / 2));
    internal void SurfaceSuspendText()
    {
        if (activeTextAnnotation != null && !activeTextAnnotation.IsValid())
        {
            textAnnotationSnapshotBeforeEdit = null;
            FinalizeActiveTextAnnotation();
            SyncSelectedTextAnnotation();
        }
        else
            SuspendTextEditingForUiFocus();
    }

    internal void SurfaceApplyTool(bool apply)
    {
        switch (CurrentTool)
        {
            case ActiveTool.Straighten:
                DeactivateStraightenTool(apply);
                break;
            case ActiveTool.FreeRotate:
                DeactivateFreeRotateTool(apply);
                break;
            case ActiveTool.Censor:
                DeactivateCensorTool(apply);
                break;
        }

        viewport.Invalidate();
    }

    internal void SurfaceAngle(float angle)
    {
        freeRotateAngleDeg = angle;
        viewport.Invalidate();
    }

    internal float SurfaceAngleValue => freeRotateAngleDeg;
    internal bool SurfaceCanApply => CurrentTool switch
    {
        ActiveTool.Straighten => lib.ImageStraightener.IsValidPerspectiveQuad(straightenCorners),
        ActiveTool.Censor => censorRegions.Any(r => r.Selected),
        _ => true
    };
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
            return availableFontChoices.Concat(fontVariantMap!.Values.SelectMany(variants => variants.Select(variant => variant.FullName))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
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
            bool unchanged = property switch
            {
                "Color" => value.Equals(SurfaceColor),
                "Font" => value.Equals(SurfaceFont),
                "Size" => value.Equals(SurfaceFontSize),
                "Outline" => value.Equals(SurfaceOutline),
                "OutlineColor" => value.Equals(SurfaceOutlineColor),
                "Style" => value.Equals(SurfaceFontStyle),
                _ => false
            };
            if (unchanged)
                return;
            var before = CloneTextAnnotations();
            foreach (var text in selectedTexts)
            {
                switch (property)
                {
                    case "Color":
                        text.TextColor = (Color)value;
                        break;
                    case "Font":
                        text.FontFamily = (string)value;
                        break;
                    case "Size":
                        text.FontSize = (float)value;
                        break;
                    case "Outline":
                        text.OutlineThickness = (float)value;
                        break;
                    case "OutlineColor":
                        text.OutlineColor = (Color)value;
                        break;
                    case "Style":
                        text.FontStyle = (FontStyle)value;
                        break;
                }
            }

            switch (property)
            {
                case "Color":
                    textToolColor = (Color)value;
                    break;
                case "Font":
                    textToolFontFamily = (string)value;
                    textToolFontVariant = null;
                    break;
                case "Size":
                    textToolFontSize = (float)value;
                    break;
                case "Outline":
                    textToolOutlineThickness = (float)value;
                    break;
                case "OutlineColor":
                    textToolOutlineColor = (Color)value;
                    break;
                case "Style":
                    textToolFontStyle = (FontStyle)value;
                    break;
            }

            if (selectedTexts.Count > 0)
                PushTextUndoStep(before, CloneTextAnnotations());
            SaveTextToolSettings();
        }
        else
        {
            switch (property)
            {
                case "Color":
                    ActiveToolDefaultColor = (Color)value;
                    ApplyColorToSelection((Color)value);
                    break;
                case "Width":
                    if (SurfaceInspectorKind == "Highlighter")
                    {
                        annotationHighlighterThickness = (float)value;
                        ApplyHighlighterThicknessToSelection((float)value);
                    }
                    else
                    {
                        annotationLineThickness = (float)value;
                        ApplyLineThicknessToSelection((float)value);
                    }

                    break;
                case "Arrow":
                    annotationArrowSize = Convert.ToDecimal(value);
                    ApplyArrowSizeToSelection(annotationArrowSize);
                    break;
                case "Opacity":
                    annotationHighlighterOpacity = (float)value;
                    ApplyHighlighterOpacityToSelection((float)value);
                    break;
            }
        }

        viewport.Invalidate();
    }
}

public partial class ImageDocumentEditor
{
    internal Bitmap? SurfaceCopyBase()
    {
        FinalizeActiveTextAnnotation();
        return viewport.Image == null ? null : new Bitmap(viewport.Image);
    }

    internal long SurfaceRevision => deJpegRevision;
    internal int SurfaceCensorDirection => (int)censorDirection;
    internal int SurfaceCensorIterations => censorIterations;
    internal int SurfaceCensorSmear => censorSmear;

    internal void SurfaceCensorSettings(int? direction, int? iterations, int? smear)
    {
        if (direction.HasValue)
            censorDirection = (CensorDirection)direction.Value;
        if (iterations.HasValue)
            censorIterations = iterations.Value;
        if (smear.HasValue)
            censorSmear = smear.Value;
        RebuildCensorPreviewAfterParamChange();
    }

    internal void SurfaceSelectCensor(bool select)
    {
        if (select)
            SelectAllCensorRegions();
        else
            DeselectCensorRegions();
    }

    internal int SurfaceLayerCount => imageLayers.Count;
    internal ImageLayer? SurfaceSelectedLayer => HasSelectedLayer ? imageLayers[selectedLayerIndex] : null;

    internal void SurfaceSelectLayer(int index)
    {
        SelectLayerFromPanel(index >= 0 && index < imageLayers.Count ? imageLayers[index] : null);
    }

    internal void SurfaceLayerProperty(string property, float value)
    {
        switch (property)
        {
            case "X":
                ApplySelectedLayerPosition(value, true);
                break;
            case "Y":
                ApplySelectedLayerPosition(value, false);
                break;
            case "Width":
                ApplySelectedLayerDimension(value, true);
                break;
            case "Height":
                ApplySelectedLayerDimension(value, false);
                break;
            case "Angle":
                ApplySelectedLayerAngle(value);
                break;
        }
    }

    internal void SurfaceColorCorrection(float exposure, float contrast, float saturation, float gamma)
    {
        if (viewport.Image == null)
            return;
        FinalizeActiveTextAnnotation();
        using var source = new Bitmap(viewport.Image);
        using var result = ColorCorrectionProcessor.Apply(source, Selection, exposure, contrast, saturation, gamma);
        ApplyDeJpeg(result, deJpegRevision); // Same guarded full-canvas replacement and undo contract.
    }
}

public partial class ImageDocumentEditor
{
    internal Action<string, string>? SurfaceNotification { get; set; }

    private void NotifySurface(string message, string title) => SurfaceNotification?.Invoke(title, message);
    internal string[] SurfaceEmojiTiles => emojiRecentStore.Tiles;

    internal bool SurfaceDropEmoji(string emoji, Point point) => AddEmojiAtClientPoint(emoji, point);
    internal bool SurfaceDropImage(Bitmap image, Point point) => AddFloatingImageLayer(image, viewport.ClientToPixel(point));
    internal bool SurfaceMoveLayer(int index, int slot) => index >= 0 && index < imageLayers.Count && MoveLayerToRowSlot(imageLayers[index], slot);
    internal ImageLayer SurfaceLayerAt(int index) => imageLayers[index];
    internal bool SurfaceLayerAspectLock { get => LayerAspectRatioLocked; set => layerAspectRatioLocked = value; }

    internal void SurfaceLayerAction(int index, string action)
    {
        if (index < 0 || index >= imageLayers.Count)
            return;
        var layer = imageLayers[index];
        switch (action)
        {
            case "Show":
                ToggleLayerMuteFromPanel(layer);
                break;
            case "Merge":
                ApplyLayerFromPanel(layer);
                break;
            case "Delete":
                DeleteLayerFromPanel(layer);
                break;
            case "Up":
                MoveLayerToRowSlot(layer, Math.Max(0, imageLayers.Count - 2 - index));
                break;
            case "Down":
                MoveLayerToRowSlot(layer, Math.Min(imageLayers.Count - 1, imageLayers.Count - index));
                break;
        }
    }

    internal Rectangle SurfaceSelection => Selection;

    internal Bitmap SurfacePreviewComposite(Bitmap image)
    {
        var result = new Bitmap(image);
        using var graphics = Graphics.FromImage(result);
        DrawImageLayers(graphics, AnnotationSurface.Image);
        DrawAnnotations(graphics, AnnotationSurface.Image);
        DrawTextAnnotations(graphics, AnnotationSurface.Image);
        return result;
    }

    internal bool SurfaceSaveAs(string path) => SaveImageAs(path);
    internal bool SurfaceReloadConfirmed() => TryReloadImageFromClipboard();
    internal void SurfaceModifiers(Keys modifiers) => inputModifiers = modifiers;
}
