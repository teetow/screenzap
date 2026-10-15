using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Undo/redo is a document operation, not a camera one. Steps that snapshot the whole base
    /// bitmap (every layer edit, crop, expand, rotate…) used to go through the viewport's Image
    /// setter and then CenterImage, so undoing snapped zoom, pan and the alpha view back to
    /// their defaults.
    /// </summary>
    public class UndoKeepsViewTests
    {
        private static screenzap.ImageDocumentEditor PannedAndZoomed()
        {
            var editor = EditorFixture.WithCanvas(new Size(400, 300), viewportSize: new Size(640, 480));
            editor.TestSetZoom(2m);
            editor.TestPanViewportBy(new Size(-150, -90));
            return editor;
        }

        [Fact]
        public void UndoingAndRedoingALayerPaste_KeepsZoomPanAndAlphaView()
        {
            StaTest.Run(() =>
            {
                using var editor = PannedAndZoomed();
                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.ToggleTransparencyGrid));
                Assert.False(editor.TestAlphaViewEnabled);

                using var pasted = EditorFixture.Canvas(20, 20, Color.Red);
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());
                var view = editor.TestViewportMetrics;

                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(view.ZoomLevel, editor.TestViewportMetrics.ZoomLevel);
                Assert.Equal(view.PanOffset, editor.TestViewportMetrics.PanOffset);
                Assert.False(editor.TestAlphaViewEnabled);

                Assert.True(presenter.TryExecute(EditorCommandId.Redo));
                Assert.Equal(view.ZoomLevel, editor.TestViewportMetrics.ZoomLevel);
                Assert.Equal(view.PanOffset, editor.TestViewportMetrics.PanOffset);
                Assert.False(editor.TestAlphaViewEnabled);
            });
        }

        [Fact]
        public void UndoingACanvasSizeChange_KeepsZoomAndTheImageCentreInPlace()
        {
            StaTest.Run(() =>
            {
                using var editor = PannedAndZoomed();
                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.ExpandCanvas));

                // Pan away from wherever the forward op left the camera, so a recentre shows.
                editor.TestPanViewportBy(new Size(-70, -40));
                var expanded = editor.TestViewportMetrics;

                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                var undone = editor.TestViewportMetrics;
                Assert.Equal(expanded.ZoomLevel, undone.ZoomLevel);
                Assert.Equal(new Size(400, 300), undone.ImagePixelSize);

                var centreBefore = Centre(expanded.ImageClientRectangle);
                var centreAfter = Centre(undone.ImageClientRectangle);
                Assert.Equal(centreBefore.X, centreAfter.X, 0);
                Assert.Equal(centreBefore.Y, centreAfter.Y, 0);
            });
        }

        private static PointF Centre(RectangleF r) => new(r.X + r.Width / 2f, r.Y + r.Height / 2f);
    }
}
