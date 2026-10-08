using System;
using System.Drawing;
using System.IO;
using System.Linq;
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
    public void CommitTextOrEmoji_ExportsComposite_KeepsEditableObjectAndBase(bool emoji)
    {
        StaTest.Run(() =>
        {
            using var original = EditorFixture.Canvas(300, 200);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, original);
            var item = host.HistoryStore.ActiveItem!;
            var recentPath = Path.Combine(Path.GetTempPath(), "Screenzap-export-emoji-" + Guid.NewGuid() + ".json");
            try
            {
                if (emoji)
                {
                    editor.TestSetEmojiRecentStore(new EmojiRecentStore(recentPath));
                    Assert.True(editor.AddEmojiAtClientPoint("😀", editor.TestImagePixelToClient(new Point(150, 100))));
                }
                else AddText(editor, new Point(20, 20), "Hello");
                var annotation = Assert.Single(editor.TestTextAnnotations);
                using var edited = editor.BuildCompositeImageForTests();
                Assert.False(EqualPixels(original, edited));
                int writes = 0;
                host.ClipboardImageWriterForDiagnostics = bitmap =>
                {
                    AssertPixelsEqual(edited, (Bitmap)bitmap);
                    writes++;
                    return true;
                };
                Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
                Assert.Equal(1, writes);
                Assert.False(item.IsDirty);
                Assert.Same(annotation, Assert.Single(editor.TestTextAnnotations));
                using (var baseImage = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(original, baseImage);
                AssertPixelsEqual(edited, item.CommittedImage!);
                AssertPixelsEqual(original, item.CurrentImage!);
                Assert.Single(item.Overlay!.Texts);
                Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
                Assert.Empty(editor.TestTextAnnotations);
                Assert.True(item.IsDirty);
                Assert.False(host.ExecuteHostCommand(EditorCommandId.Undo));
                Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
                Assert.Single(editor.TestTextAnnotations);
                Assert.False(item.IsDirty);
                using var composite = editor.BuildCompositeImageForTests();
                AssertPixelsEqual(edited, composite);
            }
            finally { if (File.Exists(recentPath)) File.Delete(recentPath); }
        });
    }

    [Fact]
    public void CommitMixedDocument_SwitchHistory_RestoresAllObjectsAndCheckpoint()
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(220, 160);
            using (var g = Graphics.FromImage(source)) g.FillRectangle(Brushes.Blue, 0, 0, 30, 40);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            var item = host.HistoryStore.ActiveItem!;
            Assert.True(host.ExecuteHostCommand(EditorCommandId.RotateRight));
            using var rotatedBase = editor.CloneBaseBitmapForTests()!;
            AddRectangle(editor);
            using var pasted = EditorFixture.Canvas(24, 24, Color.FromArgb(120, 0, 200, 0));
            editor.SetInternalClipboardImageForDiagnostics(pasted);
            Assert.True(editor.PasteFromClipboardForDiagnostics());
            AddText(editor, new Point(20, 70), "Mixed");
            using var edited = editor.BuildCompositeImageForTests();
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            using var otherSource = EditorFixture.Canvas(40, 30, Color.Teal);
            var other = host.HistoryStore.AddObservedImage(otherSource);
            Assert.True(host.ActivateHistoryItem(other));
            Assert.True(host.ActivateHistoryItem(item));
            Assert.False(item.IsDirty);
            Assert.Equal(1, editor.TestAnnotationShapeCount);
            Assert.Equal(1, editor.TestTextAnnotationCount);
            Assert.Equal(1, editor.ImageLayerCountForTests);
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(rotatedBase, restored);
            using (var restored = editor.BuildCompositeImageForTests()) AssertPixelsEqual(edited, restored);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.Equal(0, editor.TestTextAnnotationCount);
            Assert.Equal(1, editor.ImageLayerCountForTests);
            Assert.True(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.False(item.IsDirty);
            int undoCount = 0;
            while (host.ExecuteHostCommand(EditorCommandId.Undo))
                Assert.True(++undoCount <= 10);
            Assert.Equal(4, undoCount);
            using var initial = editor.BuildCompositeImageForTests();
            AssertPixelsEqual(source, initial);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommitPixelEdit_DoesNotAddUndo_AndRedoRestoresClean(bool rotate)
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(220, 160);
            using (var g = Graphics.FromImage(source)) g.FillRectangle(Brushes.Blue, 0, 0, 30, 40);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            var item = host.HistoryStore.ActiveItem!;
            Assert.True(host.ExecuteHostCommand(rotate ? EditorCommandId.RotateRight : EditorCommandId.FlipHorizontal));
            using var edited = editor.CloneBaseBitmapForTests()!;
            Assert.False(EqualPixels(source, edited));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.True(item.IsDirty);
            using (var restored = editor.CloneBaseBitmapForTests()!) AssertPixelsEqual(source, restored);
            Assert.False(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.False(item.IsDirty);
            using var redone = editor.CloneBaseBitmapForTests()!;
            AssertPixelsEqual(edited, redone);
        });
    }

    [Fact]
    public void RepeatedCommit_OnlyLatestExportIsClean_WithoutExtraUndoSteps()
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(300, 200);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            var item = host.HistoryStore.ActiveItem!;
            AddText(editor, new Point(20, 20), "First");
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            AddText(editor, new Point(20, 80), "Second");
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.Equal("First", Assert.Single(editor.TestTextAnnotations).Text);
            Assert.True(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.Empty(editor.TestTextAnnotations);
            Assert.False(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.True(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.False(item.IsDirty);
            Assert.Equal(2, editor.TestTextAnnotationCount);
        });
    }

    [Fact]
    public void CommitAfterUndo_PreservesRedo_AndUndoRedoFindNewCheckpoint()
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(300, 200);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            var item = host.HistoryStore.ActiveItem!;
            AddText(editor, new Point(20, 20), "First");
            AddText(editor, new Point(20, 80), "Second");
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.False(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.Equal(2, editor.TestTextAnnotationCount);
            Assert.True(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.False(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.True(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.False(item.IsDirty);
        });
    }

    [Fact]
    public void EditAfterUndo_AtSameHistoryIndex_DoesNotReuseOldCleanRevision()
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(300, 200);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            var item = host.HistoryStore.ActiveItem!;
            AddText(editor, new Point(20, 20), "Old branch");
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            AddText(editor, new Point(20, 80), "New branch");
            Assert.False(host.CanExecuteHostCommand(EditorCommandId.Redo));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.True(item.IsDirty);
            Assert.Equal("New branch", Assert.Single(editor.TestTextAnnotations).Text);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCommit_KeepsObjectsDirtyAndRedo_AndCanRetry(bool throws)
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(300, 200);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            var item = host.HistoryStore.ActiveItem!;
            AddText(editor, new Point(20, 20), "First");
            AddText(editor, new Point(20, 80), "Second");
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            var annotation = Assert.Single(editor.TestTextAnnotations);
            using var committedBefore = new Bitmap(item.CommittedImage!);
            var checkpointBefore = item.CommittedRevision;
            host.ClipboardImageWriterForDiagnostics = _ => throws ? throw new InvalidOperationException("busy") : false;
            Assert.False(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.True(item.IsDirty);
            Assert.Equal(checkpointBefore, item.CommittedRevision);
            Assert.Same(annotation, Assert.Single(editor.TestTextAnnotations));
            Assert.True(host.CanExecuteHostCommand(EditorCommandId.Redo));
            AssertPixelsEqual(committedBefore, item.CommittedImage!);
            host.ClipboardImageWriterForDiagnostics = _ => true;
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.False(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.True(item.IsDirty);
            Assert.Equal(2, editor.TestTextAnnotationCount);
        });
    }

    [Fact]
    public void CommitWhileTyping_KeepsCaretAndTool_AndContinuedTypingHasItsOwnUndo()
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(300, 200);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            var item = host.HistoryStore.ActiveItem!;
            host.ExecuteHostCommand(EditorCommandId.TextTool);
            editor.SurfacePointer(0, new Point(80, 70), MouseButtons.Left);
            editor.SurfacePointer(2, new Point(80, 70), MouseButtons.Left);
            editor.SurfaceCharacter('A');
            var annotation = Assert.Single(editor.TestTextAnnotations);
            var tool = editor.CurrentTool;
            Assert.True(editor.SurfaceEditingText);
            host.ClipboardImageWriterForDiagnostics = _ => false;
            Assert.False(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.False(host.CanExecuteHostCommand(EditorCommandId.Undo));
            Assert.True(item.IsDirty);
            host.ClipboardImageWriterForDiagnostics = _ => true;
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.Same(annotation, Assert.Single(editor.TestTextAnnotations));
            Assert.True(editor.SurfaceEditingText);
            Assert.Equal(1, annotation.CaretPosition);
            Assert.Equal(tool, editor.CurrentTool);
            Assert.False(item.IsDirty);
            editor.SurfaceCharacter('B');
            Assert.True(item.IsDirty);
            Assert.Equal("AB", annotation.Text);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.Equal("A", Assert.Single(editor.TestTextAnnotations).Text);
            Assert.False(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.Empty(editor.TestTextAnnotations);
            Assert.True(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.False(item.IsDirty);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Redo));
            Assert.True(item.IsDirty);
        });
    }

    [Fact]
    public void CommittedEditableDocument_PersistsBaseObjectsExportAndCleanState()
    {
        StaTest.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "Screenzap-export-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            try
            {
                using var original = EditorFixture.Canvas(300, 200);
                using var editor = new ImageEditor();
                using var host = CreateHost(editor, original);
                var item = host.HistoryStore.ActiveItem!;
                AddRectangle(editor);
                using var paste = EditorFixture.Canvas(20, 15, Color.FromArgb(120, 20, 100, 50));
                editor.SurfaceDropImage(paste, new Point(150, 150));
                AddText(editor, new Point(20, 80), "Persist");
                using var composite = editor.BuildCompositeImageForTests();
                Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
                new ClipboardHistoryPersistence(root).Save(host.HistoryStore.Items, item);
                var restored = new ClipboardHistoryPersistence(root).Load();
                try
                {
                    var reopened = Assert.Single(restored.Items);
                    Assert.False(reopened.IsDirty);
                    AssertPixelsEqual(original, reopened.CurrentImage!);
                    AssertPixelsEqual(composite, reopened.CommittedImage!);
                    Assert.Single(reopened.Overlay!.Shapes);
                    Assert.Single(reopened.Overlay.Texts);
                    Assert.Single(reopened.Overlay.Layers);
                    using var freshEditor = new ImageEditor();
                    using var freshHost = CreateHost(freshEditor, original);
                    freshHost.HistoryStore.ReplaceAll(restored.Items);
                    Assert.True(freshHost.ActivateHistoryItem(reopened));
                    Assert.False(freshHost.CanExecuteHostCommand(EditorCommandId.CommitEdits));
                    using (var frame = freshEditor.BuildCompositeImageForTests()) AssertPixelsEqual(composite, frame);
                    Assert.True(freshHost.ExecuteHostCommand(EditorCommandId.RotateRight));
                    Assert.True(reopened.IsDirty);
                    Assert.True(freshHost.ExecuteHostCommand(EditorCommandId.Undo));
                    Assert.False(reopened.IsDirty);
                }
                finally { foreach (var restoredItem in restored.Items) restoredItem.Dispose(); }
            }
            finally { Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void PublishedComposite_IsReusedByClipboardObservation_WithoutFlatteningOrReload()
    {
        StaTest.Run(() =>
        {
            using var source = EditorFixture.Canvas(300, 200);
            using var editor = new ImageEditor();
            using var host = CreateHost(editor, source);
            var item = host.HistoryStore.ActiveItem!;
            AddText(editor, new Point(20, 20), "Export");
            using var composite = editor.BuildCompositeImageForTests();
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            var annotation = Assert.Single(editor.TestTextAnnotations);
            var observed = host.HistoryStore.EnsureTopObservedImage(composite);
            Assert.False(observed.Added);
            Assert.Same(item, observed.Item);
            host.OnObservedClipboardItem(observed.Item);
            Assert.Same(annotation, Assert.Single(editor.TestTextAnnotations));
            Assert.False(item.IsDirty);
            using var system = ClipboardHistoryItem.FromImage(composite);
            system.AssignSystemHistoryId("export");
            Assert.Same(item, host.TryBindPendingCommittedSystemItem(system));
            Assert.Equal("export", item.SystemHistoryId);
            using var external = EditorFixture.Canvas(80, 60, Color.Teal);
            var other = host.HistoryStore.AddObservedImage(external);
            host.OnObservedClipboardItem(other);
            Assert.False(item.IsDirty);
            Assert.True(host.ActivateHistoryItem(item));
            Assert.Single(editor.TestTextAnnotations);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            Assert.Empty(editor.TestTextAnnotations);
        });
    }

    private static ClipboardEditorHostForm CreateHost(ImageEditor editor, Bitmap source)
    {
        editor.AttachExternalSurface();
        var host = new ClipboardEditorHostForm(true, editor) { SuppressActivation = true };
        host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());
        Assert.True(host.ActivateHistoryItem(host.HistoryStore.AddObservedImage(source)));
        editor.ResizeSurface(new Size(500, 400));
        EditorFixture.PinModifiers(editor);
        return host;
    }

    private static void AddRectangle(ImageEditor editor)
    {
        editor.TestToggleRectTool();
        editor.TestFireMouseDownAtImagePixel(new Point(10, 10), MouseButtons.Left);
        editor.TestFireMouseMoveAtImagePixel(new Point(80, 45), MouseButtons.Left);
        editor.TestFireMouseUpAtImagePixel(new Point(80, 45), MouseButtons.Left);
        editor.TestDeactivateDrawingTool();
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
