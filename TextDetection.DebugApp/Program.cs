using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using TextDetection;

namespace TextDetection.DebugApp
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var (bitmap, originPath) = LoadBitmap(args);
                using (bitmap)
                {
                    Console.WriteLine($"Loaded image {originPath} ({bitmap.Width}x{bitmap.Height}).");
                    var regions = TextRegionDetector.FindTextRegionsDetailed(bitmap);
                    Console.WriteLine($"Detected {regions.Count} candidate text regions.");

                    var outputPath = WriteAnnotatedPreview(bitmap, regions, originPath);
                    DumpRegions(bitmap, regions);
                    Console.WriteLine($"Annotated preview saved to {outputPath}.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static (Bitmap bitmap, string originPath) LoadBitmap(string[] args)
        {
            if (args.Length > 0 && File.Exists(args[0]))
            {
                var filePath = Path.GetFullPath(args[0]);
                return (new Bitmap(filePath), filePath);
            }

            var samplePath = GetDefaultSamplePath();
            if (File.Exists(samplePath))
            {
                Console.WriteLine($"Reusing cached screenshot {samplePath}.");
                return (new Bitmap(samplePath), samplePath);
            }

            var screenshotPath = CaptureScreenshot(samplePath);
            return (new Bitmap(screenshotPath), screenshotPath);
        }

        private static string GetDefaultSamplePath()
        {
            var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Screenzap", "TextDetection");
            Directory.CreateDirectory(cacheRoot);
            return Path.Combine(cacheRoot, "debug-sample.png");
        }

        private static string CaptureScreenshot(string targetPath)
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary == null)
            {
                throw new InvalidOperationException("No primary screen found.");
            }

            var bounds = primary.Bounds;

            using (var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                bitmap.Save(targetPath, ImageFormat.Png);
            }

            Console.WriteLine($"Captured screenshot to {targetPath}.");
            return targetPath;
        }

        private static string WriteAnnotatedPreview(Bitmap source, IReadOnlyList<DetectedTextRegion> regions, string originPath)
        {
            var directory = Path.GetDirectoryName(originPath) ?? Environment.CurrentDirectory;
            var baseName = Path.GetFileNameWithoutExtension(originPath);
            var previewPath = Path.Combine(directory, baseName + "-annotated.png");

            using (var annotated = new Bitmap(source))
            using (var g = Graphics.FromImage(annotated))
            using (var pen = new Pen(Color.LimeGreen, 2))
            using (var brush = new SolidBrush(Color.FromArgb(60, Color.LimeGreen)))
            {
                foreach (var region in regions)
                {
                    var rect = region.Bounds;
                    g.FillRectangle(brush, rect);
                    g.DrawRectangle(pen, rect);
                }

                annotated.Save(previewPath, ImageFormat.Png);
            }

            return previewPath;
        }

        private static void DumpRegions(Bitmap bitmap, IReadOnlyList<DetectedTextRegion> regions)
        {
            bool showAscii = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEXTDETECTION_SHOW_ASCII"));
            for (int i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                var r = region.Bounds;
                Console.WriteLine($"[{i:D3}] X:{r.X,4} Y:{r.Y,4} W:{r.Width,4} H:{r.Height,4} C:{region.Confidence * 100f,5:F1}");

                if (showAscii && r.Width > 200 && r.Height >= 16)
                {
                    DumpAscii(bitmap, r);
                }
            }
        }

        private static void DumpAscii(Bitmap bitmap, Rectangle region)
        {
            Console.WriteLine($"      ascii preview ({region.Width}x{region.Height})");
            const string ramp = " .:-=+*#%@";

            int sampleWidth = Math.Min(120, region.Width);
            int stepX = Math.Max(1, region.Width / sampleWidth);
            int stepY = Math.Max(1, region.Height / 16);

            for (int y = region.Top; y < region.Bottom; y += stepY)
            {
                var line = new char[sampleWidth];
                int xi = 0;
                for (int x = region.Left; x < region.Right && xi < sampleWidth; x += stepX, xi++)
                {
                    var color = bitmap.GetPixel(x, y);
                    int lum = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                    int bucket = (lum * (ramp.Length - 1)) / 255;
                    line[xi] = ramp[bucket];
                }

                Console.WriteLine("      " + new string(line));
            }
        }
    }
}
