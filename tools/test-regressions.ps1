param(
    [string]$LogPath = (Join-Path $PSScriptRoot '..\local\regressions.log')
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$LogPath = [System.IO.Path]::GetFullPath($LogPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($LogPath)) | Out-Null
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$project = Join-Path $repo 'tests\Screenzap.ViewportTests\Screenzap.ViewportTests.csproj'

# Document tests create no editor windows. Isolate platform interop fixtures and their
# child processes on a separate desktop so they cannot interfere with the user.
# No SwitchDesktop call is made; the interactive desktop remains active throughout.
Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class RegressionDesktop {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo {
        public int Size;
        public string Reserved, Desktop, Title;
        public int X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll")]
    static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity,
        IntPtr threadSecurity, bool inheritHandles, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    public static int Run(string command, string directory) {
        string name = "ScreenzapTests_" + Guid.NewGuid().ToString("N");
        IntPtr desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x01FF, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var startup = new StartupInfo { Size = Marshal.SizeOf(typeof(StartupInfo)), Desktop = "winsta0\\" + name };
            ProcessInfo process;
            if (!CreateProcess(Environment.GetEnvironmentVariable("ComSpec"), new StringBuilder(command),
                IntPtr.Zero, IntPtr.Zero, false, 0x08000000, IntPtr.Zero, directory, ref startup, out process))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                Console.WriteLine("Running regressions on isolated desktop: " + name);
                if (WaitForSingleObject(process.Process, 0xFFFFFFFF) != 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                uint code;
                if (!GetExitCodeProcess(process.Process, out code))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return (int)code;
            } finally {
                CloseHandle(process.Thread);
                CloseHandle(process.Process);
            }
        } finally { CloseDesktop(desktop); }
    }
}
'@
$command = 'cmd.exe /d /s /c ""{0}" test "{1}" -nr:false -m:1 -p:UseSharedCompilation=false > "{2}" 2>&1"' -f $dotnet, $project, $LogPath
$result = [RegressionDesktop]::Run($command, $repo)
Get-Content $LogPath | Select-Object -Last 20
exit $result
