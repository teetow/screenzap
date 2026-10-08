using System.ComponentModel;
using System.Runtime.InteropServices;

namespace screenzap.Native;

// Cache the two frozen appearances once. Drag frames use GDI copies, never a full-screen
// GDI+ alpha blend or a Bitmap-to-window conversion.
internal sealed class CaptureOverlayRenderer : IDisposable
{
    private readonly Surface original, dimmed, buffer;
    private readonly Size size;
    private readonly nint font, pen;
    private bool disposed;

    internal CaptureOverlayRenderer(Bitmap background)
    {
        size = background.Size;
        original = new Surface(background);
        try
        {
            using var dark = new Bitmap(background);
            using (var graphics = Graphics.FromImage(dark))
            using (var brush = new SolidBrush(Color.FromArgb(120, Color.Black)))
                graphics.FillRectangle(brush, new Rectangle(Point.Empty, size));
            dimmed = new Surface(dark);
            buffer = new Surface(dark);
            font = (SystemFonts.CaptionFont ?? SystemFonts.DefaultFont).ToHfont();
            pen = CreatePen(0, 2, 0xFFBF00); // DeepSkyBlue, COLORREF byte order.
            if (font == 0 || pen == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            SelectObject(buffer.Dc, font);
            SelectObject(buffer.Dc, pen);
            SelectObject(buffer.Dc, GetStockObject(5)); // NULL_BRUSH: keep the selection clear.
            SetTextColor(buffer.Dc, 0xFFFFFF);
            SetBkMode(buffer.Dc, 1); // TRANSPARENT
        }
        catch { Dispose(); throw; }
    }

    internal Rectangle CaptionBounds(Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0) return Rectangle.Empty;
        string caption = Caption(area);
        if (!GetTextExtentPoint32(buffer.Dc, caption, caption.Length, out var text))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        int x = Math.Clamp(area.Right - text.Width, 0, Math.Max(0, size.Width - text.Width));
        int y = area.Bottom + 4;
        if (y + text.Height > size.Height) y = Math.Max(0, area.Top - text.Height - 4);
        return new Rectangle(x, y, text.Width, text.Height);
    }

    internal void Paint(nint target, Rectangle dirty, nint dirtyRegion, Rectangle area)
    {
        if (dirty.Width <= 0 || dirty.Height <= 0) return;
        SelectClipRgn(buffer.Dc, dirtyRegion);
        try
        {
            Copy(buffer.Dc, dimmed.Dc, dirty);
            var clear = Rectangle.Intersect(dirty, area);
            if (clear.Width > 0 && clear.Height > 0) Copy(buffer.Dc, original.Dc, clear);
            if (area.Width > 0 && area.Height > 0)
            {
                DrawRectangle(buffer.Dc, area.Left - 1, area.Top - 1, area.Right + 2, area.Bottom + 2);
                var label = CaptionBounds(area);
                string caption = Caption(area);
                TextOut(buffer.Dc, label.X, label.Y, caption, caption.Length);
            }
            Copy(target, buffer.Dc, dirty);
        }
        finally { SelectClipRgn(buffer.Dc, 0); }
    }

    // Returns a caller-owned HRGN. Stable selection interiors are excluded: even a nearly
    // full-monitor selection only repaints the changed strips, border, and dimension label.
    internal nint CreateDamageRegion(Rectangle before, Rectangle after)
    {
        nint damage = CreateRectRgn(before.Left, before.Top, before.Right, before.Bottom);
        nint scratch = CreateRectRgn(after.Left, after.Top, after.Right, after.Bottom);
        if (damage == 0 || scratch == 0)
        {
            if (damage != 0) DeleteObject(damage);
            if (scratch != 0) DeleteObject(scratch);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            CombineRgn(damage, damage, scratch, 3); // XOR old/new clear areas.
            AddChrome(before);
            AddChrome(after);
            return damage;
        }
        catch { DeleteObject(damage); throw; }
        finally { DeleteObject(scratch); }

        void Add(Rectangle rect)
        {
            SetRectRgn(scratch, rect.Left, rect.Top, rect.Right, rect.Bottom);
            CombineRgn(damage, damage, scratch, 2); // OR
        }
        void AddChrome(Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return;
            var outer = Rectangle.Inflate(area, 3, 3);
            Add(new Rectangle(outer.Left, outer.Top, outer.Width, 6));
            Add(new Rectangle(outer.Left, outer.Bottom - 6, outer.Width, 6));
            Add(new Rectangle(outer.Left, outer.Top, 6, outer.Height));
            Add(new Rectangle(outer.Right - 6, outer.Top, 6, outer.Height));
            Add(Rectangle.Inflate(CaptionBounds(area), 2, 2));
        }
    }

    private static string Caption(Rectangle area) => $"{area.Width} x {area.Height}";
    private static void Copy(nint target, nint source, Rectangle rect)
    {
        if (!BitBlt(target, rect.X, rect.Y, rect.Width, rect.Height, source, rect.X, rect.Y, 0x00CC0020))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        buffer?.Dispose(); // Release the DC's selections before deleting fonts/pens.
        dimmed?.Dispose();
        original?.Dispose();
        if (font != 0) DeleteObject(font);
        if (pen != 0) DeleteObject(pen);
    }

    private sealed class Surface : IDisposable
    {
        internal nint Dc { get; private set; }
        private nint bitmap, previous;
        internal Surface(Bitmap source)
        {
            try
            {
                Dc = CreateCompatibleDC(0);
                bitmap = source.GetHbitmap();
                if (Dc == 0 || bitmap == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                previous = SelectObject(Dc, bitmap);
                if (previous == 0 || previous == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (Dc != 0)
            {
                if (previous != 0 && previous != -1) SelectObject(Dc, previous);
                DeleteDC(Dc);
                Dc = 0;
            }
            if (bitmap != 0) { DeleteObject(bitmap); bitmap = 0; }
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct TextSize { public int Width, Height; }
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll", EntryPoint = "Rectangle")] private static extern bool DrawRectangle(nint dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool TextOut(nint dc, int x, int y, string text, int count);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetTextExtentPoint32(nint dc, string text, int count, out TextSize size);
    [DllImport("gdi32.dll")] internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool SetRectRgn(nint region, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint target, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] private static extern int SelectClipRgn(nint dc, nint region);
}
