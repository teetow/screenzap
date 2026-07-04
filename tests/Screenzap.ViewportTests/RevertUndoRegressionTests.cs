using System;
using System.Drawing;
using System.Windows.Forms;
using screenzap;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class RevertUndoRegressionTests
    {
        private static Bitmap CreateSolidBitmap(Color color, int width = 4, int height = 4)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(color);
            }

            return bmp;
        }

        [Fact]
        public void RevertToOriginal_AppendsUndoStep_SoRevertIsUndoable()
        {
            using var original = CreateSolidBitmap(Color.Blue);
            using var edited = CreateSolidBitmap(Color.Red);

            using var item = ClipboardHistoryItem.FromImage(original);
            item.UpdateCurrentImage(edited);
            Assert.True(item.IsDirty);

            var snapshot = new UndoRedo.Snapshot { Index = 0 };
            snapshot.Steps.Add(new ImageUndoStep(
                new Rectangle(0, 0, 4, 4),
                new Bitmap(original),
                new Bitmap(edited),
                Rectangle.Empty,
                Rectangle.Empty,
                true,
                null,
                null));
            item.UndoSnapshot = snapshot;

            item.RevertToOriginal();

            Assert.False(item.IsDirty);
            Assert.False(item.CanRevertToOriginal);
            Assert.Equal(Color.Blue.ToArgb(), item.CurrentImage!.GetPixel(0, 0).ToArgb());

            // Prior history is preserved and the revert itself is one more step on top.
            Assert.NotNull(item.UndoSnapshot);
            Assert.Equal(2, item.UndoSnapshot!.Steps.Count);
            Assert.Equal(1, item.UndoSnapshot.Index);

            var revertStep = Assert.IsType<ImageUndoStep>(item.UndoSnapshot.Steps[1]);
            Assert.True(revertStep.ReplacesImage);
            Assert.Equal(Color.Red.ToArgb(), revertStep.Before!.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.Blue.ToArgb(), revertStep.After!.GetPixel(0, 0).ToArgb());

            // After-lists must be empty (not null): redoing the revert clears annotations/layers.
            Assert.NotNull(revertStep.ShapesAfter);
            Assert.Empty(revertStep.ShapesAfter!);
            Assert.NotNull(revertStep.TextsAfter);
            Assert.Empty(revertStep.TextsAfter!);
            Assert.NotNull(revertStep.LayersAfter);
            Assert.Empty(revertStep.LayersAfter!);
        }

        [Fact]
        public void RevertToOriginal_WithoutPriorSnapshot_CreatesSingleStepSnapshot()
        {
            using var original = CreateSolidBitmap(Color.Blue);
            using var edited = CreateSolidBitmap(Color.Red);

            using var item = ClipboardHistoryItem.FromImage(original);
            item.UpdateCurrentImage(edited);
            Assert.Null(item.UndoSnapshot);

            item.RevertToOriginal();

            Assert.NotNull(item.UndoSnapshot);
            Assert.Single(item.UndoSnapshot!.Steps);
            Assert.Equal(0, item.UndoSnapshot.Index);
        }

        [Fact]
        public void RevertToOriginal_TruncatesRedoTail_LikeAnyOtherEdit()
        {
            using var original = CreateSolidBitmap(Color.Blue);
            using var edited = CreateSolidBitmap(Color.Red);

            using var item = ClipboardHistoryItem.FromImage(original);
            item.UpdateCurrentImage(edited);

            // Two steps with the second one undone (Index = 0) — a pending redo tail.
            var snapshot = new UndoRedo.Snapshot { Index = 0 };
            snapshot.Steps.Add(new ImageUndoStep(
                new Rectangle(0, 0, 4, 4),
                new Bitmap(original),
                new Bitmap(edited),
                Rectangle.Empty,
                Rectangle.Empty,
                true,
                null,
                null));
            snapshot.Steps.Add(new ImageUndoStep(
                new Rectangle(0, 0, 4, 4),
                new Bitmap(edited),
                CreateSolidBitmap(Color.Green),
                Rectangle.Empty,
                Rectangle.Empty,
                true,
                null,
                null));
            item.UndoSnapshot = snapshot;

            item.RevertToOriginal();

            // The undone (green) step is dropped; the revert step takes its place at the top.
            Assert.Equal(2, item.UndoSnapshot!.Steps.Count);
            Assert.Equal(1, item.UndoSnapshot.Index);
            var top = Assert.IsType<ImageUndoStep>(item.UndoSnapshot.Steps[1]);
            Assert.Equal(Color.Blue.ToArgb(), top.After!.GetPixel(0, 0).ToArgb());
        }

        [Fact]
        public void Paste_ThenRevert_UndoRestoresLayer_RedoRevertsAgain()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(40, 30);
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.White);
                }
                editor.LoadImage(canvas);

                using var pasted = new Bitmap(8, 8);
                using (var g = Graphics.FromImage(pasted))
                {
                    g.Clear(Color.Magenta);
                }
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());
                Assert.Equal(1, editor.ImageLayerCountForTests);

                // Simulate the host revert cycle: stash → revert → reload.
                var presenter = (IClipboardDocumentPresenter)editor;
                using var item = ClipboardHistoryItem.FromImage(canvas);

                presenter.StashHistoryItemState(item);
                item.RevertToOriginal();
                presenter.LoadHistoryItem(item);

                // Post-revert: layer gone, base back to the original canvas.
                Assert.Equal(0, editor.ImageLayerCountForTests);
                using (var afterRevert = editor.CloneBaseBitmapForTests()!)
                {
                    Assert.Equal(Color.White.ToArgb(), afterRevert.GetPixel(20, 15).ToArgb());
                }

                // Undo brings the pasted layer back and notifies the host so Commit/Revert re-enable.
                int contentEditedNotifications = 0;
                editor.ContentEditedCallback = () => contentEditedNotifications++;

                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.True(contentEditedNotifications > 0);
                using (var composite = editor.BuildCompositeImageForTests())
                {
                    Assert.Equal(Color.Magenta.ToArgb(), composite.GetPixel(20, 15).ToArgb());
                }

                // Redo re-applies the revert.
                Assert.True(presenter.CanExecute(EditorCommandId.Redo));
                Assert.True(presenter.TryExecute(EditorCommandId.Redo));
                Assert.Equal(0, editor.ImageLayerCountForTests);
                using (var afterRedo = editor.CloneBaseBitmapForTests()!)
                {
                    Assert.Equal(Color.White.ToArgb(), afterRedo.GetPixel(20, 15).ToArgb());
                }
            });
        }

        [Fact]
        public void HostRevertCommand_IsUndoable_AndUndoRedirtiesItem()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var host = new ClipboardEditorHostForm(true, editor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false,
                    Opacity = 0
                };

                host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());
                using var source = new Bitmap(40, 30);
                using (var g = Graphics.FromImage(source))
                {
                    g.Clear(Color.White);
                }
                var item = host.HistoryStore.AddObservedImage(source);

                host.Show();
                Application.DoEvents();

                // Activate through the host so the presenter is loaded and host services attach.
                Assert.True(host.ActivateHistoryItem(item));
                Application.DoEvents();
                Assert.Same(item, host.HistoryStore.ActiveItem);

                using var pasted = new Bitmap(8, 8);
                using (var g = Graphics.FromImage(pasted))
                {
                    g.Clear(Color.Magenta);
                }
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());
                Application.DoEvents();

                Assert.True(item.IsDirty);
                Assert.True(item.CanRevertToOriginal);

                // Revert through the real host command pipeline (stash → revert → reload).
                Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Revert));
                Application.DoEvents();

                Assert.False(item.IsDirty);
                Assert.False(item.CanRevertToOriginal);
                Assert.Equal(0, editor.ImageLayerCountForTests);

                // Undo through the host: the layer returns and the item re-dirties,
                // which re-enables the Commit/Revert buttons.
                Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
                Application.DoEvents();

                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.True(item.IsDirty);
                Assert.True(item.CanRevertToOriginal);
            });
        }
    }
}
