using System;

namespace screenzap.lib
{
    internal static class TextToneNormalizer
    {
        // Operates on the packed BGRA output of the background-division pass.
        internal static void Normalize(byte[] pixels)
        {
            var histogram = new int[256];
            int count = 0;
            double total = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                // Hidden / mostly transparent pixels must not set the document's levels.
                if (pixels[i + 3] < 128) continue;
                int tone = (54 * pixels[i + 2] + 183 * pixels[i + 1] + 19 * pixels[i] + 128) >> 8;
                histogram[tone]++;
                count++;
                total += tone;
            }

            if (count == 0) return;

            // Otsu's between-class variance separates ink from paper, even when ink
            // occupies less than one percent of the image. This is only an analysis
            // threshold: the output retains continuous tones and antialiased edges.
            int inkCount = 0;
            double inkSum = 0;
            double bestScore = 0;
            int split = -1;
            int bestInkCount = 0;
            for (int tone = 0; tone < 255; tone++)
            {
                inkCount += histogram[tone];
                inkSum += (double)tone * histogram[tone];
                int paperCount = count - inkCount;
                if (inkCount == 0 || paperCount == 0) continue;
                double difference = inkSum / inkCount - (total - inkSum) / paperCount;
                double score = (double)inkCount * paperCount * difference * difference;
                if (score > bestScore)
                {
                    bestScore = score;
                    split = tone;
                    bestInkCount = inkCount;
                }
            }

            if (split < 0) return;

            int inkMedian = Percentile(histogram, 0, split, bestInkCount, 0.5);
            int white = Percentile(histogram, split + 1, 255, count - bestInkCount, 0.1);
            // A nearly uniform page is usually paper grain, not faint writing.
            if (white - inkMedian < 24) return;

            // Leave room below the darkest typical ink instead of clipping its
            // bottom five percent. The margin preserves variation inside strokes.
            int black = Percentile(histogram, 0, split, bestInkCount, 0.01) - 16;
            // Limit the linear gain to about 4x for faint text / narrow ranges.
            black = Math.Max(0, Math.Min(black, white - 64));
            double midtone = (double)(inkMedian - black) / (white - black);
            double gamma = midtone > 0 && midtone < 1
                ? Math.Clamp(Math.Log(0.25) / Math.Log(midtone), 1.0, 2.5)
                : 1.0;

            var curve = new byte[256];
            for (int tone = 0; tone < curve.Length; tone++)
            {
                double normalized = Math.Clamp((double)(tone - black) / (white - black), 0.0, 1.0);
                // Retain a little linear response so gamma does not crush the
                // newly preserved shadow detail back to zero in 8-bit output.
                double adjusted = 0.9 * Math.Pow(normalized, gamma) + 0.1 * normalized;
                curve[tone] = (byte)Math.Round(255 * adjusted);
            }

            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] == 0) continue;
                // One shared curve keeps neutral pixels neutral; alpha is untouched.
                pixels[i] = curve[pixels[i]];
                pixels[i + 1] = curve[pixels[i + 1]];
                pixels[i + 2] = curve[pixels[i + 2]];
            }
        }

        private static int Percentile(int[] histogram, int low, int high, int count, double fraction)
        {
            int target = Math.Max(1, (int)Math.Ceiling(count * fraction));
            int cumulative = 0;
            for (int tone = low; tone <= high; tone++)
            {
                cumulative += histogram[tone];
                if (cumulative >= target) return tone;
            }
            return high;
        }
    }
}
