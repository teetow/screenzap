using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
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
    private static string Workflow => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Workflows", "dejpeg-api.json"));

    [Fact]
    public async Task ClientUploadsSubstitutesPollsAndRetrievesOnlyItsOutput()
    {
        var paths = new List<string>();
        int polls = 0;
        using var handler = new Handler(async request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            if (path == "/upload/image")
            {
                Assert.IsType<MultipartFormDataContent>(request.Content);
                return Json("{\"name\":\"input.png\",\"subfolder\":\"test\"}");
            }
            if (path == "/prompt")
            {
                var graph = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!["prompt"]!;
                Assert.Equal("test/input.png", graph["1"]!["inputs"]!["image"]!.GetValue<string>());
                Assert.Equal("006_colorCAR_DFWB_s126w7_SwinIR-M_jpeg10.pth", graph["2"]!["inputs"]!["model_name"]!.GetValue<string>());
                Assert.Equal("ImageUpscaleWithModel", graph["3"]!["class_type"]!.GetValue<string>());
                Assert.Equal(4, graph.AsObject().Count);
                return Json("{\"prompt_id\":\"our-job\"}");
            }
            if (path == "/history/our-job")
            {
                if (polls++ == 0) return Json("{}");
                return Json("""{"our-job":{"status":{"completed":true,"status_str":"success"},"outputs":{"9":{"images":[{"filename":"a b.png","subfolder":"Screenzap","type":"output"}]}}}}""");
            }
            Assert.Equal("/view", path);
            Assert.Contains("filename=a%20b.png", request.RequestUri.Query);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8188/") };
        var result = await new ComfyDeJpegFilter(client, Workflow, TimeSpan.Zero).CleanAsync(
            new byte[] { 42 }, null, CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, result);
        Assert.Equal(new[] { "/upload/image", "/prompt", "/history/our-job", "/history/our-job", "/view" }, paths);
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("execution")]
    [InlineData("missing_output")]
    [InlineData("unavailable")]
    public async Task ClientSurfacesFailures(string failure)
    {
        using var handler = new Handler(request =>
        {
            if (failure == "unavailable") throw new HttpRequestException("Connection refused");
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/upload/image") return Task.FromResult(Json("{\"name\":\"input.png\"}"));
            if (path == "/prompt") return Task.FromResult(failure == "validation"
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Missing model") }
                : Json("{\"prompt_id\":\"job\"}"));
            return Task.FromResult(Json(failure == "execution"
                ? """{"job":{"status":{"completed":false,"status_str":"error","messages":[["execution_error",{"exception_message":"out of memory"}]]}}}"""
                : """{"job":{"status":{"completed":true,"status_str":"success"},"outputs":{}}}"""));
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8188/") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ComfyDeJpegFilter(client, Workflow, TimeSpan.Zero).CleanAsync(new byte[] { 1 }, null, CancellationToken.None));
        Assert.Contains(failure switch { "validation" => "Missing model", "execution" => "out of memory", "unavailable" => "Start it", _ => "without an output" }, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientCancellationAndTimeoutNeverInterruptOtherJobs(bool timeout)
    {
        using var cts = new CancellationTokenSource();
        var paths = new List<string>();
        using var handler = new Handler(request =>
        {
            string path = request.RequestUri!.AbsolutePath; paths.Add(path);
            if (path == "/upload/image") return Task.FromResult(Json("{\"name\":\"input.png\"}"));
            if (path == "/prompt") return Task.FromResult(Json("{\"prompt_id\":\"job\"}"));
            if (!timeout) cts.Cancel();
            return Task.FromResult(Json("{}"));
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8188/") };
        var backend = new ComfyDeJpegFilter(client, Workflow, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(100));
        if (timeout) await Assert.ThrowsAsync<TimeoutException>(() => backend.CleanAsync(new byte[] { 1 }, null, cts.Token));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.CleanAsync(new byte[] { 1 }, null, cts.Token));
        Assert.DoesNotContain("/interrupt", paths);
        Assert.DoesNotContain("/queue", paths);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://192.168.1.10:8188")]
    [InlineData("http://localhost:8188/other")]
    public void EndpointIsStrictlyLocal(string address) => Assert.Throws<ArgumentException>(() => ComfyDeJpegFilter.ParseEndpoint(address));

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
            using var dialog = new DeJpegDialog(source, () => current, _ => backend);
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
            using var dialog = new DeJpegDialog(source, () => true, _ => backend);
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
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> send;
        internal Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => this.send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
