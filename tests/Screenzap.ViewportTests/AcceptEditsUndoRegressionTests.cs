using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using screenzap;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests;

public class AcceptEditsUndoRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptThenUndo_RestoresBaseBitmap_ForTextAndEmoji(bool emoji)
    {
        StaTest.Run(() =>
        {
            using var editor = new ImageEditor();
            using var host = new ClipboardEditorHostForm(true, editor)
            {
                SuppressActivation = true,
                ShowInTaskbar = false,
                Location = new Point(-32000, -32000)
            };
            host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());
            using var original = EditorFixture.Canvas(300, 200);
            var item = host.HistoryStore.AddObservedImage(original);
            host.Show();
            Assert.True(host.ActivateHistoryItem(item));
            EditorFixture.PinModifiers(editor);
            Application.DoEvents();

            var recentPath = Path.Combine(Path.GetTempPath(), "Screenzap-accept-emoji-" + Guid.NewGuid() + ".json");
            try
            {
                if (emoji)
                {
                    editor.TestSetEmojiRecentStore(new EmojiRecentStore(recentPath));
                    Assert.True(editor.AddEmojiAtClientPoint("😀", editor.TestImagePixelToClient(new Point(150, 100))));
                }
                else
                {
                    editor.TestToggleTextTool();
                    Assert.True(editor.TestHandleTextToolMouseDown(new Point(20, 20)));
                    foreach (var character in "Hello") Assert.True(editor.TestHandleTextToolKeyPress(character));
                    editor.TestToggleTextTool();
                }
                Assert.True(item.IsDirty);
                Assert.Single(editor.TestTextAnnotations);
                using var edited = editor.BuildCompositeImageForTests();
                Assert.False(EqualPixels(original, edited));

                Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.CommitEdits));
                Assert.False(item.IsDirty);
                Assert.Empty(editor.TestTextAnnotations);
                using (var accepted = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(edited, accepted);

                Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
                using (var afterUndo = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(original, afterUndo);
                Assert.Single(editor.TestTextAnnotations);
                Assert.True(item.IsDirty);
                using (var composite = editor.BuildCompositeImageForTests()) AssertPixelsEqual(edited, composite);

                // Earlier text undo must now run against the original, unflattened bitmap.
                Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
                Assert.Empty(editor.TestTextAnnotations);
                using (var composite = editor.BuildCompositeImageForTests()) AssertPixelsEqual(original, composite);
                Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Redo));
                Assert.Single(editor.TestTextAnnotations);
                Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Redo));
                Assert.Empty(editor.TestTextAnnotations);
                using (var acceptedAgain = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(edited, acceptedAgain);
            }
            finally
            {
                if (File.Exists(recentPath)) File.Delete(recentPath);
            }
        });
    }

    [Fact]
    public void AcceptMixedDocument_UndoRestoresRotatedBaseAndAllOverlays_AfterHistorySwitch()
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(220, 160);
            using (var graphics = Graphics.FromImage(source)) graphics.FillRectangle(Brushes.Blue, 0, 0, 30, 40);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            var item = host.HistoryStore.ActiveItem!;
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.RotateRight));
            using var rotatedBase = editor.CloneBaseBitmapForTests()!;
            editor.TestToggleRectTool();
            editor.TestFireMouseDownAtImagePixel(new Point(10, 10), MouseButtons.Left);
            editor.TestFireMouseMoveAtImagePixel(new Point(80, 45), MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(new Point(80, 45), MouseButtons.Left);
            editor.TestDeactivateDrawingTool();
            using var pasted = EditorFixture.Canvas(24, 24, Color.FromArgb(120, 0, 200, 0));
            editor.SetInternalClipboardImageForDiagnostics(pasted);
            Assert.True(editor.PasteFromClipboardForDiagnostics());
            AddText(editor, new Point(20, 70), "Mixed");
            Assert.Equal(1, editor.TestAnnotationShapeCount);
            Assert.Equal(1, editor.ImageLayerCountForTests);
            using var edited = editor.BuildCompositeImageForTests();
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.CommitEdits));

            // Exercise the stashed undo stack, including the layer bitmap's ownership.
            using var otherSource = EditorFixture.Canvas(40, 30, Color.Teal);
            var other = host.HistoryStore.AddObservedImage(otherSource);
            Assert.True(host.ActivateHistoryItem(other));
            Assert.True(host.ActivateHistoryItem(item));
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(rotatedBase, restored);
            Assert.Equal(1, editor.TestAnnotationShapeCount);
            Assert.Equal(1, editor.TestTextAnnotationCount);
            Assert.Equal(1, editor.ImageLayerCountForTests);
            using (var restored = editor.BuildCompositeImageForTests()) AssertPixelsEqual(edited, restored);

            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Redo));
            Assert.Equal(0, editor.TestAnnotationShapeCount);
            Assert.Equal(0, editor.TestTextAnnotationCount);
            Assert.Equal(0, editor.ImageLayerCountForTests);
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(edited, restored);

            // Every preceding edit can still be walked back, including the size-changing rotate.
            int undoCount = 0;
            while (host.ExecuteCommandForDiagnostics(EditorCommandId.Undo))
                Assert.True(++undoCount <= 10, "Undo did not reach the start of history.");
            Assert.Equal(5, undoCount);
            using (var restored = editor.BuildCompositeImageForTests()) AssertPixelsEqual(source, restored);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptPixelOnlyEdit_FirstUndoRestoresPriorBitmap(bool rotate)
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(220, 160);
            using (var graphics = Graphics.FromImage(source)) graphics.FillRectangle(Brushes.Blue, 0, 0, 30, 40);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            Assert.True(host.ExecuteCommandForDiagnostics(rotate ? EditorCommandId.RotateRight : EditorCommandId.FlipHorizontal));
            using var rotated = editor.CloneBaseBitmapForTests()!;
            Assert.False(EqualPixels(source, rotated));
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.CommitEdits));
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(source, restored);
            Assert.False(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Redo));
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(rotated, restored);
        });
    }

    [Fact]
    public void RepeatedAccept_UndoRedoKeepsEachBitmapBoundary()
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(300, 200);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            AddText(editor, new Point(20, 20), "First");
            using var first = editor.BuildCompositeImageForTests();
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.CommitEdits));
            AddText(editor, new Point(20, 80), "Second");
            using var second = editor.BuildCompositeImageForTests();
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.CommitEdits));

            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(first, restored);
            Assert.Equal("Second", Assert.Single(editor.TestTextAnnotations).Text);
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
            using (var restored = editor.BuildCompositeImageForTests()) AssertPixelsEqual(first, restored);
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(source, restored);
            Assert.Equal("First", Assert.Single(editor.TestTextAnnotations).Text);
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
            using (var restored = editor.BuildCompositeImageForTests()) AssertPixelsEqual(source, restored);
            for (int i = 0; i < 4; i++) Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Redo));
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(second, restored);
            Assert.Empty(editor.TestTextAnnotations);
        });
    }

    [Fact]
    public void AcceptAfterUndo_DropsStaleRedoAndRestoresCorrectDocument()
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(300, 200);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            AddText(editor, new Point(20, 20), "First");
            AddText(editor, new Point(20, 80), "Second");
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
            Assert.Equal("First", Assert.Single(editor.TestTextAnnotations).Text);
            using var first = editor.BuildCompositeImageForTests();
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.CommitEdits));
            Assert.False(host.ExecuteCommandForDiagnostics(EditorCommandId.Redo));
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Undo));
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(source, restored);
            Assert.Equal("First", Assert.Single(editor.TestTextAnnotations).Text);
            Assert.True(host.ExecuteCommandForDiagnostics(EditorCommandId.Redo));
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(first, restored);
            Assert.Empty(editor.TestTextAnnotations);
        });
    }

    private static ClipboardEditorHostForm CreateHost(ImageEditor editor, Bitmap source)
    {
        var host = new ClipboardEditorHostForm(true, editor)
        {
            SuppressActivation = true,
            ShowInTaskbar = false,
            Location = new Point(-32000, -32000)
        };
        host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());
        var item = host.HistoryStore.AddObservedImage(source);
        host.Show();
        Assert.True(host.ActivateHistoryItem(item));
        EditorFixture.PinModifiers(editor);
        Application.DoEvents();
        return host;
    }

    private static void AddText(ImageEditor editor, Point position, string text)
    {
        editor.TestToggleTextTool();
        Assert.True(editor.TestHandleTextToolMouseDown(position));
        foreach (var character in text) Assert.True(editor.TestHandleTextToolKeyPress(character));
        editor.TestToggleTextTool();
    }

    private static bool EqualPixels(Bitmap expected, Bitmap actual)
    {
        if (expected.Size != actual.Size) return false;
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                if (expected.GetPixel(x, y).ToArgb() != actual.GetPixel(x, y).ToArgb()) return false;
        return true;
    }

    private static void AssertPixelsEqual(Bitmap expected, Bitmap actual)
    {
        Assert.Equal(expected.Size, actual.Size);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.True(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Bitmap differs at ({x}, {y}).");
    }
}
