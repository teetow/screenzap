using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace screenzap.lib
{

    static class FileUtils
    {
        public static string SaveImage(Image image)
        {
            Bitmap bmpScreenshot = new Bitmap(image);

            var dateStr = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss") + (".png");
            var userPath = Environment.ExpandEnvironmentVariables(Properties.Settings.Default.captureFolder);
            var filePath = Path.Combine(userPath, dateStr);

            using (FileStream pngFileStream = new FileStream(filePath, FileMode.Create))
            {
                bmpScreenshot.Save(pngFileStream, ImageFormat.Png);
            }
            return filePath;
        }
    }
    static class RectangleExt
    {
        public static Rectangle fromPoints(Point pt, Point pt2)
        {
            return new Rectangle(
                Math.Min(pt.X, pt2.X),
                Math.Min(pt.Y, pt2.Y),
                Math.Abs(pt.X - pt2.X),
                Math.Abs(pt.Y - pt2.Y)
                );
        }
    }

    static class SizeExt
    {
        public static Size Subtract(this Size pt, Size pt2)
        {
            return new Size(pt.Width - pt2.Width, pt.Height - pt2.Height);
        }

        public static Size Subtract(this Size pt, Point pt2)
        {
            return new Size(pt.Width - pt2.X, pt.Height - pt2.Y);
        }

        public static Size Multiply(this Size size, decimal factor)
        {
            return new Size((int)(size.Width * factor), (int)(size.Height * factor));
        }

        public static Size Divide(this Size size, Size divisor)
        {
            return new Size(size.Width / divisor.Width, size.Height / divisor.Height);
        }

        public static Size Divide(this Size size, decimal divisor)
        {
            return new Size((int)(size.Width / divisor), (int)(size.Height / divisor));
        }
    }
    static class PointExt
    {

        public static Point Add(this Point a, Point b)
        {
            return new Point(a.X + b.X, a.Y + b.Y);
        }

        public static Point Add(this Point a, Size b)
        {
            return new Point(a.X + b.Width, a.Y + b.Height);
        }

        public static Point Add(this Point a, decimal b)
        {
            return new Point((int)(a.X + b), (int)(a.Y + b));
        }

        public static Point Subtract(this Point pt, Point pt2)
        {
            return new Point(pt.X - pt2.X, pt.Y - pt2.Y);
        }

        public static Point Subtract(this Point pt, Size pt2)
        {
            return new Point(pt.X - pt2.Width, pt.Y - pt2.Height);
        }

        public static Point Multiply(this Point size, Point divisor)
        {
            return new Point(size.X * divisor.X, size.Y * divisor.Y);
        }

        public static Point Multiply(this Point size, decimal divisor)
        {
            return new Point((int)(size.X * divisor), (int)(size.Y * divisor));
        }

        public static Point Divide(this Point size, Point divisor)
        {
            return new Point(size.X / divisor.X, size.Y / divisor.Y);
        }

        public static Point Divide(this Point size, decimal divisor)
        {
            return new Point((int)(size.X / divisor), (int)(size.Y / divisor));
        }
    }
}
