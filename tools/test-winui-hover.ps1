param([string]$Executable = (Join-Path $PSScriptRoot '..\screenzap\bin\Release\net8.0-windows10.0.19041.0\win-x64\Screenzap.exe'), [switch]$CaptureOnly)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
public class HoverCapture {
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 public static void Move(int x,int y) {
  uint nx=(uint)((long)(x-GetSystemMetrics(76))*65535/(GetSystemMetrics(78)-1));
  uint ny=(uint)((long)(y-GetSystemMetrics(77))*65535/(GetSystemMetrics(79)-1));
  mouse_event(0xC001,nx,ny,0,UIntPtr.Zero);
 }
 static Bitmap Capture(Rectangle region) {
  var bitmap=new Bitmap(region.Width,region.Height);
  using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(region.Location,Point.Empty,region.Size);
  return bitmap;
 }
 public static int Transition(Rectangle region,int fromX,int fromY,int toX,int toY,string directory) {
  Directory.CreateDirectory(directory);
  Move(fromX,fromY);Thread.Sleep(150);
  using(var start=Capture(region)) {
   Move(toX,toY);
   var frames=new Bitmap[12];var times=new long[frames.Length];var timer=Stopwatch.StartNew();
   try {
    for(int frame=0;frame<frames.Length;frame++) {
     frames[frame]=Capture(region);times[frame]=timer.ElapsedMilliseconds;
     Thread.Sleep(8);
    }
    using(var finish=Capture(region)) {
     int maximum=0;var metrics=new string[frames.Length+1];metrics[0]="frame,milliseconds,overshoot";
     using(var sheet=new Bitmap(region.Width*4,region.Height*3)) {
      using(var graphics=Graphics.FromImage(sheet)) {
       for(int frame=0;frame<frames.Length;frame++) {
        int error=0;
        for(int y=2;y<region.Height-2;y++) for(int x=2;x<region.Width-2;x++) {
         var a=start.GetPixel(x,y);var b=finish.GetPixel(x,y);var p=frames[frame].GetPixel(x,y);
         error=Math.Max(error,Math.Max(Outside(p.R,a.R,b.R),Math.Max(Outside(p.G,a.G,b.G),Outside(p.B,a.B,b.B))));
        }
        maximum=Math.Max(maximum,error);
        metrics[frame+1]=frame+","+times[frame]+","+error;
        graphics.DrawImageUnscaled(frames[frame],(frame%4)*region.Width,(frame/4)*region.Height);
       }
      }
      sheet.Save(Path.Combine(directory,"frames.png"),System.Drawing.Imaging.ImageFormat.Png);
     }
     File.WriteAllLines(Path.Combine(directory,"frames.csv"),metrics);
     start.Save(Path.Combine(directory,"start.png"),System.Drawing.Imaging.ImageFormat.Png);
     finish.Save(Path.Combine(directory,"finish.png"),System.Drawing.Imaging.ImageFormat.Png);
     return maximum;
    }
   } finally {foreach(var frame in frames) if(frame!=null) frame.Dispose();}
  }
 }
 static int Outside(int value,int a,int b) {return Math.Max(0,Math.Max(Math.Min(a,b)-value,value-Math.Max(a,b)));}
}
'@
[HoverCapture]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$p=Start-Process $Executable '--winui-smoke' -PassThru
try {
 $deadline=[DateTime]::UtcNow.AddSeconds(20)
 do {Start-Sleep -Milliseconds 100;$p.Refresh()} while($p.MainWindowHandle.ToInt64() -eq 0 -and [DateTime]::UtcNow -lt $deadline)
 $root=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 function All {$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)}
 do {Start-Sleep -Milliseconds 100;$buttons=@(All|Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button})} while($buttons.Count -lt 20 -and [DateTime]::UtcNow -lt $deadline)
 [HoverCapture]::keybd_event(18,0,0,[UIntPtr]::Zero)
 [HoverCapture]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
 [HoverCapture]::keybd_event(18,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 500
 if([HoverCapture]::GetForegroundWindow() -ne $p.MainWindowHandle) {throw 'Editor must be foreground for transition capture'}
 $bounds=$root.Current.BoundingRectangle
 $outsideX=[int]($bounds.Left+600);$outsideY=[int]($bounds.Top+550)
 $failed=$false
 foreach($name in @('Move / select tool','Draw arrows','History image','Copy to Clipboard')) {
  $button=$buttons|Where-Object {$_.Current.Name.StartsWith($name)}|Select-Object -First 1
  $r=$button.Current.BoundingRectangle
  $region=New-Object System.Drawing.Rectangle ([int]$r.Left),([int]$r.Top),([int]$r.Width),([int]$r.Height)
  $insideX=[int]($r.Left+$r.Width/2);$insideY=[int]($r.Top+$r.Height/2)
  foreach($direction in @('enter','leave')) {
   # Reset tooltip timing before capturing the first frames of each transition.
   [HoverCapture]::Move($outsideX,$outsideY)
   Start-Sleep -Milliseconds 350
   "Capturing $name $direction"
   $directory=Join-Path $PSScriptRoot ("..\local\hover-transitions\"+($name -replace '[^a-zA-Z0-9]','_')+'-'+$direction)
   $transitionDifference=if($direction -eq 'enter') {[HoverCapture]::Transition($region,$outsideX,$outsideY,$insideX,$insideY,$directory)} else {[HoverCapture]::Transition($region,$insideX,$insideY,$outsideX,$outsideY,$directory)}
   "Maximum transition overshoot: $transitionDifference"
   if($transitionDifference -gt 16) {$failed=$true; "FLASH: $name $direction; overshoot=$transitionDifference"}
  }
 }
 if($failed -and !$CaptureOnly) {throw 'Mouse-enter/leave frames flashed outside their normal/hover color range'}
 if(!$failed) {'PASS: mouse-enter and mouse-leave transition frames'}
} finally {if(!$p.HasExited){Stop-Process -Id $p.Id}}
