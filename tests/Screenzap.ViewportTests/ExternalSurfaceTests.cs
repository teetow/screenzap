using System.Drawing;
using System.Windows.Forms;
using screenzap;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests;

public class ExternalSurfaceTests
{
    [Fact]
    public void NativeSurfaceKeepsExactCanvasSizeAcrossToolChanges()
    {
        StaTest.Run(() =>
        {
            using var editor = EditorFixture.WithCanvas(400, 300);
            editor.AttachExternalSurface();
            editor.ResizeSurface(new Size(640, 480));
            ((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.TextTool);
            Assert.Equal(new Size(640, 480), editor.TestViewportMetrics.ClientSize);
            editor.SurfacePointer(0, new Point(140, 130), MouseButtons.Left);
            editor.SurfacePointer(2, new Point(140, 130), MouseButtons.Left);
            editor.SurfaceCharacter('H'); editor.SurfaceCharacter('i');
            Assert.Equal(1, editor.TestTextAnnotationCount);
            Assert.Contains("Hi", editor.TestDescribeTextAnnotations());
            Assert.Equal(new Size(640, 480), editor.TestViewportMetrics.ClientSize);
        });
    }

    [Fact]
    public void NativeSurfaceDrawsAndUndoesThroughProductionPointerAndKeyInput()
    {
        StaTest.Run(() =>
        {
            using var editor = EditorFixture.WithCanvas(400, 300);
            editor.AttachExternalSurface(); editor.ResizeSurface(new Size(640, 480));
            ((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.RectangleTool);
            editor.SurfacePointer(0, new Point(150, 120), MouseButtons.Left);
            editor.SurfacePointer(1, new Point(220, 180), MouseButtons.Left);
            editor.SurfacePointer(2, new Point(220, 180), MouseButtons.Left);
            Assert.Equal(1, editor.TestAnnotationShapeCount);
            editor.SurfaceKey(Keys.Control | Keys.Z);
            Assert.Equal(0, editor.TestAnnotationShapeCount);
        });
    }

    [Fact]
    public void NativeSurfaceCommitKeepsViewAndUndoAndUsesExternalActivation()
    {
        StaTest.Run(() =>
        {
            using var editor = new ImageEditor(); editor.AttachExternalSurface();
            using var host = new ClipboardEditorHostForm(true, editor);
            int activations = 0; host.ExternalActivateRequested = () => activations++;
            using var image = new Bitmap(800, 600);
            host.ActivateHistoryItem(host.HistoryStore.AddObservedImage(image));
            editor.ResizeSurface(new Size(640, 480)); editor.SurfaceSetZoom(2);
            editor.TestPanViewportBy(new Size(-30, -40));
            ((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.ArrowTool);
            editor.SurfacePointer(0, new Point(140, 120), MouseButtons.Left);
            editor.SurfacePointer(1, new Point(250, 160), MouseButtons.Left);
            editor.SurfacePointer(2, new Point(250, 160), MouseButtons.Left);
            var before = editor.TestViewportMetrics;
            Assert.True(host.CanExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.True(host.ExecuteHostCommand(EditorCommandId.CommitEdits));
            Assert.Equal(before.ZoomLevel, editor.TestViewportMetrics.ZoomLevel);
            Assert.Equal(before.PanOffset, editor.TestViewportMetrics.PanOffset);
            Assert.Equal(before.ClientSize, editor.TestViewportMetrics.ClientSize);
            Assert.False(host.HistoryStore.ActiveItem!.IsDirty);
            Assert.True(host.CanExecuteHostCommand(EditorCommandId.Undo));
            host.ShowAndActivate(); Assert.Equal(1, activations); Assert.False(host.Visible);
        });
    }

    [Fact]
    public void NativeSurfaceRendersAnnotationsWithoutVisibleCompatibilityControls()
    {
        StaTest.Run(() =>
        {
            using var editor = EditorFixture.WithCanvas(100, 100);
            editor.AttachExternalSurface(); editor.ResizeSurface(new Size(200, 200));
            ((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.RectangleTool);
            editor.SurfacePointer(0, new Point(60, 60), MouseButtons.Left);
            editor.SurfacePointer(1, new Point(130, 130), MouseButtons.Left);
            editor.SurfacePointer(2, new Point(130, 130), MouseButtons.Left);
            using var frame = new Bitmap(200, 200); using var graphics = Graphics.FromImage(frame);
            editor.RenderSurface(graphics);
            Assert.False(editor.Visible);
            Assert.True(frame.GetPixel(60, 80).R > frame.GetPixel(60, 80).B);
        });
    }
}

public class ExternalSurfaceTextTests
{
    [Fact]
    public void NativeTypingEnablesCommitBeforeLeavingTheTextTool()
    {
        StaTest.Run(() =>
        {
            using var editor = new ImageEditor(); editor.AttachExternalSurface();
            using var host = new ClipboardEditorHostForm(true, editor);
            host.ExternalActivateRequested = () => { };
            using var image = new Bitmap(400, 300);
            host.ActivateHistoryItem(host.HistoryStore.AddObservedImage(image));
            editor.ResizeSurface(new Size(640, 480));
            host.ExecuteHostCommand(EditorCommandId.TextTool);
            editor.SurfacePointer(0, new Point(150, 140), MouseButtons.Left);
            editor.SurfacePointer(2, new Point(150, 140), MouseButtons.Left);
            editor.SurfaceCharacter('A');
            Assert.True(host.CanExecuteHostCommand(EditorCommandId.CommitEdits));
            editor.SurfaceFinalizeText();
            host.ExecuteHostCommand(EditorCommandId.CommitEdits);
            Assert.True(host.CanExecuteHostCommand(EditorCommandId.Undo));
            host.ExecuteHostCommand(EditorCommandId.Undo);
            Assert.Contains("A", editor.TestDescribeTextAnnotations()); // Undo the flattening boundary first.
            host.ExecuteHostCommand(EditorCommandId.Undo);
            Assert.Equal(0, editor.TestTextAnnotationCount);
        });
    }

    [Fact]
    public void InspectorFocusDoesNotModifyTextOrAddUndoWhenStyleIsUnchanged()
    {
        StaTest.Run(() =>
        {
            using var editor = EditorFixture.WithCanvas(200, 150);
            editor.AttachExternalSurface(); editor.ResizeSurface(new Size(400, 300));
            ((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.TextTool);
            editor.SurfacePointer(0, new Point(120, 110), MouseButtons.Left);
            editor.SurfacePointer(2, new Point(120, 110), MouseButtons.Left);
            editor.SurfaceCharacter('A'); editor.SurfaceSuspendText();
            string font = editor.SurfaceFont;
            editor.SurfaceStyle("Font", font);
            ((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.Undo);
            Assert.Equal(0, editor.TestTextAnnotationCount);
        });
    }

    [Fact]
    public void ModalToolOwnsInspectorWhenAnImageLayerRemainsSelected()
    {
        StaTest.Run(() =>
        {
            using var editor = EditorFixture.WithCanvas(400, 300);
            editor.AttachExternalSurface(); editor.ResizeSurface(new Size(640, 480));
            using var paste = new Bitmap(40, 30);
            Assert.True(editor.SurfaceDropImage(paste, new Point(200, 180)));
            Assert.Equal("Layer", editor.SurfaceInspectorKind);
            ((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.StraightenTool);
            Assert.Equal("Straighten", editor.SurfaceInspectorKind);
        });
    }
}

public class ExternalSurfaceColorTests
{
    [Fact]
    public void ColorCorrectionChangesOnlySelectedPixelsAndPreservesAlpha()
    {
        using var source = new Bitmap(2, 1);
        var original = Color.FromArgb(128, 40, 60, 80);
        source.SetPixel(0, 0, original);
        source.SetPixel(1, 0, original);
        using var result = screenzap.lib.ColorCorrectionProcessor.Apply(source, new Rectangle(0, 0, 1, 1), 1, 0, 100, 1);
        var changed = result.GetPixel(0, 0);
        Assert.Equal(128, changed.A);
        Assert.InRange(changed.R, 78, 82);
        Assert.InRange(changed.G, 118, 122);
        Assert.InRange(changed.B, 158, 162);
        Assert.Equal(original.ToArgb(), result.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void ColorCorrectionCanUndoAndKeepsTheNativeView()
    {
        StaTest.Run(() =>
        {
            using var editor = new ImageEditor();
            editor.AttachExternalSurface();
            using var host = new ClipboardEditorHostForm(true, editor);
            host.ExternalActivateRequested = () => { };
            using var source = new Bitmap(200, 150);
            using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(40, 60, 80));
            host.ActivateHistoryItem(host.HistoryStore.AddObservedImage(source));
            editor.ResizeSurface(new Size(400, 300));
            editor.SurfaceSetZoom(2);
            editor.TestPanViewportBy(new Size(-30, -40));
            var before = editor.TestViewportMetrics;
            editor.SurfaceColorCorrection(1, 0, 100, 1);
            Assert.Equal(before.ZoomLevel, editor.TestViewportMetrics.ZoomLevel);
            Assert.Equal(before.PanOffset, editor.TestViewportMetrics.PanOffset);
            using (var changed = editor.SurfaceCopyBase()) Assert.InRange(changed!.GetPixel(0, 0).R, 78, 82);
            Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
            using var undone = editor.SurfaceCopyBase();
            Assert.Equal(source.GetPixel(0, 0).ToArgb(), undone!.GetPixel(0, 0).ToArgb());
        });
    }
}

public class ExternalSurfaceDeleteTests
{
    [Fact]
    public void DeleteClearsSelectedPixelsToAlphaAndUndoRedoPreserveTheView()
    {
        StaTest.Run(() =>
        {
            using var editor = new ImageEditor();
            editor.AttachExternalSurface();
            using var image = new Bitmap(100, 80, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(image)) g.Clear(Color.Red);
            editor.LoadImage(image);
            editor.ResizeSurface(new Size(400, 300));
            editor.SurfaceSetZoom(2);
            editor.TestPanViewportBy(new Size(-20, -10));
            var selection = new Rectangle(10, 20, 30, 25);
            editor.SetSelectionForDiagnostics(selection);
            var view = editor.TestViewportMetrics;
            Assert.True(editor.SurfaceKey(Keys.Delete));
            using (var cleared = editor.CloneBaseBitmapForTests())
            {
                Assert.Equal(0, cleared!.GetPixel(15, 25).A);
                Assert.Equal(Color.Red.ToArgb(), cleared.GetPixel(1, 1).ToArgb());
            }
            Assert.Equal(view.ZoomLevel, editor.TestViewportMetrics.ZoomLevel);
            Assert.Equal(view.PanOffset, editor.TestViewportMetrics.PanOffset);
            Assert.Equal(selection, editor.SelectionDiagnostics.Selection);
            Assert.True(editor.SurfaceKey(Keys.Control | Keys.Z));
            using (var undone = editor.CloneBaseBitmapForTests()) Assert.Equal(Color.Red.ToArgb(), undone!.GetPixel(15, 25).ToArgb());
            Assert.True(((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.Redo));
            using var redone = editor.CloneBaseBitmapForTests();
            Assert.Equal(0, redone!.GetPixel(15, 25).A);
        });
    }

    [Fact]
    public void DeleteWithoutASelectionClearsTheCanvas()
    {
        StaTest.Run(() =>
        {
            using var editor = EditorFixture.WithCanvas(40, 30);
            editor.AttachExternalSurface();
            Assert.True(editor.SurfaceKey(Keys.Delete));
            using var cleared = editor.CloneBaseBitmapForTests();
            Assert.Equal(0, cleared!.GetPixel(20, 15).A);
        });
    }
}
