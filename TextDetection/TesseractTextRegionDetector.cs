using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Tesseract;

namespace TextDetection
{
    internal static class TesseractTextRegionDetector
    {
        private const string DefaultLanguage = "eng";
        private const float MinConfidence = 45f; // percentage
        private const int MinWidth = 12;
        private const int MinHeight = 8;
        private const int MergePadding = 6;

        public static bool TryFindTextRegions(Bitmap bitmap, out IReadOnlyList<DetectedTextRegion> regions, out string? failureReason)
        {
            if (bitmap == null)
            {
                throw new ArgumentNullException(nameof(bitmap));
            }

            regions = Array.Empty<DetectedTextRegion>();
            string language = ResolveLanguage();

            if (!TessdataLocator.TryLocate(language, out var tessdataPath, out failureReason))
            {
                return false;
            }

            try
            {
                var detected = TesseractEngineManager.Run(tessdataPath!, language, engine =>
                {
                    var allLines = new List<DetectedLine>();
                    foreach (var variant in GetPreprocessVariants())
                    {
                        using var pix = CreatePix(bitmap, variant);
                        using var page = engine.Process(pix, PageSegMode.Auto);
                        var words = ExtractWords(page, bitmap.Size);
                        if (words.Count == 0)
                        {
                            continue;
                        }

                        var lines = GroupWordsIntoLines(words, bitmap.Size);
                        if (lines.Count == 0)
                        {
                            continue;
                        }

                        allLines.AddRange(lines);

#if DEBUG
                        Console.WriteLine($"[Tesseract:{variant}] words={words.Count} lines={lines.Count}");
#endif
                    }

                    return allLines;
                });

                regions = MergeAndInflate(detected, bitmap.Size);

#if DEBUG
                Console.WriteLine($"[Tesseract] regions={regions.Count} lang={language} tessdata={tessdataPath}");
#endif
                return true;
            }
            catch (TesseractException ex)
            {
                failureReason = ex.Message;
#if DEBUG
                Console.WriteLine($"[Tesseract] {ex}");
#endif
                return false;
            }
            catch (DllNotFoundException ex)
            {
                failureReason = ex.Message;
#if DEBUG
                Console.WriteLine($"[Tesseract] {ex}");
#endif
                return false;
            }
        }

        private static string ResolveLanguage()
        {
            var overrideLang = Environment.GetEnvironmentVariable("SCREENZAP_TESSDATA_LANG");
            if (!string.IsNullOrWhiteSpace(overrideLang))
            {
                return overrideLang.Trim();
            }

            return DefaultLanguage;
        }

