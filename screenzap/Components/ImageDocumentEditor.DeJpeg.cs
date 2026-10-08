using System.Drawing;
using System.Windows.Forms;

namespace screenzap;
public partial class ImageDocumentEditor
{
    private long deJpegRevision;
    internal long DeJpegRevisionForTests => deJpegRevision;

    internal bool ApplyDeJpeg(Bitmap result, long expectedRevision)
    {
        if (IsDisposed || !HasEditableImage || viewport.Image == null || deJpegRevision != expectedRevision || result.Size != viewport.Image.Size)
            return false;
        var before = new Bitmap(viewport.Image);
        var after = new Bitmap(result);
        CopyImageResolution(viewport.Image, after);
        var shapesBefore = CloneAnnotations();
        var textsBefore = CloneTextAnnotations();
        var layersBefore = CloneLayers();
        var view = viewport.Metrics;
        viewport.Image = new Bitmap(after);
        viewport.RestoreView(view.ZoomLevel, view.PanOffset, viewport.AlphaViewEnabled);
        PushUndoStep(Rectangle.Empty, before, after, Selection, Selection, replacesImage: true, shapesBefore: shapesBefore, shapesAfter: CloneAnnotations(), textsBefore: textsBefore, textsAfter: CloneTextAnnotations(), layersBefore: layersBefore, layersAfter: CloneLayers());
        viewport.Invalidate();
        return true;
    }
}
