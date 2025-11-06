using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace TextDetection
{
    /// <summary>
    /// Provides rectangular text region detection using simple contrast-based clustering.
    /// </summary>
    public static class TextRegionDetector
    {
        private const int EdgeThreshold = 52;
        private const int MergePadding = 4;
        private const int MinComponentPixels = 32;
        private const int MaxComponentPixelsDivisor = 12;

        private static readonly (int dx, int dy)[] NeighborOffsets =
        {
            (-1, 0),
            (1, 0),
            (0, -1),
            (0, 1)
        };

        private struct RowRange
        {
            public int Start;
            public int End;
        }

        private struct ColumnRange
        {
            public int Start;
            public int End;
        }

        /// <summary>
        /// Locate rectangular areas that likely contain rendered text glyphs.
        /// </summary>
        /// <param name="bitmap">Source image.</param>
        /// <returns>Rectangles in bitmap coordinates.</returns>
        public static IReadOnlyList<DetectedTextRegion> FindTextRegionsDetailed(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                throw new ArgumentNullException(nameof(bitmap));
            }

            if (bitmap.Width == 0 || bitmap.Height == 0)
            {
                return Array.Empty<DetectedTextRegion>();
            }

            if (TesseractTextRegionDetector.TryFindTextRegions(bitmap, out var tesseractRegions, out var tessFailure))
            {
                if (tesseractRegions.Count > 0)
                {
#if DEBUG
                    Console.WriteLine($"[Detector] Using Tesseract result set ({tesseractRegions.Count} regions).");
#endif
                    return tesseractRegions;
                }

#if DEBUG
                Console.WriteLine("[Detector] Tesseract returned zero regions, falling back to heuristic detector.");
#endif
            }
            else if (!string.IsNullOrWhiteSpace(tessFailure))
            {
#if DEBUG
                Console.WriteLine($"[Detector] Tesseract unavailable: {tessFailure}");
#endif
            }

            return FindTextRegionsHeuristic(bitmap);
        }

        public static IReadOnlyList<Rectangle> FindTextRegions(Bitmap bitmap)
        {
            var detailed = FindTextRegionsDetailed(bitmap);
            if (detailed.Count == 0)
            {
                return Array.Empty<Rectangle>();
            }

            var rectangles = new Rectangle[detailed.Count];
            for (int i = 0; i < detailed.Count; i++)
            {
                rectangles[i] = detailed[i].Bounds;
            }

            return rectangles;
        }

        private static IReadOnlyList<DetectedTextRegion> FindTextRegionsHeuristic(Bitmap bitmap)
        {
            var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var lockRect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = data.Stride / 4;
                int width = bitmap.Width;
                int height = bitmap.Height;
                int totalPixels = stride * height;

                var pixels = new int[totalPixels];
                Marshal.Copy(data.Scan0, pixels, 0, totalPixels);

                var luminance = new byte[totalPixels];
                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = rowStart + x;
                        int argb = pixels[idx];
                        int alpha = (argb >> 24) & 0xFF;
                        if (alpha < 16)
                        {
                            luminance[idx] = 255;
                            continue;
                        }

                        int r = (argb >> 16) & 0xFF;
                        int g = (argb >> 8) & 0xFF;
                        int b = argb & 0xFF;
                        int lum = (r * 299 + g * 587 + b * 114) / 1000;
                        luminance[idx] = (byte)lum;
                    }
                }

                var visited = new bool[totalPixels];
                var edgeMask = new bool[totalPixels];
                var components = new List<Rectangle>();

                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = rowStart + x;
                        if (visited[idx])
                        {
                            continue;
                        }

                        if (!IsEdgeCandidate(idx, x, y, width, height, stride, luminance, pixels))
                        {
                            continue;
                        }

                        if (TryExtractComponent(idx, x, y, width, height, stride, luminance, pixels, visited, edgeMask, bounds, out var rect))
                        {
                            components.Add(rect);
                        }
                    }
                }

                if (components.Count == 0)
                {
                    return Array.Empty<DetectedTextRegion>();
                }

                var merged = MergeComponents(components, bounds);
