param([string]$Executable = (Join-Path $PSScriptRoot '..\screenzap\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Screenzap.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Runtime.InteropServices;
public class NativeInput {
 public static bool GoldBackground(int x,int y) {
  using(var bitmap=new System.Drawing.Bitmap(1,1)) {
   using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(x,y,0,0,bitmap.Size);
   var color=bitmap.GetPixel(0,0);
   return color.R>color.G+6 && color.G>color.B+10;
  }
 }
 public static int BluePixels(int x,int y,int width,int height) {
  using(var bitmap=new System.Drawing.Bitmap(width,height)) {
   using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(x,y,0,0,bitmap.Size);
   int blue=0;
   for(int row=0;row<height;row++) for(int column=0;column<width;column++) {
    var color=bitmap.GetPixel(column,row);
    if(color.B>200 && color.B>color.R+60 && color.B>color.G+15) blue++;
   }
   return blue;
  }
 }
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 public static void MovePointer(int x,int y) {
  uint nx=(uint)((long)(x-GetSystemMetrics(76))*65535/(GetSystemMetrics(78)-1));
  uint ny=(uint)((long)(y-GetSystemMetrics(77))*65535/(GetSystemMetrics(79)-1));
  mouse_event(0xC001,nx,ny,0,UIntPtr.Zero);
 }
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int width,int height,bool repaint);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
}
'@
[NativeInput]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$p=Start-Process $Executable '--winui-smoke' -PassThru
try {
 function WaitFor($test,$label) {
  $deadline=[DateTime]::UtcNow.AddSeconds(15)
  while([DateTime]::UtcNow -lt $deadline) {
   if($p.HasExited) {throw "Native editor exited during $label (exit $($p.ExitCode))"}
   if(&$test) { return }; Start-Sleep -Milliseconds 100
  }
  throw "Timed out: $label"
 }
 WaitFor { $p.Refresh(); $p.MainWindowHandle -ne 0 } 'native window'
 $script:root=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 function All { $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) }
 function Find($prefix) { All | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name.StartsWith($prefix)} | Select-Object -First 1 }
 function Click($prefix) {
  $button=Find $prefix
  if(!$button) { throw "Button absent: $prefix" }
  if(!$button.Current.IsEnabled) { throw "Button disabled: $prefix" }
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 200
 }
 function Drag($x1,$y1,$x2,$y2,$middle=$false) {
  if([NativeInput]::GetForegroundWindow() -ne $p.MainWindowHandle) {throw "Native window did not receive foreground focus"}
  [NativeInput]::MovePointer([int]$x1,[int]$y1)|Out-Null
  [NativeInput]::mouse_event($(if($middle){32}else{2}),0,0,0,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 100
  [NativeInput]::MovePointer([int]($x1+($x2-$x1)/20),[int]($y1+($y2-$y1)/20))|Out-Null
  Start-Sleep -Milliseconds 350
  for($step=1;$step -le 8;$step++) {
   [NativeInput]::MovePointer([int]($x1+($x2-$x1)*$step/8),[int]($y1+($y2-$y1)*$step/8))|Out-Null
   Start-Sleep -Milliseconds 40
  }
  Start-Sleep -Milliseconds 350
  [NativeInput]::mouse_event($(if($middle){64}else{4}),0,0,0,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 200
 }
 WaitFor { $null -ne (Find 'Draw arrows') } 'native controls loaded'
 [NativeInput]::keybd_event(18,0,0,[UIntPtr]::Zero)
 [NativeInput]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
 [NativeInput]::keybd_event(18,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 500
 $bounds=$script:root.Current.BoundingRectangle
 "Window bounds: $bounds; foreground: $([NativeInput]::GetForegroundWindow()); expected: $($p.MainWindowHandle)"
 Click 'Draw arrows'
 WaitFor { (All|Where-Object {$_.Current.Name -eq 'Arrow'}).Count -gt 0 } 'arrow inspector'
 Click 'Actual size'
 foreach($field in @(@('Width',16),@('Arrowhead scale',5))) {
  $input=All|Where-Object {$_.Current.Name -eq $field[0] -and $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::Text}|Select-Object -First 1
  $input.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($field[1])
 }
 Drag ($bounds.Left+270) ($bounds.Top+330) ($bounds.Left+670) ($bounds.Top+330)
 WaitFor { (Find 'Undo').Current.IsEnabled -and (Find 'Accept edits').Current.IsEnabled } 'drawn annotation'
 $endX=[int]($bounds.Left+670); $endY=[int]($bounds.Top+330)
 [NativeInput]::MovePointer([int]($bounds.Left+450),$endY)
 Start-Sleep -Milliseconds 200
 $idle=[NativeInput]::BluePixels($endX-9,$endY-9,19,19)
 [NativeInput]::MovePointer($endX,$endY+6)
 Start-Sleep -Milliseconds 200
 $hover=[NativeInput]::BluePixels($endX-9,$endY-9,19,19)
 if($hover -lt $idle+30) {throw "Arrow endpoint did not visibly react while the arrow tool was active: $idle -> $hover blue pixels"}
 Drag ($endX-80) ($endY+60) ($endX-40) ($endY+80)
 $endX+=40; $endY+=20
 [NativeInput]::MovePointer($endX,$endY+6)
 Start-Sleep -Milliseconds 200
 if([NativeInput]::BluePixels($endX-9,$endY-9,19,19) -lt 50) {throw 'Dragging the arrowhead wing did not move the whole arrow'}
 Drag $endX ($endY+6) ($endX+40) ($endY+36)
 $endX+=40; $endY+=30
 if([NativeInput]::BluePixels($endX-9,$endY-9,19,19) -lt 50) {throw 'Endpoint grip did not move its vertex independently'}
 Drag ($bounds.Left+330) ($bounds.Top+210) ($bounds.Left+330) ($bounds.Top+210)
 WaitFor {(All|Where-Object {$_.Current.Name -eq 'Selection' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text})} 'empty click exits arrow tool'
 Click 'Draw arrows'
 Drag ($bounds.Left+530) ($bounds.Top+365) ($bounds.Left+530) ($bounds.Top+365)
 WaitFor {(All|Where-Object {$_.Current.Name -eq 'Arrowhead scale'})} 'arrow click selects arrow and keeps its tool'
 $arrowBounds=(Find 'Draw arrows').Current.BoundingRectangle
 if(![NativeInput]::GoldBackground([int]($arrowBounds.Left+4),[int]($arrowBounds.Top+$arrowBounds.Height/2))) {throw 'Clicking an arrow deactivated its tool button'}


 Click 'Actual size'
 Click 'Zoom in'
 Drag ($bounds.Left+550) ($bounds.Top+440) ($bounds.Left+500) ($bounds.Top+410) $true
 $zoom=(All|Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -match '^\d+(\.\d+)?%$'}|Select-Object -First 1).Current.Name
 Click 'Accept edits'
 WaitFor { !(Find 'Accept edits').Current.IsEnabled -and (Find 'Undo').Current.IsEnabled } 'commit and undo preserved'
 $afterZoom=(All|Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $_.Current.Name -match '^\d+(\.\d+)?%$'}|Select-Object -First 1).Current.Name
 if($afterZoom -ne $zoom) {throw "Commit changed zoom: $zoom -> $afterZoom"}
 Click 'Adjust four corners'
 Drag ($bounds.Left+280) ($bounds.Top+240) ($bounds.Left+680) ($bounds.Top+510)
 WaitFor { (Find 'Apply').Current.IsEnabled } 'perspective apply enables after drawing rectangle'
 Click 'Cancel'
 Click 'Resize the image'
 WaitFor { (All|Where-Object {$_.Current.Name -eq 'Width (px)'}).Count -gt 0 } 'native resize dialog'
 Click 'Cancel'
 Click 'Adjust color'
 WaitFor { (All|Where-Object {$_.Current.Name -eq 'Gamma'}).Count -gt 0 } 'native color dialog'
 Click 'Cancel'
 Click 'Add text'
 Drag ($bounds.Left+360) ($bounds.Top+490) ($bounds.Left+360) ($bounds.Top+490)
 foreach($key in @(72,73)) { [NativeInput]::keybd_event($key,0,0,[UIntPtr]::Zero); [NativeInput]::keybd_event($key,0,2,[UIntPtr]::Zero) }
 WaitFor { (Find 'Accept edits').Current.IsEnabled } 'live text enables commit'
 [NativeInput]::keybd_event(17,0,0,[UIntPtr]::Zero)
 [NativeInput]::keybd_event(13,0,0,[UIntPtr]::Zero)
 [NativeInput]::keybd_event(13,0,2,[UIntPtr]::Zero)
 [NativeInput]::keybd_event(17,0,2,[UIntPtr]::Zero)
 WaitFor { !(Find 'Accept edits').Current.IsEnabled } 'Ctrl+Enter commits live text'
 Click 'Drag emoji'
 WaitFor { (All|Where-Object {$_.Current.Name -eq 'Add emoji'}).Count -gt 0 } 'native emoji flyout'
 [NativeInput]::keybd_event(27,0,0,[UIntPtr]::Zero)
 [NativeInput]::keybd_event(27,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 200
 Click 'Duplicate this item'
 WaitFor { (All|Where-Object {$_.Current.AutomationId.StartsWith('HistoryItem-')}).Count -eq 2 } 'history duplicate'
 Click 'Remove this item'
 WaitFor { (All|Where-Object {$_.Current.AutomationId.StartsWith('HistoryItem-')}).Count -eq 1 } 'history delete'
 $history=All|Where-Object {$_.Current.AutomationId.StartsWith('HistoryItem-')}|Select-Object -First 1
 $historyBounds=$history.Current.BoundingRectangle
 "History drag bounds: $historyBounds; target: $($bounds.Left+580),$($bounds.Top+350)"
 Drag ($historyBounds.Left+45) ($historyBounds.Top+35) ($bounds.Left+580) ($bounds.Top+350)
 WaitFor { (All|Where-Object {$_.Current.Name -eq 'Layers'}).Count -gt 0 } 'history image drops as a layer'
 Click 'Merge layer'
 [NativeInput]::MoveWindow($p.MainWindowHandle,[int]$bounds.Left,[int]$bounds.Top,840,640,$true)|Out-Null
 Start-Sleep -Milliseconds 500
 $narrowBounds=$script:root.Current.BoundingRectangle
 foreach($prefix in @('Resize the image','Accept edits','Duplicate this item','Show or hide history')) {
  $button=Find $prefix
  $r=$button.Current.BoundingRectangle
  if($r.Width -le 0 -or $r.Right -gt $narrowBounds.Right -or $r.Bottom -gt $narrowBounds.Bottom) {throw "Clipped control at narrow size: $prefix"}
 }
 [NativeInput]::MoveWindow($p.MainWindowHandle,[int]$bounds.Left,[int]$bounds.Top,1280,900,$true)|Out-Null
 'PASS: native arrow body/head dragging, endpoint hover/drag, arrow click rules, tool selection, pointer drawing, pan/zoom, commit, undo, perspective, resize, typing, keyboard commit, color, emoji, history drag/drop, layers and narrow layout'
} finally { if(!$p.HasExited) { Stop-Process -Id $p.Id } }
