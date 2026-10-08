using System.ComponentModel;
using System.Runtime.InteropServices;
using screenzap.lib;

namespace screenzap.Native;

// Hidden service windows and the capture surface share the WinUI thread's message loop.
internal abstract class NativeWindow : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);
    private readonly WindowProcedure procedure;
    private readonly string className = "Screenzap.Native." + Guid.NewGuid().ToString("N");
    private readonly nint instance = GetModuleHandle(null);
    private bool ready;
    public nint Handle { get; private set; }

    protected NativeWindow(string title, Rectangle bounds, uint style = 0x80000000, uint extendedStyle = 0x80)
    {
        procedure = Dispatch;
        var registration = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = instance,
            Procedure = Marshal.GetFunctionPointerForDelegate(procedure), ClassName = className
        };
        if (RegisterClassEx(ref registration) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        Handle = CreateWindowEx(extendedStyle, className, title, style, bounds.X, bounds.Y,
            bounds.Width, bounds.Height, 0, 0, instance, 0);
        if (Handle == 0)
        {
            int error = Marshal.GetLastWin32Error();
            UnregisterClass(className, instance);
            throw new Win32Exception(error);
        }
        ready = true;
    }

    private nint Dispatch(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (ready && ProcessMessage(message, wParam, lParam) is nint result) return result;
        }
        catch (Exception ex) { Logger.Log($"Native window message {message:X}: {ex}"); }
        return DefWindowProc(window, message, wParam, lParam);
    }

    protected abstract nint? ProcessMessage(uint message, nuint wParam, nint lParam);

    public virtual void Dispose()
    {
        if (Handle == 0) return;
        ready = false;
        DestroyWindow(Handle);
        Handle = 0;
        UnregisterClass(className, instance);
        GC.KeepAlive(procedure);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string className, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
