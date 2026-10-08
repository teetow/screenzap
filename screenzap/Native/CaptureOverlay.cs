using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using screenzap.Components;

namespace screenzap.Native;

internal sealed class CaptureOverlay : NativeWindow
{
    private readonly Bitmap background;
    private readonly Bitmap buffer;
    private readonly Rectangle bounds;
    private readonly CaptureSelection selection;
    private readonly TaskCompletionSource<Rectangle> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool shown;
    private bool completed;

    internal CaptureOverlay(Rectangle bounds, Bitmap background)
        : base("Screenzap Capture", bounds, extendedStyle: 0x08 | 0x80)
    {
        this.bounds = bounds;
        this.background = background;
        selection = new CaptureSelection(background.Size);
        buffer = new Bitmap(background.Width, background.Height, PixelFormat.Format32bppArgb);
    }

    internal Task<Rectangle> SelectAsync()
    {
        if (shown) throw new InvalidOperationException("Capture selection has already started.");
        shown = true;
        SetWindowPos(Handle, -1, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x40);
        SetForegroundWindow(Handle);
        return completion.Task;
    }

    protected override nint? ProcessMessage(uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case 0x0F: Paint(); return 0;
            case 0x14: return 1; // The full buffered frame covers the background.
            case 0x20: SetCursor(LoadCursor(0, 32515)); return 1;
            case 0x201:
                selection.Begin(MousePoint(lParam));
                SetCapture(Handle);
                InvalidateRect(Handle, 0, false);
                return 0;
            case 0x200:
                Move(MousePoint(lParam));
                return 0;
            case 0x202:
                if (selection.IsDragging)
                {
                    Move(MousePoint(lParam));
                    Complete(selection.Finish());
                }
                return 0;
            case 0x204:
            case 0x10:
                Complete(Rectangle.Empty);
                return 0;
            case 0x100:
            case 0x104:
                if (wParam == 0x1B) Complete(Rectangle.Empty);
                else if (selection.IsDragging && wParam is 0x20 or 0x12)
                {
                    // Re-anchor to the constrained endpoint when starting a pan or centre drag.
                    var end = selection.End;
                    SetCursorPos(bounds.X + end.X, bounds.Y + end.Y);
                }
                return 0;
            case 0x101:
            case 0x105: return 0;
            case 0x215:
                if (!completed && selection.IsDragging) Complete(Rectangle.Empty);
                return 0;
            case 0x06:
                if (shown && (wParam & 0xFFFF) == 0) Complete(Rectangle.Empty);
                return 0;
        }
        return null;
    }

    private void Move(Point point)
    {
        if (!selection.IsDragging || completed) return;
        selection.Move(point, Down(0x20), Down(0x12), Down(0x10), Down(0x11));
        InvalidateRect(Handle, 0, false);
    }
    private static bool Down(int key) => (GetKeyState(key) & 0x8000) != 0;
    private static Point MousePoint(nint parameter) => new(unchecked((short)(long)parameter), unchecked((short)((long)parameter >> 16)));

    private void Complete(Rectangle result)
    {
        if (completed) return;
        completed = true;
        selection.Cancel();
        ReleaseCapture();
        ShowWindow(Handle, 0);
        completion.TrySetResult(result.Width > 0 && result.Height > 0 ? result : Rectangle.Empty);
    }

    private void Paint()
    {
        nint dc = BeginPaint(Handle, out var paint);
        try
        {
            using (var graphics = Graphics.FromImage(buffer))
            {
                graphics.DrawImageUnscaled(background, Point.Empty);
                var area = selection.Area;
                using var dim = new SolidBrush(Color.FromArgb(120, Color.Black));
                using var region = new Region(new Rectangle(Point.Empty, background.Size));
                if (area.Width > 0 && area.Height > 0) region.Exclude(area);
                graphics.FillRegion(dim, region);
                if (area.Width > 0 && area.Height > 0)
                {
                    using var border = new Pen(Color.DeepSkyBlue, 2f);
                    graphics.DrawRectangle(border, area.Left - 1, area.Top - 1, area.Width + 2, area.Height + 2);
                    var font = SystemFonts.CaptionFont ?? SystemFonts.DefaultFont;
                    string caption = $"{area.Width} x {area.Height}";
                    var text = graphics.MeasureString(caption, font);
                    float x = Math.Clamp(area.Right - text.Width, 0, Math.Max(0, background.Width - text.Width));
                    float y = area.Bottom + 4;
                    if (y + text.Height > background.Height) y = Math.Max(0, area.Top - text.Height - 4);
                    graphics.DrawString(caption, font, Brushes.White, x, y);
                }
            }
            using var target = Graphics.FromHdc(dc);
            target.DrawImageUnscaled(buffer, Point.Empty);
        }
        finally { EndPaint(Handle, ref paint); }
    }

    public override void Dispose()
    {
        if (!completed && Handle != 0) Complete(Rectangle.Empty);
        base.Dispose();
        buffer.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintData
    {
        public nint Hdc;
        public int Erase, Left, Top, Right, Bottom, Restore, Update;
        public uint Reserved0, Reserved1, Reserved2, Reserved3, Reserved4, Reserved5, Reserved6, Reserved7;
    }
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint window, out PaintData paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint window, ref PaintData paint);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, nint rectangle, bool erase);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern nint SetCapture(nint window);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadCursor(nint instance, nint cursorName);
}