#if DEBUG
                Console.WriteLine($"[Detector] components={components.Count}, merged={merged.Count}");
#endif
                if (merged.Count == 0)
                {
                    return Array.Empty<DetectedTextRegion>();
                }

                var refined = new List<Rectangle>();
                foreach (var rect in merged)
                {
                    RefineComponent(rect, width, height, stride, edgeMask, bounds, refined);
                }

                if (refined.Count == 0)
                {
                    return Array.Empty<DetectedTextRegion>();
                }

                var consolidated = ConsolidateRegions(refined, bounds, stride, edgeMask);
                if (consolidated.Count == 0)
                {
                    return Array.Empty<DetectedTextRegion>();
                }

                return consolidated.Select(rect => new DetectedTextRegion(rect, 0f)).ToList();
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static bool TryExtractComponent(int startIndex, int startX, int startY, int width, int height, int stride, byte[] luminance, int[] pixels, bool[] visited, bool[] edgeMask, Rectangle bounds, out Rectangle rect)
        {
            var stack = new Stack<int>();
            stack.Push(startIndex);
            visited[startIndex] = true;
            var componentIndices = new List<int>();

            int minX = startX;
            int maxX = startX;
            int minY = startY;
            int maxY = startY;
            int count = 0;
            int maxPixels = Math.Max(MinComponentPixels, (width * height) / MaxComponentPixelsDivisor);

            while (stack.Count > 0)
            {
                int current = stack.Pop();
                count++;
                componentIndices.Add(current);
                edgeMask[current] = true;

                if (count > maxPixels)
                {
                    foreach (var idx in componentIndices)
                    {
                        edgeMask[idx] = false;
                    }
                    rect = Rectangle.Empty;
                    return false;
                }

                int cy = current / stride;
                int cx = current - (cy * stride);
                if (cx < 0 || cx >= width || cy < 0 || cy >= height)
                {
                    continue;
                }

                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cy < minY) minY = cy;
                if (cy > maxY) maxY = cy;

                foreach (var (dx, dy) in NeighborOffsets)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    int neighborIndex = ny * stride + nx;
                    if (visited[neighborIndex])
                    {
                        continue;
                    }

                    if (!IsEdgeCandidate(neighborIndex, nx, ny, width, height, stride, luminance, pixels))
                    {
                        continue;
                    }

                    visited[neighborIndex] = true;
                    edgeMask[neighborIndex] = true;
                    stack.Push(neighborIndex);
                }
            }

            if (count < MinComponentPixels)
            {
                foreach (var idx in componentIndices)
                {
                    edgeMask[idx] = false;
                }
                rect = Rectangle.Empty;
                return false;
            }

            rect = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            rect = ClampToBounds(rect, bounds);
            rect = InflateClamped(rect, 2, bounds);
            return rect.Width > 0 && rect.Height > 0;
        }

        private static bool IsEdgeCandidate(int index, int x, int y, int width, int height, int stride, byte[] luminance, int[] pixels)
        {
            int argb = pixels[index];
            int alpha = (argb >> 24) & 0xFF;
            if (alpha < 16)
            {
                return false;
            }

            int center = luminance[index];
            int horizontalDiff = 0;
            int verticalDiff = 0;

            if (x > 0)
            {
                int diff = Math.Abs(center - luminance[index - 1]);
                if (diff > horizontalDiff) horizontalDiff = diff;
            }

            if (x + 1 < width)
            {
                int diff = Math.Abs(center - luminance[index + 1]);
                if (diff > horizontalDiff) horizontalDiff = diff;
            }

            if (y > 0)
            {
                int diff = Math.Abs(center - luminance[index - stride]);
                if (diff > verticalDiff) verticalDiff = diff;
            }

            if (y + 1 < height)
            {
                int diff = Math.Abs(center - luminance[index + stride]);
                if (diff > verticalDiff) verticalDiff = diff;
            }

            int strongest = Math.Max(horizontalDiff, verticalDiff);
            if (strongest < EdgeThreshold)
            {
                return false;
            }

            int secondaryThreshold = EdgeThreshold / 2;
            if (horizontalDiff < secondaryThreshold && verticalDiff < secondaryThreshold)
            {
                return false;
            }

            return true;
        }

        private static List<Rectangle> MergeComponents(List<Rectangle> rects, Rectangle bounds)
        {
            var working = new List<Rectangle>(rects);
            bool changed;

            do
            {
                changed = false;
                for (int i = 0; i < working.Count; i++)
                {
                    var a = working[i];
                    var expandedA = InflateClamped(a, MergePadding, bounds);

                    for (int j = i + 1; j < working.Count; j++)
                    {
                        var b = working[j];
                        var expandedB = InflateClamped(b, MergePadding, bounds);

                        if (expandedA.IntersectsWith(expandedB))
                        {
                            var union = Rectangle.Union(a, b);
                            union = ClampToBounds(union, bounds);
                            working[i] = union;
                            working.RemoveAt(j);
                            changed = true;
                            break;
                        }
                    }

                    if (changed)
                    {
                        break;
                    }
                }
            }
            while (changed);

            return working;
        }

        private static void RefineComponent(Rectangle rect, int width, int height, int stride, bool[] edgeMask, Rectangle bounds, List<Rectangle> output)
        {
            rect = ClampToBounds(rect, bounds);
            if (rect.Width < 6 || rect.Height < 6)
            {
                return;
            }

            if ((rect.Height > rect.Width * 6 && rect.Width < 120) || (rect.Height >= 300 && rect.Width <= 160))
            {
                return;
            }

            int area = rect.Width * rect.Height;
            bool allowSplit = area > 1500 && rect.Height > 10 && rect.Width > 20;

            if (!allowSplit)
            {
                if (ShouldKeep(rect, stride, edgeMask))
                {
                    output.Add(rect);
                }
                return;
            }

            var rows = IdentifyRowRanges(rect, stride, edgeMask);
#if DEBUG
            if (rect.Width * rect.Height > 20000)
            {
                Console.WriteLine($"[Detector] refine {rect} rows={rows.Count}");
            }
#endif
            if (rows.Count == 0)
            {
                if (ShouldKeep(rect, stride, edgeMask))
                {
                    output.Add(rect);
                }
                return;
            }

            foreach (var row in rows)
            {
                var columns = IdentifyColumnRanges(row, rect, stride, edgeMask);
                if (columns.Count == 0)
                {
                    continue;
                }

                foreach (var column in columns)
                {
                    var candidate = Rectangle.FromLTRB(column.Start, row.Start, column.End, row.End);
                    candidate = InflateClamped(candidate, 1, bounds);
                    if (ShouldKeep(candidate, stride, edgeMask))
                    {
                        output.Add(candidate);
                    }
                }
            }
        }

        private static List<RowRange> IdentifyRowRanges(Rectangle rect, int stride, bool[] edgeMask)
        {
            var rows = new List<RowRange>();
            int separatorHeight = Math.Max(2, rect.Height / 40);
            int activityThreshold = Math.Max(6, rect.Width / 30);

            bool inRow = false;
            int rowStart = rect.Top;
            int whitespaceRun = 0;

            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                int activity = MeasureRowActivity(rect, y, stride, edgeMask);
                bool isWhitespace = activity <= activityThreshold;

                if (!isWhitespace)
                {
                    if (!inRow)
                    {
                        inRow = true;
                        rowStart = y;
                    }

                    whitespaceRun = 0;
                }
                else if (inRow)
                {
                    whitespaceRun++;
                    if (whitespaceRun >= separatorHeight)
                    {
                        int rowEnd = y - whitespaceRun + 1;
                        if (rowEnd > rowStart)
                        {
                            rows.Add(new RowRange { Start = rowStart, End = rowEnd });
                        }

                        inRow = false;
                    }
                }
            }

            if (inRow)
            {
                rows.Add(new RowRange { Start = rowStart, End = rect.Bottom });
            }

            return rows;
        }

        private static List<ColumnRange> IdentifyColumnRanges(RowRange row, Rectangle rect, int stride, bool[] edgeMask)
        {
            var columns = new List<ColumnRange>();
            int startRow = row.Start;
            int endRow = row.End;
            int lineHeight = Math.Max(1, endRow - startRow);
            int windowWidth = rect.Width;
            if (windowWidth <= 0)
            {
                return columns;
            }

            var columnCounts = new int[windowWidth];
            for (int x = rect.Left; x < rect.Right; x++)
            {
                int offset = x - rect.Left;
                for (int y = startRow; y < endRow; y++)
                {
                    int idx = y * stride + x;
                    if (edgeMask[idx])
                    {
                        columnCounts[offset]++;
                    }
                }
            }

            int activityThreshold = Math.Max(1, lineHeight / 7);
            int gapTolerance = Math.Max(1, Math.Min(12, lineHeight / 2));

            bool inRegion = false;
            int regionStartOffset = 0;
            int lastActiveOffset = 0;
            int gap = 0;
            int globalFirstActive = -1;
            int globalLastActive = -1;
            int totalActivity = 0;

            for (int offset = 0; offset < windowWidth; offset++)
            {
                int count = columnCounts[offset];
                if (count > 0)
                {
                    if (globalFirstActive == -1)
                    {
                        globalFirstActive = offset;
                    }

                    globalLastActive = offset;
                    totalActivity += count;
                }

                if (count >= activityThreshold)
                {
                    if (!inRegion)
                    {
                        inRegion = true;
                        regionStartOffset = offset;
                    }

                    lastActiveOffset = offset;
                    gap = 0;
                }
                else if (inRegion)
                {
                    gap++;
                    if (gap > gapTolerance)
                    {
                        AddColumnRange(columns, rect.Left, regionStartOffset, lastActiveOffset);
                        inRegion = false;
                        gap = 0;
                    }
                }
            }

            if (inRegion)
            {
                AddColumnRange(columns, rect.Left, regionStartOffset, lastActiveOffset);
            }

            if (columns.Count == 0 && globalFirstActive != -1 && globalLastActive > globalFirstActive)
            {
                int width = globalLastActive - globalFirstActive + 1;
                int area = Math.Max(1, width * lineHeight);
                float density = totalActivity / (float)area;
                if (width >= 12 && density >= 0.02f)
                {
                    columns.Add(new ColumnRange
                    {
                        Start = rect.Left + globalFirstActive,
                        End = rect.Left + globalLastActive + 1
                    });
                }
            }

            return columns;
        }

        private static void AddColumnRange(List<ColumnRange> columns, int rectLeft, int startOffset, int lastActiveOffset)
        {
            if (lastActiveOffset < startOffset)
            {
                return;
            }

            int width = lastActiveOffset - startOffset + 1;
            if (width < 6)
            {
                return;
            }

            columns.Add(new ColumnRange
            {
                Start = rectLeft + startOffset,
                End = rectLeft + lastActiveOffset + 1
            });
        }

        private static int MeasureRowActivity(Rectangle rect, int y, int stride, bool[] edgeMask)
        {
            int rowOffset = y * stride;
            int ink = 0;

            for (int x = rect.Left; x < rect.Right; x++)
            {
                int idx = rowOffset + x;
                if (edgeMask[idx])
                {
                    ink++;
                }
            }

            return ink;
        }

        private static bool ShouldKeep(Rectangle rect, int stride, bool[] edgeMask)
        {
            if (rect.Width < 18 || rect.Height < 8)
            {
                return false;
            }

            if (rect.Height > 160)
            {
                return false;
            }

            float aspect = rect.Width / (float)rect.Height;
            if (aspect < 1.6f)
            {
                return false;
            }

            GetEdgeStats(rect, stride, edgeMask, out int edgeCount, out int activeColumns);
            float density = edgeCount / (float)(rect.Width * rect.Height);
            float columnFill = activeColumns / (float)Math.Max(1, rect.Width);
            if (density < 0.02f)
            {
                return false;
            }

            if (columnFill >= 0.92f && density >= 0.18f)
            {
#if DEBUG
                Console.WriteLine($"[Detector] drop dense rect={rect} density={density:F3} fill={columnFill:F2}");
#endif
                return false;
            }

#if DEBUG
            if (rect.Width > 200 || rect.Height > 48)
            {
                Console.WriteLine($"[Detector] density {density:F3} fill={columnFill:F2} edges={edgeCount} area={rect.Width * rect.Height} rect={rect}");
            }
#endif

            return true;
        }

        private static List<Rectangle> ConsolidateRegions(List<Rectangle> regions, Rectangle bounds, int stride, bool[] edgeMask)
        {
            if (regions.Count <= 1)
            {
                return new List<Rectangle>(regions);
            }

            regions.Sort((a, b) =>
            {
                int cmp = a.Y.CompareTo(b.Y);
                if (cmp != 0) return cmp;
                return a.X.CompareTo(b.X);
            });

            var result = new List<Rectangle>();
            foreach (var rect in regions)
            {
                bool merged = false;
                for (int i = 0; i < result.Count; i++)
                {
                    var existing = result[i];
                    if (AreNearlyAligned(existing, rect))
                    {
                        var union = Rectangle.Union(existing, rect);
                        union = InflateClamped(union, 1, bounds);
                        if (ShouldKeep(union, stride, edgeMask))
                        {
#if DEBUG
                            int gap = Math.Max(0, Math.Max(existing.Left, rect.Left) - Math.Min(existing.Right, rect.Right));
                            float fill = (existing.Width + rect.Width) / (float)Math.Max(1, union.Width);
                            if (union.Width > 400)
                            {
                                Console.WriteLine($"[Detector] union {existing} + {rect} -> {union} gap={gap} fill={fill:F2}");
                            }
#endif
                            result[i] = union;
                        }
                        merged = true;
                        break;
                    }
                }

                if (!merged && ShouldKeep(rect, stride, edgeMask))
                {
                    result.Add(rect);
                }
            }

            return result;
        }

        private static bool AreNearlyAligned(Rectangle a, Rectangle b)
        {
            int verticalOverlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            if (verticalOverlap <= 0)
            {
                return false;
            }

            int minHeight = Math.Min(a.Height, b.Height);
            if (verticalOverlap < minHeight * 0.6f)
            {
                return false;
            }

            int horizontalGap = Math.Max(0, Math.Max(a.Left, b.Left) - Math.Min(a.Right, b.Right));
            int allowedGap = Math.Min(minHeight * 2, 28);
            return horizontalGap <= allowedGap;
        }

        private static Rectangle ClampToBounds(Rectangle rect, Rectangle bounds)
        {
            int left = Math.Max(bounds.Left, rect.Left);
            int top = Math.Max(bounds.Top, rect.Top);
            int right = Math.Min(bounds.Right, rect.Right);
            int bottom = Math.Min(bounds.Bottom, rect.Bottom);

            if (right <= left || bottom <= top)
            {
                return Rectangle.Empty;
            }

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static Rectangle InflateClamped(Rectangle rect, int padding, Rectangle bounds)
        {
            if (padding <= 0 || rect.IsEmpty)
            {
                return rect;
            }

            int left = Math.Max(bounds.Left, rect.Left - padding);
            int top = Math.Max(bounds.Top, rect.Top - padding);
            int right = Math.Min(bounds.Right, rect.Right + padding);
            int bottom = Math.Min(bounds.Bottom, rect.Bottom + padding);

            if (right <= left || bottom <= top)
            {
                return Rectangle.Empty;
            }

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static void GetEdgeStats(Rectangle rect, int stride, bool[] edgeMask, out int edgeCount, out int activeColumns)
        {
            rect = Rectangle.FromLTRB(Math.Max(0, rect.Left), Math.Max(0, rect.Top), Math.Max(rect.Left, rect.Right), Math.Max(rect.Top, rect.Bottom));
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                edgeCount = 0;
                activeColumns = 0;
                return;
            }

            edgeCount = 0;
            activeColumns = 0;
            var columnFlags = new bool[rect.Width];
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                int rowOffset = y * stride;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    if (edgeMask[rowOffset + x])
                    {
                        edgeCount++;
                        columnFlags[x - rect.Left] = true;
                    }
                }
            }

            for (int i = 0; i < columnFlags.Length; i++)
            {
                if (columnFlags[i])
                {
                    activeColumns++;
                }
            }
        }
    }
}
