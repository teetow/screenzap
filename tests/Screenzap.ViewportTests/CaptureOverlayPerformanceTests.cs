using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using screenzap.Native;
using Xunit;
using Xunit.Abstractions;

namespace Screenzap.ViewportTests;

public sealed class CapturePerformanceTheoryAttribute : TheoryAttribute
{
    public CapturePerformanceTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("SCREENZAP_CAPTURE_PERF") != "1")
            Skip = "Use tools/measure-capture-performance.ps1 to benchmark on an isolated desktop.";
    }
}

public class CaptureOverlayPerformanceTests
{
    private readonly ITestOutputHelper output;
    public CaptureOverlayPerformanceTests(ITestOutputHelper output) => this.output = output;

    [CapturePerformanceTheory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void DragPaint(int width, int height)
    {
        StaTest.Run(() =>
        {
            var desktop = new StringBuilder(256);
            Assert.True(GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, desktop, desktop.Capacity * 2, out _));
            Assert.StartsWith("ScreenzapTests_", desktop.ToString());

            using var image = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.LightSteelBlue);
                graphics.FillEllipse(Brushes.DarkSlateBlue, 40, 40, width - 80, height - 80);
            }
            var startup = Stopwatch.StartNew();
            using var overlay = new CaptureOverlay(new Rectangle(0, 0, width, height), image);
            ShowWindow(overlay.Handle, 4); // Isolated desktop; no foreground activation or global input.
            UpdateWindow(overlay.Handle);
            startup.Stop();
            SendMessage(overlay.Handle, 0x201, 1, Position(50, 50));
            var samples = new double[80];
            for (int frame = -10; frame < samples.Length; frame++)
            {
                var clock = Stopwatch.StartNew();
                SendMessage(overlay.Handle, 0x200, 1, Position(1200 + frame * 2, 800 + frame));
                Assert.True(GetUpdateRect(overlay.Handle, out _, false), "The drag must actually invalidate and paint a frame.");
                UpdateWindow(overlay.Handle);
                GdiFlush();
                clock.Stop();
                if (frame >= 0) samples[frame] = clock.Elapsed.TotalMilliseconds;
            }
            Array.Sort(samples);
            output.WriteLine($"CAPTURE {width}x{height}: startup={startup.Elapsed.TotalMilliseconds:F2}ms drag median={samples[40]:F2}ms p95={samples[75]:F2}ms max={samples.Max():F2}ms");
            SendMessage(overlay.Handle, 0x204, 0, 0);
        });
    }

    private static nint Position(int x, int y) => (nint)((y << 16) | (x & 0xFFFF));
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetUpdateRect(nint window, out NativeRect rectangle, bool erase);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder information, int length, out int needed);
}
