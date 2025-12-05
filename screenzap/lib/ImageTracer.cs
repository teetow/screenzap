using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace screenzap.lib
{
    /// <summary>
    /// Traces bitmap images to SVG using VTracer.
    /// VTracer is a state-of-the-art image tracing tool that converts raster images to vector graphics.
    /// Download VTracer from: https://github.com/visioncortex/vtracer/releases
    /// </summary>
    public static class ImageTracer
    {
        public enum TracingPreset
        {
            /// <summary>Black and white line art, sketches</summary>
            BlackAndWhite,
            /// <summary>Logos, icons, flat graphics with solid colors</summary>
            Poster,
            /// <summary>Photographs and complex images</summary>
            Photo
        }

        private static string GetPresetArg(TracingPreset preset) => preset switch
        {
            TracingPreset.BlackAndWhite => "bw",
            TracingPreset.Poster => "poster",
            TracingPreset.Photo => "photo",
            _ => "poster"
        };

        /// <summary>
        /// Gets the path to the VTracer executable.
        /// </summary>
        private static string GetVTracerPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vtracer.exe");
        }

        /// <summary>
        /// Checks if VTracer is available.
        /// </summary>
        public static bool IsAvailable()
        {
            return File.Exists(GetVTracerPath());
        }

        /// <summary>
        /// Traces a bitmap image to SVG format.
        /// </summary>
        /// <param name="bitmap">The source bitmap to trace.</param>
        /// <param name="preset">The tracing preset to use. Defaults to Poster for general graphics.</param>
        /// <returns>SVG content as a string, or null if tracing fails.</returns>
        public static async Task<string?> TraceToSvgAsync(Bitmap bitmap, TracingPreset preset = TracingPreset.Poster)
        {
            if (bitmap == null)
            {
                throw new ArgumentNullException(nameof(bitmap));
            }

            var vtracerPath = GetVTracerPath();
            if (!File.Exists(vtracerPath))
            {
                throw new FileNotFoundException(
                    "VTracer executable not found. Download from https://github.com/visioncortex/vtracer/releases",
                    vtracerPath);
            }

            var tempInput = Path.Combine(Path.GetTempPath(), $"screenzap_trace_{Guid.NewGuid()}.png");
            var tempOutput = Path.Combine(Path.GetTempPath(), $"screenzap_trace_{Guid.NewGuid()}.svg");

            try
            {
                // Save bitmap to temp file
                bitmap.Save(tempInput, ImageFormat.Png);

                var presetArg = GetPresetArg(preset);

                var psi = new ProcessStartInfo
                {
                    FileName = vtracerPath,
                    Arguments = $"--input \"{tempInput}\" --output \"{tempOutput}\" --preset {presetArg}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                // Wait for process with timeout
                var completed = await Task.Run(() => process.WaitForExit(30000));
                if (!completed)
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("VTracer process timed out after 30 seconds.");
                }

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"VTracer failed with exit code {process.ExitCode}: {error}");
                }

                if (!File.Exists(tempOutput))
                {
                    throw new InvalidOperationException("VTracer did not produce output file.");
                }

                return await File.ReadAllTextAsync(tempOutput);
            }
            finally
            {
                // Clean up temp files
                try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }
                try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }
            }
        }

        /// <summary>
        /// Traces a bitmap image to SVG format synchronously.
        /// </summary>
        public static string? TraceToSvg(Bitmap bitmap, TracingPreset preset = TracingPreset.Poster)
        {
            return TraceToSvgAsync(bitmap, preset).GetAwaiter().GetResult();
        }
    }
}
