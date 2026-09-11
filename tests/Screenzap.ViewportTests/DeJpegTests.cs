using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using screenzap;
using screenzap.lib;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests;

public class DeJpegTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 13)]
    [InlineData(391, 253)]
    [InlineData(1201, 35)]
    public void TilingPreservesEveryPixelAcrossSeamsAndSmallOrOddEdges(int width, int height)
    {
        using var source = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            source.SetPixel(x, y, Color.FromArgb((x * 17 + y) % 256, (y * 11 + x) % 256, (x + y * 3) % 256));
        using var result = DeJpegTiles.Clean(source, tile => tile, null, CancellationToken.None);
        Assert.Equal(source.Size, result.Size);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            Assert.Equal(source.GetPixel(x, y), result.GetPixel(x, y));
    }

    [Fact]
    public void CancellationStopsBeforeTheNextTile()
    {
        using var source = new Bitmap(600, 300);
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        Assert.Throws<OperationCanceledException>(() => DeJpegTiles.Clean(source, tile =>
        {
            calls++;
            cancellation.Cancel();
            return tile;
        }, null, cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidModelOutputIsRejected(bool wrongSize)
    {
        using var source = new Bitmap(1, 1);
        Assert.Throws<InvalidOperationException>(() => DeJpegTiles.Clean(source, tile =>
        {
            if (wrongSize) return Array.Empty<float>();
            Array.Fill(tile, float.NaN);
            return tile;
        }, null, CancellationToken.None));
    }

    [Fact]
    public void BitmapRestoresSizeAndExactAlphaAndRejectsWrongDimensions()
    {
        using var source = new Bitmap(1201, 35, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(source)) g.Clear(Color.FromArgb(123, 40, 60, 80));
        source.SetPixel(0, 0, Color.FromArgb(0, 10, 20, 30));
        using var input = new DeJpegBitmap(source);
        Assert.Equal(source.Size, input.InputSize);
        using var encoded = new Bitmap(new MemoryStream(input.Png));
        Assert.Equal(source.Size, encoded.Size);
        using var generated = new Bitmap(input.InputSize.Width, input.InputSize.Height);
        using (var g = Graphics.FromImage(generated)) g.Clear(Color.Blue);
        using var restored = input.Restore(Png(generated));
        Assert.Equal(source.Size, restored.Size);
        Assert.Equal(0, restored.GetPixel(0, 0).A);
        Assert.Equal(123, restored.GetPixel(600, 20).A);
        using var wrong = new Bitmap(2, 2);
        Assert.Throws<InvalidOperationException>(() => input.Restore(Png(wrong)));
    }

    [Fact]
    public void ApplyPreservesLayersSelectionAndUndoRedoAndRejectsStaleResults()
    {
        StaTest.Run(() =>
        {
            using var editor = new ImageEditor();
            using var source = new Bitmap(48, 32);
            using (var g = Graphics.FromImage(source)) g.Clear(Color.Red);
            editor.LoadImage(source);
            using var pasted = new Bitmap(10, 10);
            editor.SetInternalClipboardImageForDiagnostics(pasted);
            Assert.True(editor.PasteFromClipboardForDiagnostics());
            var layerFrame = editor.GetImageLayerFrameForTests(0);
            editor.SetSelectionForDiagnostics(new Rectangle(1, 2, 3, 4));
            var selection = editor.SelectionDiagnostics.Selection;
            long revision = editor.DeJpegRevisionForTests;
            using var result = new Bitmap(48, 32);
            using (var g = Graphics.FromImage(result)) g.Clear(Color.Blue);
            Assert.True(editor.ApplyDeJpeg(result, revision));
            Assert.Equal(layerFrame, editor.GetImageLayerFrameForTests(0));
            Assert.Equal(selection, editor.SelectionDiagnostics.Selection);
            AssertPixel(editor, Color.Blue);
            Assert.False(editor.ApplyDeJpeg(result, revision));
            var presenter = (IClipboardDocumentPresenter)editor;
            Assert.True(presenter.TryExecute(EditorCommandId.Undo));
            AssertPixel(editor, Color.Red);
            Assert.Equal(layerFrame, editor.GetImageLayerFrameForTests(0));
            Assert.True(presenter.TryExecute(EditorCommandId.Redo));
            AssertPixel(editor, Color.Blue);
            revision = editor.DeJpegRevisionForTests;
            editor.LoadImage(source);
            Assert.False(editor.ApplyDeJpeg(result, revision));
            AssertPixel(editor, Color.Red);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogDiscardsLateResultAfterDocumentChangeOrDisposal(bool dispose)
    {
        StaTest.Run(() =>
        {
            using var source = new Bitmap(32, 32);
            var backend = new DelayedBackend();
            bool current = true;
            using var dialog = new DeJpegDialog(source, () => current, () => backend);
            dialog.CreateControl();
            var task = dialog.CleanAsync();
            PumpUntil(() => backend.Started.Task.IsCompleted);
            current = false;
            if (dispose) dialog.Dispose();
            backend.Complete.TrySetResult(Png(source));
            PumpUntil(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
            Assert.Null(dialog.Result);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogStartsAutomaticallyAndCompletesOrCancels(bool cancel)
    {
        StaTest.Run(() =>
        {
            using var source = new Bitmap(47, 31);
            var backend = new DelayedBackend();
            using var dialog = new DeJpegDialog(source, () => true, () => backend);
            using var timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += (_, _) =>
            {
                if (!backend.Started.Task.IsCompleted) return;
                timer.Stop();
                if (cancel) dialog.Close();
                backend.Complete.SetResult(Png(source));
            };
            timer.Start();
            var outcome = dialog.ShowDialog();
            PumpUntil(() => dialog.FinishedForDiagnostics);
            Assert.Equal(cancel ? DialogResult.Cancel : DialogResult.OK, outcome);
            if (cancel) Assert.Null(dialog.Result);
            else Assert.NotNull(dialog.Result);
        });
    }

    private static void PumpUntil(Func<bool> ready)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!ready() && DateTime.UtcNow < limit) { Application.DoEvents(); Thread.Sleep(5); }
        Assert.True(ready(), "UI operation timed out");
    }
    private sealed class DelayedBackend : IDeJpegFilter
    {
        internal TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<byte[]> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<byte[]> CleanAsync(byte[] png, IProgress<string>? progress, CancellationToken cancellation)
        { Started.TrySetResult(true); return Complete.Task; }
    }
    private static void AssertPixel(ImageEditor editor, Color color)
    { using var image = editor.CloneBaseBitmapForTests(); Assert.Equal(color.ToArgb(), image!.GetPixel(10, 10).ToArgb()); }
    private static byte[] Png(Image image)
    { using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png); return stream.ToArray(); }
}
