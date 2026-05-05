using System;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Tests for layer rotation: render (verified indirectly via RotationDeg state), hit-testing
    /// on rotated bodies, rotation handle interaction, and undo/redo.
    /// </summary>
    public class ImageLayerRotationTests
    {
        private static screenzap.ImageEditor MakeEditorWithLayer(int layerW = 40, int layerH = 20)
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(120, 80);
            using (var g = Graphics.FromImage(canvas))
                g.Clear(Color.White);
            editor.LoadImage(canvas);
            canvas.Dispose();

            var layerBmp = new Bitmap(layerW, layerH);
            using (var g = Graphics.FromImage(layerBmp))
                g.Clear(Color.OrangeRed);
            editor.SetInternalClipboardImageForDiagnostics(layerBmp);
            layerBmp.Dispose();
            Assert.True(editor.PasteFromClipboardForDiagnostics());

            return editor;
        }

        [Fact]
        public void Layer_DefaultRotation_IsZero()
        {
            StaTest.Run(() =>
            {
                using var editor = MakeEditorWithLayer();
                Assert.Equal(0f, editor.TestGetLayerRotationDeg(0));
            });
        }

        [Fact]
        public void SetRotation_Roundtrips()
        {
            StaTest.Run(() =>
            {
                using var editor = MakeEditorWithLayer();
                editor.TestSetLayerRotationDeg(0, 45f);
                Assert.Equal(45f, editor.TestGetLayerRotationDeg(0));
            });
        }

        [Fact]
        public void RotatedLayer_BodyClick_SelectsCorrectly()
        {
            StaTest.Run(() =>
            {
                using var editor = MakeEditorWithLayer(40, 20);
                // Rotate 90°: the 40-wide extent is now vertical, 10-tall extent horizontal.
                editor.TestSetLayerRotationDeg(0, 90f);

                var f = editor.GetImageLayerFrameForTests(0);
                int cx = (int)(f.X + f.Width / 2);
                int cy = (int)(f.Y + f.Height / 2);

                // Deselect first.
                editor.TestFireMouseDownAtImagePixel(new Point(0, 0), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(0, 0), MouseButtons.Left);
                Assert.Equal(-1, editor.SelectedLayerIndexForTests);

                // A point inside the rotated body — within the half-width (10px) along the
                // *original* X axis, which is now the Y axis. (cx, cy+8) is inside the rotated frame.
                editor.TestFireMouseDownAtImagePixel(new Point(cx, cy + 8), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(cx, cy + 8), MouseButtons.Left);
                Assert.Equal(0, editor.SelectedLayerIndexForTests);
            });
        }

        [Fact]
        public void RotatedLayer_PointOutsideBody_DoesNotSelect()
        {
            StaTest.Run(() =>
            {
                using var editor = MakeEditorWithLayer(40, 20);
                editor.TestSetLayerRotationDeg(0, 90f);

                var f = editor.GetImageLayerFrameForTests(0);
                int cx = (int)(f.X + f.Width / 2);
                int cy = (int)(f.Y + f.Height / 2);

                // Deselect.
                editor.TestFireMouseDownAtImagePixel(new Point(0, 0), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(0, 0), MouseButtons.Left);

                // A point that would be inside the unrotated AABB but outside the *rotated* body.
                // At 90°, the frame corners have moved. A corner of the old AABB (cx+15, cy+5)
                // is now outside the rotated body (the rotated half-width along original X = 10px).
                editor.TestFireMouseDownAtImagePixel(new Point(cx + 15, cy + 5), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(cx + 15, cy + 5), MouseButtons.Left);
                Assert.Equal(-1, editor.SelectedLayerIndexForTests);
            });
        }

        [Fact]
        public void RotationHandleDrag_ChangesRotationDeg()
        {
            StaTest.Run(() =>
            {
                using var editor = MakeEditorWithLayer(40, 20);

                // Select the layer first.
                var f = editor.GetImageLayerFrameForTests(0);
                int cx = (int)(f.X + f.Width / 2);
                int cy = (int)(f.Y + f.Height / 2);

                // The rotation handle is above top-center in image pixels (zoom=1).
                // Top center = (cx, f.Y). Handle offset = 28px.
                var rotHandle = new Point(cx, (int)(f.Y - 28));

                // Begin interaction at the rotation handle position.
                bool began = editor.BeginLayerInteractionForTests(rotHandle);
                Assert.True(began);
                Assert.Equal(screenzap.ImageLayerHandle.Rotate, editor.TestActiveLayerHandle);

                // Drag to the right from top: this is roughly a 90° rotation.
                int radius = (int)(f.Height / 2f + 28);
                editor.UpdateLayerInteractionForTests(new Point(cx + radius, cy));
                editor.EndLayerInteractionForTests();

                float rot = editor.TestGetLayerRotationDeg(0);
                // Expect ~90° (within 5° tolerance for integer pixel rounding).
                Assert.True(Math.Abs(rot - 90f) < 5f, $"Expected ~90° but got {rot}°");
            });
        }

        [Fact]
        public void RotationDrag_ThenUndo_RestoresZero()
        {
            StaTest.Run(() =>
            {
                using var editor = MakeEditorWithLayer(40, 20);

                var f = editor.GetImageLayerFrameForTests(0);
                int cx = (int)(f.X + f.Width / 2);
                int cy = (int)(f.Y + f.Height / 2);
                var rotHandle = new Point(cx, (int)(f.Y - 28));

                editor.BeginLayerInteractionForTests(rotHandle);
                int radius = (int)(f.Height / 2f + 28);
                editor.UpdateLayerInteractionForTests(new Point(cx + radius, cy));
                editor.EndLayerInteractionForTests();

                Assert.NotEqual(0f, editor.TestGetLayerRotationDeg(0));

                // Undo should restore 0°.
                editor.TestFireKeyDown(Keys.Control | Keys.Z);
                Assert.Equal(0f, editor.TestGetLayerRotationDeg(0));
            });
        }
    }
}
