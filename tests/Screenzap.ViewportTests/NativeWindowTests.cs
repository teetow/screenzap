using System;
using System.Drawing;
using System.Runtime.InteropServices;
using screenzap.Native;
using Xunit;

namespace Screenzap.ViewportTests;

public class NativeWindowTests
{
    [Fact]
    public void NativeMessageWindow_IsHidden_ReceivesMessages_AndDestroysItsHandle()
    {
        StaTest.Run(() =>
        {
            for (int index = 0; index < 3; index++)
            {
                var window = new ProbeWindow();
                nint handle = window.Handle;
                Assert.True(IsWindow(handle));
                Assert.False(IsWindowVisible(handle));
                Assert.Equal((nint)123, SendMessage(handle, 0x8009, 0, 0));
                Assert.Equal(1, window.Received);
                window.Dispose();
                window.Dispose();
                Assert.Equal(0, window.Handle);
                Assert.False(IsWindow(handle));
            }
        });
    }

    [Fact]
    public void CaptureOverlay_CanBePreparedAndDisposed_WithoutShowingAWindow()
    {
        StaTest.Run(() =>
        {
            using var image = new Bitmap(100, 80);
            var capture = new CaptureOverlay(new Rectangle(-100, -80, 100, 80), image);
            nint handle = capture.Handle;
            Assert.True(IsWindow(handle));
            Assert.False(IsWindowVisible(handle));
            capture.Dispose();
            Assert.False(IsWindow(handle));
            // Disposing the surface must leave ownership of the frozen screenshot with its caller.
            Assert.Equal(100, image.Width);
        });
    }

    private sealed class ProbeWindow : screenzap.Native.NativeWindow
    {
        internal int Received;
        internal ProbeWindow() : base("Screenzap Regression Service", Rectangle.Empty) { }
        protected override nint? ProcessMessage(uint message, nuint wParam, nint lParam)
        {
            if (message != 0x8009) return null;
            Received++;
            return 123;
        }
    }
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
}
