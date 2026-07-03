using System.Drawing;
using System.Windows.Forms;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageLayerToolbarAndCropTests
    {
        private static screenzap.ImageEditor PrepareEditor(out RectangleF frame)
        {
            var editor = new screenzap.ImageEditor();
            using var canvas = new Bitmap(80, 60);
            editor.LoadImage(canvas);

            using var pasted = new Bitmap(20, 14);
            editor.SetInternalClipboardImageForDiagnostics(pasted);
            Assert.True(editor.PasteFromClipboardForDiagnostics());
            frame = editor.GetImageLayerFrameForTests(0);
            return editor;
        }

        [Fact]
        public void Paste_ShowsDedicatedLayerToolbar()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out _);
                editor.Show();
                Application.DoEvents();

                Assert.True(editor.LayerToolbarAvailableForTests);
                Assert.True(editor.LayerToolbarShownForTests);
                Assert.True(editor.LayerRotationInputAvailableForTests);

                editor.SetSelectedLayerForTests(-1);
                Assert.False(editor.LayerToolbarShownForTests);
            });
        }

        [Fact]
        public void ToolbarDimensions_RespectAspectLock_AndAreUndoable()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out var original);
                editor.SetLayerAspectLockForTests(true);

                editor.SetSelectedLayerHeightForTests(28f);

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(40f, resized.Width);
                Assert.Equal(28f, resized.Height);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                var restored = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(original, restored);
            });
        }

        [Fact]
        public void AspectLock_AppliesToCornerDrag()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out var original);
                editor.SetLayerAspectLockForTests(true);
                var corner = new Point((int)original.Right, (int)original.Bottom);

                Assert.True(editor.BeginLayerInteractionForTests(corner));
                editor.UpdateLayerInteractionForTests(new Point(corner.X + 10, corner.Y + 2));
                editor.EndLayerInteractionForTests();

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(30f, resized.Width);
                Assert.Equal(21f, resized.Height);
            });
        }

        [Fact]
        public void CornerDrag_PreservesAspectByDefault()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out var original);
                var corner = new Point((int)original.Right, (int)original.Bottom);

                Assert.True(editor.BeginLayerInteractionForTests(corner));
                editor.UpdateLayerInteractionForTests(new Point(corner.X + 10, corner.Y + 2));
                editor.EndLayerInteractionForTests();

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(30f, resized.Width);
                Assert.Equal(21f, resized.Height);
            });
        }

        [Fact]
        public void ShiftCornerDrag_InvertsAspectLock()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out var original);
                editor.TestSetShiftHeld(true);
                var corner = new Point((int)original.Right, (int)original.Bottom);

                Assert.True(editor.BeginLayerInteractionForTests(corner));
                editor.UpdateLayerInteractionForTests(new Point(corner.X + 10, corner.Y + 2));
                editor.EndLayerInteractionForTests();

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(30f, resized.Width);
                Assert.Equal(16f, resized.Height);
            });
        }

        [Fact]
        public void ToolbarAngle_NormalizesDegrees()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out _);

                editor.SetSelectedLayerAngleForTests(270f);

                Assert.Equal(-90f, editor.GetImageLayerRotationForTests(0));
            });
        }

        [Fact]
        public void RotatedCornerResize_PreservesOppositeVisualCorner()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out var original);
                editor.SetLayerAspectLockForTests(false);
                editor.SetSelectedLayerAngleForTests(90f);
                var fixedTopLeft = VisualCorner(original, 90f, right: false, bottom: false);
                var draggedBottomRight = VisualCorner(original, 90f, right: true, bottom: true);
                var dragStart = Point.Round(draggedBottomRight);

                Assert.True(editor.BeginLayerInteractionForTests(dragStart));
                // Local (+6,+4) rotated by 90° becomes canvas (-4,+6).
                editor.UpdateLayerInteractionForTests(new Point(dragStart.X - 4, dragStart.Y + 6));
                editor.EndLayerInteractionForTests();

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(26f, resized.Width, 3);
                Assert.Equal(18f, resized.Height, 3);
                var fixedAfterResize = VisualCorner(resized, 90f, right: false, bottom: false);
                AssertPointEqual(fixedTopLeft, fixedAfterResize);
            });
        }

        [Fact]
        public void RotatedToolbarResize_PreservesVisualTopLeft()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out var original);
                editor.SetLayerAspectLockForTests(false);
                editor.SetSelectedLayerAngleForTests(90f);
                var fixedTopLeft = VisualCorner(original, 90f, right: false, bottom: false);

                editor.SetSelectedLayerWidthForTests(30f);

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(30f, resized.Width);
                Assert.Equal(original.Height, resized.Height);
                AssertPointEqual(
                    fixedTopLeft,
                    VisualCorner(resized, 90f, right: false, bottom: false));
            });
        }

        [Fact]
        public void CtrlDragHandle_CropsWithoutRescaling_AndUndoRestores()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out var original);
                var rightHandle = new Point(
                    (int)original.Right,
                    (int)(original.Top + original.Height / 2f));

                Assert.True(editor.BeginLayerInteractionForTests(rightHandle));
                editor.SetLayerCropModifierForTests(true);
                editor.UpdateLayerInteractionForTests(new Point(rightHandle.X - 6, rightHandle.Y));
                editor.SetLayerCropModifierForTests(false);
                editor.EndLayerInteractionForTests();

                var croppedFrame = editor.GetImageLayerFrameForTests(0);
                var croppedFill = editor.GetImageLayerFillForTests(0);
                Assert.Equal(original.X, croppedFrame.X);
                Assert.Equal(original.Width - 6f, croppedFrame.Width);
                Assert.Equal(14f, croppedFill.Width);
                Assert.Equal(14f, croppedFill.Height);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(original, editor.GetImageLayerFrameForTests(0));
                Assert.Equal(new RectangleF(0f, 0f, 20f, 14f), editor.GetImageLayerFillForTests(0));
            });
        }

        [Fact]
        public void CtrlDragOutward_RevealsPreviouslyCroppedSource()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out var original);
                var leftHandle = new Point(
                    (int)original.Left,
                    (int)(original.Top + original.Height / 2f));

                Assert.True(editor.BeginLayerInteractionForTests(leftHandle));
                editor.SetLayerCropModifierForTests(true);
                editor.UpdateLayerInteractionForTests(new Point(leftHandle.X + 5, leftHandle.Y));
                editor.SetLayerCropModifierForTests(false);
                editor.EndLayerInteractionForTests();

                var cropped = editor.GetImageLayerFrameForTests(0);
                var croppedLeftHandle = new Point(
                    (int)cropped.Left,
                    (int)(cropped.Top + cropped.Height / 2f));
                Assert.True(editor.BeginLayerInteractionForTests(croppedLeftHandle));
                editor.SetLayerCropModifierForTests(true);
                editor.UpdateLayerInteractionForTests(new Point(croppedLeftHandle.X - 20, croppedLeftHandle.Y));
                editor.SetLayerCropModifierForTests(false);
                editor.EndLayerInteractionForTests();

                Assert.Equal(original, editor.GetImageLayerFrameForTests(0));
                Assert.Equal(new RectangleF(0f, 0f, 20f, 14f), editor.GetImageLayerFillForTests(0));
            });
        }

        [Fact]
        public void Reset_RestoresOriginalDimensionsFullSourceAndRotation()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor(out var original);
                var leftHandle = new Point(
                    (int)original.Left,
                    (int)(original.Top + original.Height / 2f));

                Assert.True(editor.BeginLayerInteractionForTests(leftHandle));
                editor.SetLayerCropModifierForTests(true);
                editor.UpdateLayerInteractionForTests(new Point(leftHandle.X + 5, leftHandle.Y));
                editor.SetLayerCropModifierForTests(false);
                editor.EndLayerInteractionForTests();
                editor.SetSelectedLayerAngleForTests(30f);
                var croppedPosition = editor.GetImageLayerFrameForTests(0).Location;

                editor.ResetSelectedLayerForTests();

                var resetFrame = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(croppedPosition, resetFrame.Location);
                Assert.Equal(20f, resetFrame.Width);
                Assert.Equal(14f, resetFrame.Height);
                Assert.Equal(new RectangleF(0f, 0f, 20f, 14f), editor.GetImageLayerFillForTests(0));
                Assert.Equal(0f, editor.GetImageLayerRotationForTests(0));
            });
        }

        private static PointF VisualCorner(RectangleF frame, float degrees, bool right, bool bottom)
        {
            float localX = (right ? 1f : -1f) * frame.Width / 2f;
            float localY = (bottom ? 1f : -1f) * frame.Height / 2f;
            double radians = degrees * System.Math.PI / 180.0;
            float cos = (float)System.Math.Cos(radians);
            float sin = (float)System.Math.Sin(radians);
            return new PointF(
                frame.X + frame.Width / 2f + localX * cos - localY * sin,
                frame.Y + frame.Height / 2f + localX * sin + localY * cos);
        }

        private static void AssertPointEqual(PointF expected, PointF actual)
        {
            Assert.InRange(System.Math.Abs(expected.X - actual.X), 0f, 0.001f);
            Assert.InRange(System.Math.Abs(expected.Y - actual.Y), 0f, 0.001f);
        }
    }
}
