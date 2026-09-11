using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Windows.AI.MachineLearning;

namespace screenzap.lib;

internal interface IDeJpegFilter
{
    Task<byte[]> CleanAsync(byte[] png, IProgress<string>? progress, CancellationToken cancellation);
}

internal sealed class OnnxDeJpegFilter : IDeJpegFilter
{
    // Only one GPU job at a time, including jobs from other editor windows.
    private static readonly SemaphoreSlim gpu = new(1, 1);
    private static bool providersReady;
    internal static string ModelPath => Path.Combine(AppContext.BaseDirectory, "Models", "swinir-jpeg10.onnx");

    public async Task<byte[]> CleanAsync(byte[] png, IProgress<string>? progress, CancellationToken cancellation)
    {
        progress?.Report("Preparing JPEG cleanup…");
        await gpu.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await PrepareProvidersAsync(progress, cancellation).ConfigureAwait(false);
            return await Task.Run(() => Clean(png, progress, cancellation), cancellation).ConfigureAwait(false);
        }
        finally { gpu.Release(); }
    }

    private static async Task PrepareProvidersAsync(IProgress<string>? progress, CancellationToken cancellation)
    {
        if (providersReady) return;
        _ = OrtEnv.Instance();
        var catalog = ExecutionProviderCatalog.GetDefault();
        if (catalog != null)
        {
            progress?.Report("Preparing acceleration… First use may download Windows ML components.");
            try
            {
                await catalog.EnsureAndRegisterCertifiedAsync().AsTask(cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // An offline Store must not prevent use of bundled GPU/CPU providers.
                Logger.Log($"Windows ML provider download unavailable: {ex.Message}");
                await catalog.RegisterCertifiedAsync().AsTask(cancellation).ConfigureAwait(false);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        providersReady = true;
        Logger.Log("De-JPEG available providers: " + string.Join(", ", OrtEnv.Instance().GetEpDevices()
            .Select(device => $"{device.EpName} ({device.HardwareDevice.Type})")));
    }

    private static byte[] Clean(byte[] png, IProgress<string>? progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!File.Exists(ModelPath))
            throw new FileNotFoundException("The De-JPEG model is missing. Reinstall Screenzap with its Models folder.", ModelPath);
        using var options = new SessionOptions
        {
            EnableMemoryPattern = false,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 2)),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        options.SetEpSelectionPolicy(ExecutionProviderDevicePolicy.MAX_PERFORMANCE);
        var profileDirectory = Environment.GetEnvironmentVariable("SCREENZAP_DEJPEG_PROFILE_DIR");
        if (!string.IsNullOrEmpty(profileDirectory))
        {
            Directory.CreateDirectory(profileDirectory);
            options.ProfileOutputPathPrefix = Path.Combine(profileDirectory, "dejpeg-" + Guid.NewGuid().ToString("N"));
            options.EnableProfiling = true;
        }
        // Dispose after each operation so the app does not hold GPU memory while idle.
        using var session = new InferenceSession(ModelPath, options);
        using var run = new RunOptions();
        using var cancelRun = cancellation.Register(() => run.Terminate = true);
        try
        {
            using var stream = new MemoryStream(png);
            using var source = new Bitmap(stream);
            using var result = DeJpegTiles.Clean(source, tile =>
            {
                using var input = OrtValue.CreateTensorValueFromMemory(tile, new long[] { 1, 3, DeJpegTiles.TileSize, DeJpegTiles.TileSize });
                using var output = session.Run(run, new[] { "image" }, new[] { input }, new[] { "clean" });
                return output[0].GetTensorDataAsSpan<float>().ToArray();
            }, progress, cancellation);
            using var encoded = new MemoryStream();
            result.Save(encoded, ImageFormat.Png);
            return encoded.ToArray();
        }
        catch (OnnxRuntimeException) when (cancellation.IsCancellationRequested)
        { throw new OperationCanceledException(cancellation); }
        finally
        {
            if (options.EnableProfiling) Logger.Log("De-JPEG profile: " + session.EndProfiling());
        }
    }
}

internal static class DeJpegTiles
{
    internal const int TileSize = 252;
    private const int Border = 28;
    private const int Step = TileSize - Border * 2;

    // Feed each tile extra context, then keep its centre. Mirror at image edges,
    // including tiny images; never resize. Tile origins stay on the seven-pixel grid.
    internal static Bitmap Clean(Bitmap source, Func<float[], float[]> infer, IProgress<string>? progress, CancellationToken cancellation)
    {
        int width = source.Width, height = source.Height;
        var bounds = new Rectangle(0, 0, width, height);
        using var rgb = source.Clone(bounds, PixelFormat.Format32bppArgb);
        var pixels = new byte[checked(width * height * 4)];
        var locked = rgb.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(locked.Scan0 + y * locked.Stride, pixels, y * width * 4, width * 4);
        }
        finally { rgb.UnlockBits(locked); }
        var output = new byte[pixels.Length];
        int plane = TileSize * TileSize;
        var tile = new float[3 * plane];
        int total = ((width + Step - 1) / Step) * ((height + Step - 1) / Step), done = 0;
        for (int top = 0; top < height; top += Step)
        for (int left = 0; left < width; left += Step)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"Removing JPEG artifacts… {++done}/{total}");
            for (int y = 0; y < TileSize; y++)
            for (int x = 0; x < TileSize; x++)
            {
                int pixel = (Reflect(top + y - Border, height) * width + Reflect(left + x - Border, width)) * 4;
                int index = y * TileSize + x;
                tile[index] = pixels[pixel + 2] / 255f;
                tile[plane + index] = pixels[pixel + 1] / 255f;
                tile[plane * 2 + index] = pixels[pixel] / 255f;
            }
            var clean = infer(tile);
            cancellation.ThrowIfCancellationRequested();
            if (clean.Length != tile.Length) throw new InvalidOperationException("The De-JPEG model returned an unexpected image size.");
            for (int y = 0; y < Math.Min(Step, height - top); y++)
            for (int x = 0; x < Math.Min(Step, width - left); x++)
            {
                int pixel = ((top + y) * width + left + x) * 4;
                int index = (y + Border) * TileSize + x + Border;
                output[pixel] = ToByte(clean[plane * 2 + index]);
                output[pixel + 1] = ToByte(clean[plane + index]);
                output[pixel + 2] = ToByte(clean[index]);
                output[pixel + 3] = pixels[pixel + 3];
            }
        }
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            var target = result.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(output, y * width * 4, target.Scan0 + y * target.Stride, width * 4);
            }
            finally { result.UnlockBits(target); }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static byte ToByte(float value)
    {
        if (!float.IsFinite(value)) throw new InvalidOperationException("The De-JPEG model returned invalid pixels.");
        return (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    }

    private static int Reflect(int position, int length)
    {
        if (length == 1) return 0;
        int period = 2 * (length - 1);
        position = (position % period + period) % period;
        return position < length ? position : period - position;
    }
}
