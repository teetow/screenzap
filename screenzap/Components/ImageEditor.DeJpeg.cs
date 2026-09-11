using System.Drawing;
using System.Windows.Forms;

namespace screenzap;

public partial class ImageEditor
{
    private ToolStripButton? deJpegButton;
    private long deJpegRevision;

    internal long DeJpegRevisionForTests => deJpegRevision;

    private void InitializeDeJpegCommand()
    {
        deJpegButton = new ToolStripButton("De-JPEG")
        {
            Name = "deJpegButton", Enabled = false,
            ToolTipText = "Remove JPEG artifacts"
        };
        deJpegButton.Click += (_, _) => ShowDeJpegDialog();
        mainToolStrip.Items.Add(deJpegButton);
    }

    private void ShowDeJpegDialog()
    {
        if (!HasEditableImage || pictureBox1.Image == null) return;
        FinalizeActiveTextAnnotation();
        long revision = deJpegRevision;
        var identity = pictureBox1.Image;
        using var dialog = new DeJpegDialog(identity,
            () => !IsDisposed && !Disposing && deJpegRevision == revision && ReferenceEquals(identity, pictureBox1.Image));
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
            ApplyDeJpeg(dialog.Result, revision);
    }

    internal bool ApplyDeJpeg(Bitmap result, long expectedRevision)
    {
        if (IsDisposed || Disposing || !HasEditableImage || pictureBox1.Image == null
            || deJpegRevision != expectedRevision || result.Size != pictureBox1.Image.Size)
            return false;

        var before = new Bitmap(pictureBox1.Image);
        var after = new Bitmap(result);
        CopyImageResolution(pictureBox1.Image, after);
        var shapesBefore = CloneAnnotations();
        var textsBefore = CloneTextAnnotations();
        var layersBefore = CloneLayers();
        pictureBox1.Image.Dispose();
        pictureBox1.Image = new Bitmap(after);
        PushUndoStep(Rectangle.Empty, before, after, Selection, Selection, replacesImage: true,
            shapesBefore: shapesBefore, shapesAfter: CloneAnnotations(),
            textsBefore: textsBefore, textsAfter: CloneTextAnnotations(),
            layersBefore: layersBefore, layersAfter: CloneLayers());
        UpdateCommandUI();
        UpdateStatusBar();
        pictureBox1.Invalidate();
        return true;
    }
}