        private static IReadOnlyList<DetectedTextRegion> MergeAndInflate(List<DetectedLine> lines, Size bounds)
        {
            if (lines.Count == 0)
            {
                return Array.Empty<DetectedTextRegion>();
            }

            var ordered = lines
                .OrderBy(line => line.Bounds.Top)
                .ThenBy(line => line.Bounds.Left)
                .ToList();

            var result = new List<DetectedTextRegion>();
            foreach (var line in ordered)
            {
                var rect = InflateClamped(line.Bounds, MergePadding, bounds);
                if (rect.Width < MinWidth || rect.Height < MinHeight)
                {
                    continue;
                }

                bool merged = false;
                for (int i = 0; i < result.Count; i++)
                {
                    var existing = result[i];
                    if (ShouldMergeLines(existing.Bounds, rect))
                    {
                        var mergedBounds = UnionClamped(existing.Bounds, rect, bounds);
                        float mergedConfidence = Math.Max(existing.Confidence, line.Confidence);
                        result[i] = new DetectedTextRegion(mergedBounds, mergedConfidence);
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    result.Add(new DetectedTextRegion(rect, line.Confidence));
                }
            }

            return result;
        }

        private static bool ShouldMergeLines(Rectangle a, Rectangle b)
        {
            int verticalOverlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            if (verticalOverlap <= 0)
            {
                return false;
            }

            int minHeight = Math.Min(a.Height, b.Height);
            if (minHeight == 0)
            {
                return false;
            }

            if (verticalOverlap < minHeight * 0.4f)
            {
                return false;
            }

            int horizontalGap = Math.Max(0, Math.Max(a.Left, b.Left) - Math.Min(a.Right, b.Right));
            if (horizontalGap > Math.Min(Math.Max(a.Width, b.Width) * 0.08f, 18f))
            {
                return false;
            }

            return true;
        }

        private static Rectangle InflateRect(Rectangle rect, int padding)
        {
            if (padding <= 0)
            {
                return rect;
            }

            rect.Inflate(padding, padding);
            return rect;
        }

        private static Rectangle UnionClamped(Rectangle a, Rectangle b, Size bounds)
        {
            var union = Rectangle.Union(a, b);
            return ClampToBounds(union, bounds);
        }

        private static Rectangle InflateClamped(Rectangle rect, int padding, Size bounds)
        {
            rect = InflateRect(rect, padding);
            return ClampToBounds(rect, bounds);
        }

        private static Rectangle ClampToBounds(Rectangle rect, Size bounds)
        {
            int left = Math.Max(0, rect.Left);
            int top = Math.Max(0, rect.Top);
            int right = Math.Min(bounds.Width, rect.Right);
            int bottom = Math.Min(bounds.Height, rect.Bottom);

            if (right <= left || bottom <= top)
            {
                return Rectangle.Empty;
            }

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static List<DetectedWord> ExtractWords(Page page, Size imageSize)
        {
            var words = new List<DetectedWord>();
            using var iterator = page.GetIterator();
            if (iterator == null)
            {
                return words;
            }

            iterator.Begin();
            do
            {
                if (!iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                {
                    continue;
                }

                var bounds = new Rectangle(rect.X1, rect.Y1, rect.Width, rect.Height);
                bounds = ClampToBounds(bounds, imageSize);
                if (bounds.Width < MinWidth || bounds.Height < MinHeight)
                {
                    continue;
                }

                string? text = iterator.GetText(PageIteratorLevel.Word);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                text = text.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                float confidence = iterator.GetConfidence(PageIteratorLevel.Word);
                if (float.IsNaN(confidence) || confidence < MinConfidence)
                {
                    continue;
                }

                words.Add(new DetectedWord(bounds, confidence / 100f, text));
            }
            while (iterator.Next(PageIteratorLevel.Word));

            return words;
        }

        private static List<DetectedLine> GroupWordsIntoLines(List<DetectedWord> words, Size bounds)
        {
            var lines = new List<DetectedLine>();
            if (words.Count == 0)
            {
                return lines;
            }

            var groups = new List<WordGroup>();
            foreach (var word in words.OrderBy(w => w.Bounds.Top).ThenBy(w => w.Bounds.Left))
            {
                WordGroup? bestGroup = null;
                float bestDistance = float.MaxValue;

                foreach (var group in groups)
                {
                    if (!AreOnSameLine(group.Bounds, word.Bounds))
                    {
                        continue;
                    }

                    float distance = Math.Abs((group.Bounds.Top + group.Bounds.Height / 2f) - (word.Bounds.Top + word.Bounds.Height / 2f));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestGroup = group;
                    }
                }

                if (bestGroup == null)
                {
                    var group = new WordGroup(word);
                    groups.Add(group);
                }
                else
                {
                    bestGroup.Add(word);
                }
            }

            foreach (var group in groups)
            {
                group.SortWords();
                SplitRuns(group.Words, lines, bounds);
            }

            return lines;
        }

        private static bool AreOnSameLine(Rectangle a, Rectangle b)
        {
            int verticalOverlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            if (verticalOverlap <= 0)
            {
                return false;
            }

            int minHeight = Math.Min(a.Height, b.Height);
            if (minHeight == 0)
            {
                return false;
            }

            return verticalOverlap >= minHeight * 0.55f;
        }

        private static IEnumerable<PreprocessVariant> GetPreprocessVariants()
        {
            yield return PreprocessVariant.Original;
            yield return PreprocessVariant.Inverted;
            yield return PreprocessVariant.RedChannel;
            yield return PreprocessVariant.GreenChannel;
            yield return PreprocessVariant.BlueChannel;
        }

        private static Pix CreatePix(Bitmap bitmap, PreprocessVariant variant)
        {
            Bitmap? processed = null;

            try
            {
                processed = variant switch
                {
                    PreprocessVariant.Original => null,
                    PreprocessVariant.Inverted => CreateInvertedCopy(bitmap),
                    PreprocessVariant.RedChannel => CreateChannelCopy(bitmap, ColorChannel.Red),
                    PreprocessVariant.GreenChannel => CreateChannelCopy(bitmap, ColorChannel.Green),
                    PreprocessVariant.BlueChannel => CreateChannelCopy(bitmap, ColorChannel.Blue),
                    _ => null
                };

                var source = processed ?? bitmap;
                using var stream = new MemoryStream();
                source.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                return Pix.LoadFromMemory(stream.ToArray());
            }
            finally
            {
                processed?.Dispose();
            }
        }

        private static Bitmap CreateInvertedCopy(Bitmap source)
        {
            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(copy))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            var rect = new Rectangle(0, 0, copy.Width, copy.Height);
            var data = copy.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            try
            {
                int length = Math.Abs(data.Stride) * copy.Height;
                var buffer = new byte[length];
                Marshal.Copy(data.Scan0, buffer, 0, length);

                for (int i = 0; i < length; i += 4)
                {
                    buffer[i] = (byte)(255 - buffer[i]);         // B
                    buffer[i + 1] = (byte)(255 - buffer[i + 1]);   // G
                    buffer[i + 2] = (byte)(255 - buffer[i + 2]);   // R
                    // Alpha channel (i + 3) remains unchanged.
                }

                Marshal.Copy(buffer, 0, data.Scan0, length);
            }
            finally
            {
                copy.UnlockBits(data);
            }

            return copy;
        }

        private static Bitmap CreateChannelCopy(Bitmap source, ColorChannel channel)
        {
            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(copy))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            var rect = new Rectangle(0, 0, copy.Width, copy.Height);
            var data = copy.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            try
            {
                int length = Math.Abs(data.Stride) * copy.Height;
                var buffer = new byte[length];
                Marshal.Copy(data.Scan0, buffer, 0, length);

                for (int i = 0; i < length; i += 4)
                {
                    byte value = channel switch
                    {
                        ColorChannel.Red => buffer[i + 2],
                        ColorChannel.Green => buffer[i + 1],
                        ColorChannel.Blue => buffer[i],
                        _ => 0
                    };

                    buffer[i] = value;
                    buffer[i + 1] = value;
                    buffer[i + 2] = value;
                    // Leave alpha untouched at i + 3.
                }

                Marshal.Copy(buffer, 0, data.Scan0, length);
            }
            finally
            {
                copy.UnlockBits(data);
            }

            return copy;
        }

