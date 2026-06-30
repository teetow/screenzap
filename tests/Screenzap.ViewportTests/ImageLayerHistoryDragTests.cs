using System.Drawing;
using System.Windows.Forms;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageLayerHistoryDragTests
    {
        [Fact]
        public void HistoryDragPayload_UsesFullResolutionComposite()
        {
            StaTest.Run(() =>
            {
                using var source = new Bitmap(24, 12);
                using (var g = Graphics.FromImage(source))
                {
                    g.Clear(Color.Red);
                }

                using var composite = new Bitmap(24, 12);
                using (var g = Graphics.FromImage(composite))
                {
                    g.Clear(Color.Blue);
                }

                using var item = ClipboardHistoryItem.FromImage(source);
                item.SetPreviewComposite(composite);
                using var payload = ClipboardHistoryImageDragPayload.Create(item);

                Assert.NotNull(payload);
                Assert.Equal(source.Size, payload!.Image.Size);
                Assert.Equal(Color.Blue.ToArgb(), payload.Image.GetPixel(12, 6).ToArgb());

                var data = payload.CreateDataObject();
                Assert.True(ClipboardHistoryImageDragPayload.TryGetImage(data, out var dragged));
                Assert.Same(payload.Image, dragged);
            });
        }

        [Fact]
        public void DroppingHistoryImage_CreatesSelectedLayerAtDropPoint_AndSupportsUndo()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(100, 80);
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.White);
                }
                editor.LoadImage(canvas);

                using var draggedBitmap = new Bitmap(20, 10);
                using (var g = Graphics.FromImage(draggedBitmap))
                {
                    g.Clear(Color.Blue);
                }
                using var item = ClipboardHistoryItem.FromImage(draggedBitmap);
                using var payload = ClipboardHistoryImageDragPayload.Create(item);
                var data = payload!.CreateDataObject();

                Assert.True(editor.DropHistoryImageForTests(data, new Point(70, 30)));
                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.Equal(0, editor.SelectedLayerIndexForTests);
                Assert.Equal(new RectangleF(60f, 25f, 20f, 10f), editor.GetImageLayerFrameForTests(0));

                using (var baseImage = editor.CloneBaseBitmapForTests()!)
                {
                    Assert.Equal(Color.White.ToArgb(), baseImage.GetPixel(70, 30).ToArgb());
                }
                using (var compositeImage = editor.BuildCompositeImageForTests())
                {
                    Assert.Equal(Color.Blue.ToArgb(), compositeImage.GetPixel(70, 30).ToArgb());
                }

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(0, editor.ImageLayerCountForTests);
            });
        }

        [Fact]
        public void DropRejectsUnrelatedDragData()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(40, 30);
                editor.LoadImage(canvas);

                var unrelated = new DataObject();
                unrelated.SetText("not an image history item");

                Assert.False(editor.DropHistoryImageForTests(unrelated, new Point(20, 15)));
                Assert.Equal(0, editor.ImageLayerCountForTests);
            });
        }
    }
}
