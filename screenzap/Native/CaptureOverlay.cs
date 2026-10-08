using System.Runtime.InteropServices;
using screenzap.Components;

namespace screenzap.Native;

internal sealed class CaptureOverlay : NativeWindow
{
    private readonly CaptureOverlayRenderer renderer;
    private readonly Rectangle bounds;
    private readonly CaptureSelection selection;
    private readonly TaskCompletionSource<Rectangle> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool shown;
    private bool completed;

    internal CaptureOverlay(Rectangle bounds, Bitmap background)
        : base("Screenzap Capture", bounds, extendedStyle: 0x08 | 0x80)
    {
        this.bounds = bounds;
        selection = new CaptureSelection(background.Size);
        try { renderer = new CaptureOverlayRenderer(background); }
        catch { base.Dispose(); throw; }
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
                var previous = selection.Area;
                selection.Begin(MousePoint(lParam));
                SetCapture(Handle);
                InvalidateSelection(previous);
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
        var previous = selection.Area;
        selection.Move(point, Down(0x20), Down(0x12), Down(0x10), Down(0x11));
        InvalidateSelection(previous);
    }
    private void InvalidateSelection(Rectangle previous)
    {
        if (previous == selection.Area) return;
        nint damage = renderer.CreateDamageRegion(previous, selection.Area);
        try { InvalidateRgn(Handle, damage, false); }
        finally { CaptureOverlayRenderer.DeleteObject(damage); }
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
        nint dirty = CaptureOverlayRenderer.CreateRectRgn(0, 0, 0, 0);
        GetUpdateRgn(Handle, dirty, false);
        nint dc = BeginPaint(Handle, out var paint);
        try
        {
            renderer.Paint(dc, Rectangle.FromLTRB(paint.Left, paint.Top, paint.Right, paint.Bottom), dirty, selection.Area);
        }
        finally
        {
            EndPaint(Handle, ref paint);
            CaptureOverlayRenderer.DeleteObject(dirty);
        }
    }

    public override void Dispose()
    {
        if (!completed && Handle != 0) Complete(Rectangle.Empty);
        base.Dispose();
        renderer.Dispose();
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
    [DllImport("user32.dll")] private static extern bool InvalidateRgn(nint window, nint region, bool erase);
    [DllImport("user32.dll")] private static extern int GetUpdateRgn(nint window, nint region, bool erase);
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
