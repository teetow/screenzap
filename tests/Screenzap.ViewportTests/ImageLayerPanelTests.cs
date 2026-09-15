using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// The ephemeral layers panel: it exists only while something is floating, lists the stack
    /// top-most first over a Background row, and each row's eye / check / cross act on that row
    /// alone.
    /// </summary>
    public class ImageLayerPanelTests
    {
        private static void PasteBlockAt(
            screenzap.ImageEditor editor,
            Color color,
            int width,
            int height,
            float x,
            float y)
        {
            using var block = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(block))
            {
                graphics.Clear(color);
            }

            editor.SetInternalClipboardImageForDiagnostics(block);
            Assert.True(editor.PasteFromClipboardForDiagnostics());
            editor.SetSelectedLayerXForTests(x);
            editor.SetSelectedLayerYForTests(y);
        }

        [Fact]
        public void Panel_IsEphemeral_ArrivesWithTheFirstPasteAndLeavesWithTheLast()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(60, 40);
                Assert.True(editor.LayersPanelAvailableForTests);
                Assert.False(editor.LayersPanelShownForTests);

                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                Assert.True(editor.LayersPanelShownForTests);

                Assert.True(editor.ApplyFloatingPasteForTests());
                Assert.Equal(0, editor.ImageLayerCountForTests);
                Assert.False(editor.LayersPanelShownForTests);
            });
        }

        [Fact]
        public void Panel_ListsLayersTopMostFirst_OverABackgroundRow()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 8, 8, 20f, 20f);

                // Newest paste is the top of the stack, so it heads the list.
                Assert.Equal(
                    new[] { "Paste 2", "Paste 1", "Background" },
                    editor.LayersPanelCaptionsForTests);
                Assert.Equal(3, editor.LayersPanelRowCountForTests);
            });
        }

        [Fact]
        public void Panel_BackgroundRow_DropsTheLayerSelection()
        {
            StaTest.Run(() =>
            {
                // The reason to want a panel mid-paste: get back to the canvas without
                // discarding what is floating over it.
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                Assert.Equal(0, editor.SelectedLayerIndexForTests);

                editor.ClickLayerPanelRowForTests(-1);

                Assert.Equal(-1, editor.SelectedLayerIndexForTests);
                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.True(editor.LayersPanelShownForTests);
            });
        }

        [Fact]
        public void Panel_Mute_TakesTheLayerOutOfTheComposite_AndUndoBringsItBack()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);

                using (var lit = editor.BuildCompositeImageForTests())
                {
                    Assert.Equal(Color.Lime.ToArgb(), lit.GetPixel(4, 4).ToArgb());
                }

                editor.ClickLayerPanelMuteForTests(0);
                Assert.False(editor.IsImageLayerVisibleForTests(0));
                Assert.True(editor.LayerPanelMuteShowsMutedForTests(0));

                // Muting is content, not a view toggle: a save or copy loses it too.
                using (var muted = editor.BuildCompositeImageForTests())
                {
                    Assert.Equal(Color.White.ToArgb(), muted.GetPixel(4, 4).ToArgb());
                }

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.True(editor.IsImageLayerVisibleForTests(0));
            });
        }

        [Fact]
        public void Panel_MutedLayer_CannotBeGluedDown()
        {
            StaTest.Run(() =>
            {
                // Gluing down something you cannot see would drop invisible pixels into the
                // canvas, so the row's check is disabled and the action is inert.
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                editor.ClickLayerPanelMuteForTests(0);

                Assert.False(editor.LayerPanelCommitEnabledForTests(0));

                editor.ClickLayerPanelCommitForTests(0);
                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.False(editor.ApplyFloatingPasteForTests());
            });
        }

        [Fact]
        public void Panel_Commit_GluesDownThatRowAlone()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 8, 8, 40f, 20f);

                // Glue down the *lower* row, which is not the selected one.
                editor.ClickLayerPanelCommitForTests(0);

                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.Equal(new[] { "Paste 2", "Background" }, editor.LayersPanelCaptionsForTests);

                using var baked = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(Color.Lime.ToArgb(), baked.GetPixel(4, 4).ToArgb());
                Assert.Equal(Color.White.ToArgb(), baked.GetPixel(44, 24).ToArgb());
            });
        }

        [Fact]
        public void Panel_Delete_DiscardsThatRowAlone_AndIsUndoable()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 8, 8, 40f, 20f);

                editor.ClickLayerPanelDeleteForTests(0);

                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.Equal(new[] { "Paste 2", "Background" }, editor.LayersPanelCaptionsForTests);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(2, editor.ImageLayerCountForTests);
                Assert.Equal(
                    new[] { "Paste 2", "Paste 1", "Background" },
                    editor.LayersPanelCaptionsForTests);
            });
        }

        [Fact]
        public void Panel_DragRowToBottom_RestacksTheLayers_AndIsUndoable()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 8, 8, 20f, 20f);
                PasteBlockAt(editor, Color.Cyan, 8, 8, 40f, 10f);

                Assert.Equal(
                    new[] { "Paste 3", "Paste 2", "Paste 1", "Background" },
                    editor.LayersPanelCaptionsForTests);

                // Drag the top row down into the gap just above Background.
                Assert.True(editor.DragLayerRowToSlotForTests(2, 3));

                Assert.Equal(
                    new[] { "Paste 2", "Paste 1", "Paste 3", "Background" },
                    editor.LayersPanelCaptionsForTests);
                // The dragged row keeps the selection through the move.
                Assert.Equal(0, editor.SelectedLayerIndexForTests);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(
                    new[] { "Paste 3", "Paste 2", "Paste 1", "Background" },
                    editor.LayersPanelCaptionsForTests);
            });
        }

        [Fact]
        public void Panel_DragRow_ChangesWhichLayerDrawsOnTop()
        {
            StaTest.Run(() =>
            {
                // Two layers over the same pixels: restacking has to change the composite, not
                // just the list.
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 10, 10, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 10, 10, 0f, 0f);

                using (var before = editor.BuildCompositeImageForTests())
                {
                    Assert.Equal(Color.Magenta.ToArgb(), before.GetPixel(5, 5).ToArgb());
                }

                // Send the magenta row (currently top) to the bottom of the stack.
                Assert.True(editor.DragLayerRowToSlotForTests(1, 2));

                using (var after = editor.BuildCompositeImageForTests())
                {
                    Assert.Equal(Color.Lime.ToArgb(), after.GetPixel(5, 5).ToArgb());
                }
            });
        }

        [Fact]
        public void Panel_DragRow_CannotSinkBelowBackground()
        {
            StaTest.Run(() =>
            {
                // Background is the canvas; nothing floats under it. A drop aimed past it
                // clamps to the last gap instead of doing something surprising.
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 8, 8, 20f, 20f);

                Assert.True(editor.DragLayerRowToSlotForTests(1, 99));

                Assert.Equal(
                    new[] { "Paste 1", "Paste 2", "Background" },
                    editor.LayersPanelCaptionsForTests);
            });
        }

        [Fact]
        public void Panel_DragRow_OntoItsOwnSlot_ChangesNothing()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 8, 8, 20f, 20f);

                var presenter = (IClipboardDocumentPresenter)editor;
                bool couldUndoBefore = presenter.CanExecute(EditorCommandId.Undo);

                // Slot 0 is where the top row already is, as is slot 1 once its own removal
                // is accounted for. Neither may push an undo step.
                Assert.False(editor.DragLayerRowToSlotForTests(1, 0));
                Assert.False(editor.DragLayerRowToSlotForTests(1, 1));

                Assert.Equal(
                    new[] { "Paste 2", "Paste 1", "Background" },
                    editor.LayersPanelCaptionsForTests);
                Assert.Equal(couldUndoBefore, presenter.CanExecute(EditorCommandId.Undo));
            });
        }

        [Fact]
        public void Panel_DropSlot_IsTheNearestGapAndStopsAtBackground()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 8, 8, 20f, 20f);

                // Rows are 34px tall, so a gap is nearest from 17px either side of it.
                Assert.Equal(0, editor.LayerRowDropSlotForTests(0));
                Assert.Equal(0, editor.LayerRowDropSlotForTests(16));
                Assert.Equal(1, editor.LayerRowDropSlotForTests(18));
                Assert.Equal(1, editor.LayerRowDropSlotForTests(34));
                Assert.Equal(2, editor.LayerRowDropSlotForTests(68));
                // Past the Background row it clamps rather than running off the end.
                Assert.Equal(2, editor.LayerRowDropSlotForTests(400));
            });
        }

        [Fact]
        public void Panel_MutedLayer_IsNotClickableOnCanvas()
        {
            StaTest.Run(() =>
            {
                // Nothing on screen, nothing to hit: a muted layer must not eat clicks aimed
                // at whatever is behind it.
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 20, 20, 0f, 0f);
                editor.ClickLayerPanelMuteForTests(0);
                editor.ClickLayerPanelRowForTests(-1);

                Assert.False(editor.BeginLayerInteractionForTests(new Point(10, 10)));
            });
        }
    }
}
