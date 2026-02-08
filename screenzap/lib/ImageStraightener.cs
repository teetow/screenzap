using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace screenzap.lib
{
    /// <summary>
    /// Provides automatic image straightening via perspective correction and rotation deskew.
    /// Uses OpenCvSharp (OpenCV) for line/contour detection and geometric transforms.
    /// </summary>
    internal static class ImageStraightener
    {
        /// <summary>
        /// Minimum skew angle (degrees) to bother correcting.
        /// </summary>
        private const double MinSkewAngleDegrees = 0.5;

        /// <summary>
        /// Minimum fraction of image area a detected quadrilateral must cover
        /// to be considered a valid document/screen boundary.
        /// </summary>
        private const double MinQuadAreaFraction = 0.15;

        /// <summary>
        /// Maximum contour approximation epsilon as a fraction of the contour's arc length.
        /// </summary>
        private const double ContourApproxEpsilonFraction = 0.02;

        /// <summary>
        /// Minimum image dimension (pixels) to attempt straightening.
        /// </summary>
        private const int MinDimension = 50;

        /// <summary>
        /// Attempts to automatically straighten the image by first trying perspective correction
        /// (if a quadrilateral boundary is detected), then falling back to rotation deskew
        /// (using Hough line detection). Returns a new bitmap with the correction applied,
        /// or a clone of the input if no meaningful correction is detected.
        /// </summary>
        public static StraightenResult Straighten(Bitmap input)
        {
            if (input.Width < MinDimension || input.Height < MinDimension)
            {
                return StraightenResult.NoCorrection(input);
            }

            using var src = BitmapConverter.ToMat(input);
            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            // Stage A: try perspective correction via quadrilateral detection
            using var perspectiveResult = TryPerspectiveCorrection(src, gray);
            if (perspectiveResult != null)
            {
                return new StraightenResult(BitmapConverter.ToBitmap(perspectiveResult), true, true);
            }

            // Stage B: fall back to rotation deskew via Hough lines
            using var deskewResult = TryRotationDeskew(src, gray);
            if (deskewResult != null)
            {
                return new StraightenResult(BitmapConverter.ToBitmap(deskewResult), true, false);
            }

            return StraightenResult.NoCorrection(input);
        }

        /// <summary>
        /// Attempts to find a quadrilateral boundary in the image and applies a perspective
        /// warp to produce a rectangular output. Returns null if no suitable quad is found.
        /// </summary>
        private static Mat? TryPerspectiveCorrection(Mat src, Mat gray)
        {
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);

            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 50, 150);

            // Dilate edges to close small gaps in the boundary
            using var dilated = new Mat();
            var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            Cv2.Dilate(edges, dilated, kernel, iterations: 2);

            Cv2.FindContours(dilated, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            if (contours.Length == 0) return null;

            double imageArea = src.Rows * src.Cols;

            // Sort contours by area descending, check each for a 4-point polygon
            var sorted = contours
                .Select(c => new { Contour = c, Area = Cv2.ContourArea(c) })
                .Where(c => c.Area > imageArea * MinQuadAreaFraction)
                .OrderByDescending(c => c.Area);

            foreach (var candidate in sorted)
            {
                double peri = Cv2.ArcLength(candidate.Contour, true);
                var approx = Cv2.ApproxPolyDP(candidate.Contour, ContourApproxEpsilonFraction * peri, true);

                if (approx.Length != 4) continue;

                // Check that the quad is convex
                var approxF = approx.Select(p => new Point2f(p.X, p.Y)).ToArray();
                if (!Cv2.IsContourConvex(approx)) continue;

                // Order corners: top-left, top-right, bottom-right, bottom-left
                var ordered = OrderCornerPoints(approxF);

                // Compute output dimensions from the quad's edge lengths
                double widthTop = Distance(ordered[0], ordered[1]);
                double widthBottom = Distance(ordered[3], ordered[2]);
                int maxWidth = (int)Math.Max(widthTop, widthBottom);

                double heightLeft = Distance(ordered[0], ordered[3]);
                double heightRight = Distance(ordered[1], ordered[2]);
                int maxHeight = (int)Math.Max(heightLeft, heightRight);

                if (maxWidth < MinDimension || maxHeight < MinDimension) continue;

                // Check if the perspective distortion is significant enough to warrant correction.
                // If the quad is already very close to a rectangle aligned with image edges, skip.
                if (!IsSignificantPerspective(ordered, src.Cols, src.Rows))
                    continue;

                var dstPts = new Point2f[]
                {
                    new Point2f(0, 0),
                    new Point2f(maxWidth - 1, 0),
                    new Point2f(maxWidth - 1, maxHeight - 1),
                    new Point2f(0, maxHeight - 1)
                };

                using var M = Cv2.GetPerspectiveTransform(ordered, dstPts);
                var result = new Mat();
                Cv2.WarpPerspective(src, result, M, new OpenCvSharp.Size(maxWidth, maxHeight),
                    InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);

                return result;
            }

            return null;
        }

        /// <summary>
        /// Checks if the detected quadrilateral represents a meaningful perspective distortion
        /// (i.e., not just the image edges or a nearly-axis-aligned rectangle).
        /// </summary>
        private static bool IsSignificantPerspective(Point2f[] quad, int imageWidth, int imageHeight)
        {
            // Each corner's distance from the nearest image corner
            var imageCorners = new Point2f[]
            {
                new Point2f(0, 0),
                new Point2f(imageWidth - 1, 0),
                new Point2f(imageWidth - 1, imageHeight - 1),
                new Point2f(0, imageHeight - 1)
            };

            double totalDeviation = 0;
            for (int i = 0; i < 4; i++)
            {
                totalDeviation += Distance(quad[i], imageCorners[i]);
            }

            // If the total deviation is less than 2% of the image diagonal, it's essentially
            // the image boundary — no perspective correction needed
            double diag = Math.Sqrt(imageWidth * imageWidth + imageHeight * imageHeight);
            if (totalDeviation < diag * 0.02) return false;

            // Also check angles — if all angles are close to 90°, minimal distortion
            double maxAngleDeviation = 0;
            for (int i = 0; i < 4; i++)
            {
                var a = quad[i];
                var b = quad[(i + 1) % 4];
                var c = quad[(i + 2) % 4];

                double angle = AngleBetweenPoints(a, b, c);
                maxAngleDeviation = Math.Max(maxAngleDeviation, Math.Abs(angle - 90));
            }

            // If no angle deviates more than 3° from 90°, skip
            return maxAngleDeviation > 3.0;
        }

        /// <summary>
        /// Returns the angle (in degrees) at point b, formed by the line segments b→a and b→c.
        /// </summary>
        private static double AngleBetweenPoints(Point2f a, Point2f b, Point2f c)
        {
            double v1x = a.X - b.X, v1y = a.Y - b.Y;
            double v2x = c.X - b.X, v2y = c.Y - b.Y;
            double dot = v1x * v2x + v1y * v2y;
            double mag1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            double mag2 = Math.Sqrt(v2x * v2x + v2y * v2y);
            if (mag1 < 1e-6 || mag2 < 1e-6) return 0;
            double cosAngle = Math.Max(-1, Math.Min(1, dot / (mag1 * mag2)));
            return Math.Acos(cosAngle) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Attempts to detect the dominant text line angle via Hough line detection and
        /// rotates the image to correct the skew. Returns null if no meaningful skew is detected.
        /// </summary>
        private static Mat? TryRotationDeskew(Mat src, Mat gray)
        {
            using var binary = new Mat();
            Cv2.AdaptiveThreshold(gray, binary, 255,
                AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 15, 10);
            // Invert so text becomes white (foreground)
            Cv2.BitwiseNot(binary, binary);

            var lines = Cv2.HoughLinesP(binary, 1, Math.PI / 180, 80,
                minLineLength: Math.Max(src.Cols / 8, 30), maxLineGap: 10);

            if (lines.Length == 0) return null;

            // Compute angles of near-horizontal lines (within ±45° of horizontal)
            var angles = lines
                .Select(seg =>
                {
                    double dx = seg.P2.X - seg.P1.X;
                    double dy = seg.P2.Y - seg.P1.Y;
                    return Math.Atan2(dy, dx) * 180.0 / Math.PI;
                })
                .Where(a => Math.Abs(a) < 45)
                .ToArray();

            if (angles.Length == 0) return null;

            // Use the median angle for robustness against outliers
            Array.Sort(angles);
            double medianAngle = angles[angles.Length / 2];

            if (Math.Abs(medianAngle) < MinSkewAngleDegrees) return null;

            // Rotate to correct the skew
            var center = new Point2f(src.Cols / 2f, src.Rows / 2f);
            using var rotM = Cv2.GetRotationMatrix2D(center, medianAngle, 1.0);

            // Compute new bounding box to avoid clipping
            double absAngle = Math.Abs(medianAngle * Math.PI / 180.0);
            int newWidth = (int)(src.Cols * Math.Cos(absAngle) + src.Rows * Math.Sin(absAngle));
            int newHeight = (int)(src.Cols * Math.Sin(absAngle) + src.Rows * Math.Cos(absAngle));

            // Adjust the rotation matrix to account for the new center
            rotM.Set<double>(0, 2, rotM.Get<double>(0, 2) + (newWidth - src.Cols) / 2.0);
            rotM.Set<double>(1, 2, rotM.Get<double>(1, 2) + (newHeight - src.Rows) / 2.0);

            var result = new Mat();
            Cv2.WarpAffine(src, result, rotM, new OpenCvSharp.Size(newWidth, newHeight),
                InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);

            return result;
        }

        /// <summary>
        /// Orders 4 corner points as: top-left, top-right, bottom-right, bottom-left.
        /// Uses the sum (x+y) and difference (y-x) heuristic.
        /// </summary>
        private static Point2f[] OrderCornerPoints(Point2f[] pts)
        {
            if (pts.Length != 4) throw new ArgumentException("Expected exactly 4 points.");

            var result = new Point2f[4];

            var sums = pts.Select(p => p.X + p.Y).ToArray();
            var diffs = pts.Select(p => p.Y - p.X).ToArray();

            // Top-left has the smallest sum, bottom-right the largest
            result[0] = pts[Array.IndexOf(sums, sums.Min())];
            result[2] = pts[Array.IndexOf(sums, sums.Max())];

            // Top-right has the smallest difference, bottom-left the largest
            result[1] = pts[Array.IndexOf(diffs, diffs.Min())];
            result[3] = pts[Array.IndexOf(diffs, diffs.Max())];

            return result;
        }

        private static double Distance(Point2f a, Point2f b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// Result of a straightening operation.
    /// </summary>
    internal class StraightenResult
    {
        /// <summary>The output bitmap (always non-null). May be a corrected image or a clone of the input.</summary>
        public Bitmap Image { get; }

        /// <summary>True if any correction was applied.</summary>
        public bool WasCorrected { get; }

        /// <summary>True if the correction was a perspective warp (dimensions may differ from input).</summary>
        public bool IsPerspectiveCorrection { get; }

        public StraightenResult(Bitmap image, bool wasCorrected, bool isPerspectiveCorrection)
        {
            Image = image;
            WasCorrected = wasCorrected;
            IsPerspectiveCorrection = isPerspectiveCorrection;
        }

        public static StraightenResult NoCorrection(Bitmap input) =>
            new StraightenResult((Bitmap)input.Clone(), false, false);
    }
}
