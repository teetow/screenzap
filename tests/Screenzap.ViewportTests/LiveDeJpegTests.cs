using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using screenzap;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests;

// Explicit opt-in: these tests use the actual GPU/backend, no injected responses.
public sealed class LiveComfyFactAttribute : FactAttribute
{
    public LiveComfyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SCREENZAP_LIVE_COMFYUI") != "1")
            Skip = "Set SCREENZAP_LIVE_COMFYUI=1 and SCREENZAP_LIVE_INPUT_DIR to run actual GPU/UI validation.";
    }
}

public class LiveDeJpegTests
{
    [LiveComfyFact]
    public void RealOneClickCleanupUndoRedo()
    {
        string directory = Environment.GetEnvironmentVariable("SCREENZAP_LIVE_INPUT_DIR")
            ?? throw new InvalidOperationException("SCREENZAP_LIVE_INPUT_DIR is required.");
        string artifacts = Path.Combine(directory, "results");
        Directory.CreateDirectory(artifacts);
        var files = Directory.GetFiles(directory, "*.png");
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            StaTest.Run(() => Exercise(file, artifacts));
        }
    }

    private static void Exercise(string file, string artifacts)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        string prefix = Path.Combine(artifacts, name);
        using var source = new Bitmap(file);
        using var editor = new ImageEditor();
        editor.LoadImage(source);
        editor.Show();
        using var before = editor.CloneBaseBitmapForTests()!;
        before.Save(prefix + "-input.png", ImageFormat.Png);
        var presenter = (IClipboardDocumentPresenter)editor;
        bool observed = false;
        Exception? failure = null;
        var elapsed = Stopwatch.StartNew();
        using var timer = new System.Windows.Forms.Timer { Interval = 200 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<DeJpegDialog>().FirstOrDefault();
            if (dialog == null) return;
            try
            {
                observed = true;
                if (elapsed.Elapsed > TimeSpan.FromMinutes(12)) throw new TimeoutException(dialog.StatusForDiagnostics);
                if (dialog.FinishedForDiagnostics && dialog.Result == null)
                    throw new InvalidOperationException(dialog.StatusForDiagnostics);

            }
            catch (Exception ex)
            {
                failure = ex;
                timer.Stop();
                File.WriteAllText(prefix + "-status.txt", ex.ToString());
                dialog.Close();
            }
        };
        timer.Start();
        Assert.True(presenter.TryExecute(EditorCommandId.DeJpeg));
        timer.Stop();
        if (failure != null) throw failure;
        Assert.True(observed);
        using var after = editor.CloneBaseBitmapForTests()!;
        Assert.Equal(before.Size, after.Size);
        Assert.False(PixelsEqual(before, after), "Backend returned unchanged pixels");
        after.Save(prefix + "-applied.png", ImageFormat.Png);
        Assert.True(presenter.TryExecute(EditorCommandId.Undo));
        using var undone = editor.CloneBaseBitmapForTests()!;
        Assert.True(PixelsEqual(before, undone), "Undo did not restore original pixels");
        Assert.True(presenter.TryExecute(EditorCommandId.Redo));
        using var redone = editor.CloneBaseBitmapForTests()!;
        Assert.True(PixelsEqual(after, redone), "Redo did not restore edited pixels");
        File.WriteAllText(prefix + "-result.json", JsonSerializer.Serialize(new
        {
            input = file, seconds = elapsed.Elapsed.TotalSeconds,
            width = after.Width, height = after.Height, applied = true, undoExact = true, redoExact = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(prefix + "-status.txt", "Passed: one-click real GPU De-JPEG, automatic apply, exact Undo/Redo.\n");
        editor.Close();
    }

    private static bool PixelsEqual(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) return false;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) return false;
        return true;
    }

}