        private enum PreprocessVariant
        {
            Original,
            Inverted,
            RedChannel,
            GreenChannel,
            BlueChannel
        }

        private enum ColorChannel
        {
            Red,
            Green,
            Blue
        }

        private readonly struct DetectedWord
        {
            public DetectedWord(Rectangle bounds, float confidence, string text)
            {
                Bounds = bounds;
                Confidence = confidence;
                Text = text;
            }

            public Rectangle Bounds { get; }

            public float Confidence { get; }

            public string Text { get; }
        }

        private readonly struct DetectedLine
        {
            public DetectedLine(Rectangle bounds, float confidence, string text, int wordCount)
            {
                Bounds = bounds;
                Confidence = confidence;
                Text = text;
                WordCount = wordCount;
            }

            public Rectangle Bounds { get; }

            public float Confidence { get; }

            public string Text { get; }

            public int WordCount { get; }
        }

        private sealed class WordGroup
        {
            private Rectangle _bounds;

            public WordGroup(DetectedWord word)
            {
                Words = new List<DetectedWord> { word };
                _bounds = word.Bounds;
            }

            public List<DetectedWord> Words { get; }

            public Rectangle Bounds => _bounds;

            public void Add(DetectedWord word)
            {
                Words.Add(word);
                _bounds = Union(_bounds, word.Bounds);
            }

            public void SortWords()
            {
                Words.Sort((a, b) =>
                {
                    int cmp = a.Bounds.Left.CompareTo(b.Bounds.Left);
                    if (cmp != 0)
                    {
                        return cmp;
                    }

                    return a.Bounds.Top.CompareTo(b.Bounds.Top);
                });
            }

            public string BuildText()
            {
                return string.Join(" ", Words.Select(w => w.Text));
            }

            private static Rectangle Union(Rectangle a, Rectangle b)
            {
                if (a == Rectangle.Empty)
                {
                    return b;
                }

                if (b == Rectangle.Empty)
                {
                    return a;
                }

                return Rectangle.Union(a, b);
            }
        }

        private static void SplitRuns(IReadOnlyList<DetectedWord> words, List<DetectedLine> lines, Size bounds)
        {
            if (words == null || words.Count == 0)
            {
                return;
            }

            if (words.Count == 1)
            {
                AddLine(words, lines, bounds);
                return;
            }

            float avgHeight = words.Sum(w => w.Bounds.Height) / (float)words.Count;
            float threshold = Math.Max(28f, Math.Min(160f, avgHeight * 1.6f));

            int maxGap = 0;
            int maxGapIndex = -1;
            for (int i = 0; i < words.Count - 1; i++)
            {
                int gap = words[i + 1].Bounds.Left - words[i].Bounds.Right;
                if (gap > maxGap)
                {
                    maxGap = gap;
                    maxGapIndex = i;
                }
            }

            if (maxGapIndex >= 0 && maxGap > threshold)
            {
                var left = new List<DetectedWord>(maxGapIndex + 1);
                for (int i = 0; i <= maxGapIndex; i++)
                {
                    left.Add(words[i]);
                }

                var right = new List<DetectedWord>(words.Count - maxGapIndex - 1);
                for (int i = maxGapIndex + 1; i < words.Count; i++)
                {
                    right.Add(words[i]);
                }

                SplitRuns(left, lines, bounds);
                SplitRuns(right, lines, bounds);
                return;
            }

            AddLine(words, lines, bounds);
        }

