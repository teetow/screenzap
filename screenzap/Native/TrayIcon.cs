using System.ComponentModel;
using System.Runtime.InteropServices;

namespace screenzap.Native;

internal enum TrayNotificationKind : uint { Info = 1, Warning = 2, Error = 3 }

internal sealed class TrayIcon : NativeWindow
{
    private const uint CallbackMessage = 0x8001;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Icon icon;
    private string tooltip = "Screenzap";
    private bool registered;
    private Action? notificationClicked;
    public Action? OpenRequested { get; set; }
    public Action? SaveRequested { get; set; }
    public Action? QuitRequested { get; set; }
    public string BuildDescription { get; set; } = "";

    public TrayIcon() : base("Screenzap Background", Rectangle.Empty)
    {
        try
        {
            icon = new Icon(Path.Combine(AppContext.BaseDirectory, "res", "screenzap-icon.ico"), 32, 32);
            AddIcon();
        }
        catch { Dispose(); throw; }
    }

    public string Tooltip
    {
        set
        {
            tooltip = value;
            var data = Data(0x04 | 0x80);
            Shell_NotifyIcon(1, ref data);
        }
    }

    public void ShowNotification(string title, string text, TrayNotificationKind kind, Action? clicked = null)
    {
        notificationClicked = clicked;
        var data = Data(0x10);
        data.Info = Truncate(text, 255);
        data.InfoTitle = Truncate(title, 63);
        data.InfoFlags = (uint)kind | 0x80;
        Shell_NotifyIcon(1, ref data);
    }

    private void AddIcon()
    {
        var data = Data(0x01 | 0x02 | 0x04 | 0x80);
        if (!Shell_NotifyIcon(0, ref data)) throw new Win32Exception(Marshal.GetLastWin32Error());
        registered = true;
        data.TimeoutOrVersion = 4;
        Shell_NotifyIcon(4, ref data);
    }

    protected override nint? ProcessMessage(uint message, nuint wParam, nint lParam)
    {
        if (message == taskbarCreated) { AddIcon(); return 0; }
        if (message == 0x10) { QuitRequested?.Invoke(); return 0; }
        if (message != CallbackMessage) return null;
        switch ((uint)(long)lParam & 0xFFFF)
        {
            case 0x203: // Double click.
            case 0x401: OpenRequested?.Invoke(); break; // Keyboard activation.
            case 0x7B: ShowMenu(); break;
            case 0x405: // NIN_BALLOONUSERCLICK.
                var action = notificationClicked;
                notificationClicked = null;
                action?.Invoke();
                break;
            case 0x403: // NIN_BALLOONHIDE.
            case 0x404: notificationClicked = null; break; // NIN_BALLOONTIMEOUT.
        }
        return 0;
    }

    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        if (menu == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        uint command;
        try
        {
            AppendMenu(menu, 0, 1, "Edit &Clipboard Image");
            AppendMenu(menu, 0, 2, "&Save Clipboard Image");
            AppendMenu(menu, 0x800, 0, null);
            if (BuildDescription.Length > 0) AppendMenu(menu, 0x01, 0, BuildDescription);
            AppendMenu(menu, 0, 3, "&Quit");
            GetCursorPos(out var point);
            SetForegroundWindow(Handle);
            command = TrackPopupMenu(menu, 0x100 | 0x02, point.X, point.Y, 0, Handle, 0);
            PostMessage(Handle, 0, 0, 0);
        }
        finally { DestroyMenu(menu); }
        switch (command)
        {
            case 1: OpenRequested?.Invoke(); break;
            case 2: SaveRequested?.Invoke(); break;
            case 3: QuitRequested?.Invoke(); break;
        }
    }

    private NotifyIconData Data(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = Handle, Id = 1,
        Flags = flags, Callback = CallbackMessage, Icon = icon?.Handle ?? 0,
        Tip = Truncate(tooltip, 127), Info = "", InfoTitle = ""
    };
    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    public override void Dispose()
    {
        if (registered)
        {
            var data = Data(0);
            Shell_NotifyIcon(2, ref data);
            registered = false;
        }
        notificationClicked = null;
        base.Dispose();
        icon?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Shell_NotifyIcon(uint operation, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint rectangle);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
