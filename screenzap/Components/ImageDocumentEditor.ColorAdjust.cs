using screenzap.lib;
using System.Drawing;

namespace screenzap;

internal readonly record struct ColorAdjustment(float Exposure, float Contrast, float Saturation, float Gamma)
{
    internal static readonly ColorAdjustment Neutral = new(0, 0, 100, 1);
    internal bool IsNeutral => this == Neutral;
}

// Color correction is a live adjustment over the document, not a tool and not a dialog. While
// it is open the viewport shows the corrected pixels in place of the image; the document is
// untouched until Apply. The preview is derived from whatever the image and selection are at
// paint time, so selecting, undoing or cropping underneath keeps it truthful.
public partial class ImageDocumentEditor
{
    private ColorAdjustment? colorAdjustment;
    private Bitmap? colorPreview;
    private (ColorAdjustment Adjustment, Image Image, long Revision, Rectangle Area) colorPreviewKey;

    internal bool SurfaceColorAdjusting => colorAdjustment.HasValue;
    internal ColorAdjustment SurfaceColorAdjustment => colorAdjustment ?? ColorAdjustment.Neutral;
    internal bool SurfaceColorAdjustsSelection => !ClampToImage(ColorAdjustmentArea).IsEmpty;

    // The area the adjustment covers is whatever the marquee shows right now, including a
    // rubber band still being dragged, so reshaping the selection is part of tweaking.
    private Rectangle ColorAdjustmentArea => isDrawingRubberBand ? ClampToImage(GetNormalizedRect(MouseInPixel, MouseOutPixel)) : Selection;

    internal bool BeginColorAdjustment()
    {
        if (!HasEditableImage)
            return false;
        FinalizeActiveTextAnnotation();
        colorAdjustment ??= ColorAdjustment.Neutral;
        viewport.Invalidate();
        return true;
    }

    internal void SurfaceSetColorAdjustment(ColorAdjustment adjustment)
    {
        if (!colorAdjustment.HasValue || colorAdjustment == adjustment)
            return;
        colorAdjustment = adjustment;
        viewport.Invalidate();
    }

    /// <summary>Close the adjustment, baking it into the image as one undo step when applied.</summary>
    internal void SurfaceEndColorAdjustment(bool apply)
    {
        if (colorAdjustment is not { } adjustment)
            return;
        colorAdjustment = null;
        ReleaseColorPreview();
        if (apply && !adjustment.IsNeutral && viewport.Image != null)
        {
            FinalizeActiveTextAnnotation();
            using var source = new Bitmap(viewport.Image);
            using var result = ColorCorrectionProcessor.Apply(source, Selection, adjustment.Exposure, adjustment.Contrast, adjustment.Saturation, adjustment.Gamma);
            ApplyDeJpeg(result, deJpegRevision); // Same guarded full-canvas replacement and undo contract.
        }

        viewport.Invalidate();
    }

    private void RefreshColorPreview()
    {
        if (colorAdjustment is not { } adjustment || adjustment.IsNeutral || viewport.Image is not Bitmap image)
        {
            ReleaseColorPreview();
            return;
        }

        var area = ColorAdjustmentArea;
        var key = (adjustment, (Image)image, deJpegRevision, area);
        if (colorPreview != null && colorPreviewKey == key)
            return;
        ReleaseColorPreview();
        colorPreview = ColorCorrectionProcessor.Apply(image, area, adjustment.Exposure, adjustment.Contrast, adjustment.Saturation, adjustment.Gamma);
        colorPreviewKey = key;
        viewport.PreviewImage = colorPreview;
    }

    private void ReleaseColorPreview()
    {
        if (colorPreview == null)
            return;
        viewport.PreviewImage = null;
        colorPreview.Dispose();
        colorPreview = null;
        colorPreviewKey = default;
    }
}