        private static void AddLine(IReadOnlyList<DetectedWord> words, List<DetectedLine> lines, Size imageBounds)
        {
            if (words == null || words.Count == 0)
            {
                return;
            }

            Rectangle rect = words[0].Bounds;
            for (int i = 1; i < words.Count; i++)
            {
                rect = Rectangle.Union(rect, words[i].Bounds);
            }

            rect = ClampToBounds(rect, imageBounds);
            if (rect.Width < MinWidth || rect.Height < MinHeight)
            {
                return;
            }

            string text = string.Join(" ", words.Select(w => w.Text));
            if (text.Length == 0)
            {
                return;
            }

            float confidence = words.Sum(w => w.Confidence) / words.Count;
            lines.Add(new DetectedLine(rect, confidence, text, words.Count));
        }
    }

    internal static class TessdataLocator
    {
        public static bool TryLocate(string language, out string? tessdataPath, out string? failureReason)
        {
            foreach (var candidate in EnumerateCandidates())
            {
                if (TryResolve(candidate, language, out tessdataPath))
                {
                    failureReason = null;
                    return true;
                }
            }

            tessdataPath = null;
            failureReason = string.Format(CultureInfo.InvariantCulture,
                "No tessdata directory containing '{0}.traineddata' was found. Set SCREENZAP_TESSDATA_PATH or copy tessdata next to the executable.",
                language);
            return false;
        }

        private static IEnumerable<string> EnumerateCandidates()
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? envPath = Environment.GetEnvironmentVariable("SCREENZAP_TESSDATA_PATH");
            if (!string.IsNullOrWhiteSpace(envPath) && visited.Add(envPath))
            {
                yield return envPath;
            }

            string? tessdataPrefix = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
            if (!string.IsNullOrWhiteSpace(tessdataPrefix) && visited.Add(tessdataPrefix))
            {
                yield return tessdataPrefix;
            }

            string baseDir = AppContext.BaseDirectory;
            if (visited.Add(baseDir))
            {
                yield return baseDir;
            }

            string binTessdata = Path.Combine(baseDir, "tessdata");
            if (visited.Add(binTessdata))
            {
                yield return binTessdata;
            }

            string assemblyDir = Path.GetDirectoryName(typeof(TessdataLocator).Assembly.Location) ?? string.Empty;
            if (visited.Add(assemblyDir))
            {
                yield return assemblyDir;
            }

            string assemblyTessdata = Path.Combine(assemblyDir, "tessdata");
            if (visited.Add(assemblyTessdata))
            {
                yield return assemblyTessdata;
            }

            string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Screenzap", "tessdata");
            if (visited.Add(localAppData))
            {
                yield return localAppData;
            }
        }

        private static bool TryResolve(string candidate, string language, out string? path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            string normalized = candidate.Trim();
            if (Directory.Exists(normalized) && ContainsLanguage(normalized, language))
            {
                path = normalized;
                return true;
            }

            string nested = Path.Combine(normalized, "tessdata");
            if (Directory.Exists(nested) && ContainsLanguage(nested, language))
            {
                path = nested;
                return true;
            }

            return false;
        }

        private static bool ContainsLanguage(string directory, string language)
        {
            string file = Path.Combine(directory, language + ".traineddata");
            return File.Exists(file);
        }
    }

    internal static class TesseractEngineManager
    {
        private static readonly object SyncRoot = new();
        private static TesseractEngine? _engine;
        private static string? _currentPath;
        private static string? _currentLanguage;

        public static T Run<T>(string tessdataPath, string language, Func<TesseractEngine, T> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            lock (SyncRoot)
            {
                EnsureEngine(tessdataPath, language);
                if (_engine == null)
                {
                    throw new InvalidOperationException("Failed to initialize Tesseract engine.");
                }

                return operation(_engine);
            }
        }

        private static void EnsureEngine(string tessdataPath, string language)
        {
            if (_engine != null && string.Equals(_currentPath, tessdataPath, StringComparison.OrdinalIgnoreCase) && string.Equals(_currentLanguage, language, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _engine?.Dispose();
            _engine = new TesseractEngine(tessdataPath, language, EngineMode.LstmOnly);
            _engine.SetVariable("debug_file", "NUL");
            _engine.DefaultPageSegMode = PageSegMode.Auto;

            _currentPath = tessdataPath;
            _currentLanguage = language;
        }
    }
}
