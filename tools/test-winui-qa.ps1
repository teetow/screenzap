param([string]$Executable = (Join-Path $PSScriptRoot '..\screenzap\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Screenzap.exe'), [switch]$LiveDeJpeg)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Runtime.InteropServices;
public class NativeInput {
 public static void SaveRegion(int x,int y,int width,int height,string path) {
  using(var bitmap=new System.Drawing.Bitmap(width,height)) {
   using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(x,y,0,0,bitmap.Size);
   bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
  }
 }
 public static int[] CaptureRegion(int x,int y,int width,int height) {
  using(var bitmap=new System.Drawing.Bitmap(width,height)) {
   using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(x,y,0,0,bitmap.Size);
   var pixels=new int[width*height];
   for(int row=0;row<height;row++) for(int column=0;column<width;column++)
    pixels[row*width+column]=bitmap.GetPixel(column,row).ToArgb();
   return pixels;
  }
 }
 public static int MaximumDifference(int[] before,int[] after) {
  int maximum=0;
  for(int index=0;index<before.Length;index++) for(int shift=0;shift<24;shift+=8)
   maximum=Math.Max(maximum,Math.Abs(((before[index]>>shift)&255)-((after[index]>>shift)&255)));
  return maximum;
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

New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot '..\local') -Force|Out-Null
$p=Start-Process $Executable '--winui-smoke --qa-history' -PassThru
try {
 function WaitFor($test,$label,$seconds=20) {
  $deadline=[DateTime]::UtcNow.AddSeconds($seconds)
  while([DateTime]::UtcNow -lt $deadline) {if($p.HasExited) {throw "Editor exited: $label"}; if(&$test) {return}; Start-Sleep -Milliseconds 100}
  throw "Timed out: $label"
 }
 WaitFor {$p.Refresh(); $p.MainWindowHandle.ToInt64() -gt 0} 'window'
 $root=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 function All {$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)}
 function Find($name) {All|Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name.StartsWith($name)}|Select-Object -First 1}
 function Click($name) {(Find $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 250}
 function Wheel($element,$delta) {
  [NativeInput]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
  WaitFor {[NativeInput]::GetForegroundWindow() -eq $p.MainWindowHandle} 'wheel foreground focus'
  $r=$element.Current.BoundingRectangle
  [NativeInput]::MovePointer([int]($r.Left+$r.Width/2),[int]($r.Top+$r.Height/2))
  Start-Sleep -Milliseconds 150
  [NativeInput]::mouse_event(2048,0,0,[uint32]$delta,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 200
 }
 WaitFor {(All|Where-Object {$_.Current.AutomationId.StartsWith('HistoryItem-')}).Count -eq 32} '32 history tiles'
 [NativeInput]::keybd_event(18,0,0,[UIntPtr]::Zero)
 [NativeInput]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
 [NativeInput]::keybd_event(18,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 500
 $scroll=All|Where-Object {$_.Current.AutomationId -eq 'HistoryScroll'}|Select-Object -First 1
 $scrollPattern=$scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
 $scrollPattern.SetScrollPercent(50,-1)
 Start-Sleep -Milliseconds 1000
 $offset=$scrollPattern.Current.HorizontalScrollPercent
 $allTiles=@(All|Where-Object {$_.Current.AutomationId.StartsWith('HistoryItem-')})
 $identities=@{}; foreach($tile in $allTiles) {$identities[$tile.Current.AutomationId]=$tile.GetRuntimeId() -join ','}
 $viewport=$scroll.Current.BoundingRectangle
 $tile=$allTiles|Where-Object {$r=$_.Current.BoundingRectangle; $r.Width -ge 100 -and $r.Height -gt 0 -and $r.Left -ge $viewport.Left -and $r.Right -le $viewport.Right}|Select-Object -First 1
 if(!$tile) {throw 'No visible history tile'}
 $tile.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Start-Sleep -Milliseconds 600
 foreach($current in @(All|Where-Object {$_.Current.AutomationId.StartsWith('HistoryItem-')})) {
  if(($current.GetRuntimeId() -join ',') -ne $identities[$current.Current.AutomationId]) {throw 'History tile was rebuilt after activation'}
 }
 if([Math]::Abs($scrollPattern.Current.HorizontalScrollPercent-$offset) -gt .01) {throw "Activation changed the history scroll offset: $offset -> $($scrollPattern.Current.HorizontalScrollPercent)"}
 for($index=0;$index -lt 12;$index++) {
  $hover=if($index%2 -eq 0) {Find 'Draw arrows'} else {$tile}
  $r=$hover.Current.BoundingRectangle
  if($r.IsEmpty) { $hover=All|Where-Object {$_.Current.AutomationId.StartsWith('HistoryItem-') -and !$_.Current.BoundingRectangle.IsEmpty}|Select-Object -First 1; $r=$hover.Current.BoundingRectangle }
  [NativeInput]::MovePointer([int]($r.Left+$r.Width/2),[int]($r.Top+$r.Height/2))
  Start-Sleep -Milliseconds 100
 }
 if(($tile.GetRuntimeId() -join ',') -ne $identities[$tile.Current.AutomationId]) {throw 'Hover replaced the history tile'}
 foreach($hover in @((Find 'Draw arrows'),$tile)) {
  $r=$hover.Current.BoundingRectangle
  [NativeInput]::MovePointer([int]($r.Left+$r.Width/2),[int]($r.Top+$r.Height/2))
  Start-Sleep -Milliseconds 600
  $before=[NativeInput]::CaptureRegion([int]$r.Left+2,[int]$r.Top+2,[int]$r.Width-4,[int]$r.Height-4)
  for($frame=0;$frame -lt 12;$frame++) {
   Start-Sleep -Milliseconds 50
   $after=[NativeInput]::CaptureRegion([int]$r.Left+2,[int]$r.Top+2,[int]$r.Width-4,[int]$r.Height-4)
   # Allow tiny changes from native tooltip shadows; detect hover-state resets/flashes.
   if([NativeInput]::MaximumDifference($before,$after) -gt 8) {
    [NativeInput]::SaveRegion([int]$r.Left+2,[int]$r.Top+2,[int]$r.Width-4,[int]$r.Height-4,(Join-Path $PSScriptRoot '..\local\qa-hover-after.png'))
    $windowBounds=$root.Current.BoundingRectangle
    [NativeInput]::SaveRegion([int]$windowBounds.Left,[int]$windowBounds.Top,[int]$windowBounds.Width,[int]$windowBounds.Height,(Join-Path $PSScriptRoot '..\local\qa-hover-window.png'))
    throw "Frame ${frame}: Hovered control did not stay visually stable: $($hover.Current.Name)"
   }
  }
 }
 Click 'Draw arrows'
 WaitFor {(All|Where-Object {$_.Current.Name -eq 'Width' -and $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::Text})} 'arrow width input'
 $width=All|Where-Object {$_.Current.Name -eq 'Width' -and $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::Text}|Select-Object -First 1
 $range=$width.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
 $before=$range.Current.Value
 Wheel $width 120
 if($range.Current.Value -ne $before+1) {throw "Wheel did not change width once: $before -> $($range.Current.Value)"}
 $range.SetValue(16)
 $head=All|Where-Object {$_.Current.Name -eq 'Arrowhead scale' -and $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::Text}|Select-Object -First 1
 $headRange=$head.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
 $headRange.SetValue(0)
 Wheel $head 120
 if($headRange.Current.Value -ne .1) {throw 'Arrowhead wheel did not increase from zero in exact 0.1 increments'}
 foreach($scale in @(.1,.2,.3,.7,1.1)) {
  $headRange.SetValue($scale)
  Click 'Draw rectangles'
  Click 'Draw arrows'
  $head=All|Where-Object {$_.Current.Name -eq 'Arrowhead scale' -and $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::Text}|Select-Object -First 1
  $headRange=$head.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
  if($headRange.Current.Value -ne $scale) {throw "Arrowhead scale picked up float noise after reopening: $scale -> $($headRange.Current.Value.ToString('R'))"}
  $input=$head.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty),([System.Windows.Automation.ControlType]::Edit)))
  $display=$input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
  if([double]::Parse($display,[Globalization.CultureInfo]::CurrentCulture) -ne $scale) {throw "Arrowhead display contains float noise: $display"}
 }
 $headRange.SetValue(.2)
 $input=$head.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty),([System.Windows.Automation.ControlType]::Edit)))
 $input.SetFocus()
 [NativeInput]::keybd_event(38,0,0,[UIntPtr]::Zero); [NativeInput]::keybd_event(38,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 250
 if([Math]::Abs($headRange.Current.Value-.3) -gt .000000000001) {throw 'Arrowhead up-key did not step by 0.1'}
 $input=$head.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty),([System.Windows.Automation.ControlType]::Edit)))
 if([double]::Parse($input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value,[Globalization.CultureInfo]::CurrentCulture) -ne .3) {throw 'Arrowhead up-key displayed floating-point noise'}
 $file=All|Where-Object {$_.Current.Name -eq 'File' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem}|Select-Object -First 1
 $file.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 Start-Sleep -Milliseconds 200
 if(!(All|Where-Object {$_.Current.Name -eq 'Copy as SVG'})) {throw 'Missing SVG export grouping'}
 if(All|Where-Object {$_.Current.Name -eq 'Poster (logos, icons)'}) {throw 'SVG preset is still at file-menu root'}
 [NativeInput]::keybd_event(27,0,0,[UIntPtr]::Zero); [NativeInput]::keybd_event(27,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 250
 WaitFor {(All|Where-Object {$_.Current.Name -eq 'Image' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem})} 'Image menu'
 $image=All|Where-Object {$_.Current.Name -eq 'Image' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem}|Select-Object -First 1
 if(!$image) { All|Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem}|ForEach-Object { "Menu: $($_.Current.Name)" }; throw 'Image menu unavailable' }
 $image.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
 Start-Sleep -Milliseconds 200
 if(All|Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem -and $_.Current.Name -in @('Arrow','Rectangle','Highlighter','Text','Emoji','Censor','Straighten','Free rotate')}) {throw 'Tool rail duplicated in Image menu'}
 [NativeInput]::keybd_event(27,0,0,[UIntPtr]::Zero); [NativeInput]::keybd_event(27,0,2,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 250
 Click 'Resize the image'
 WaitFor {(All|Where-Object {$_.Current.Name -eq 'Width (px)' -and $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::Text})} 'resize width input'
 $width=All|Where-Object {$_.Current.Name -eq 'Width (px)' -and $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::Text}|Select-Object -First 1
 $range=$width.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
 $before=$range.Current.Value
 Wheel $width 120
 if($range.Current.Value -ne $before+1) {throw 'Wheel did not adjust dialog width'}
 Click 'Cancel'
 [NativeInput]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
 WaitFor {[NativeInput]::GetForegroundWindow() -eq $p.MainWindowHandle} 'keyboard foreground focus'
 $selection=Find 'Move / select tool'
 $selection.SetFocus()
 WaitFor {$selection.Current.HasKeyboardFocus} 'tool button keyboard focus'
 [NativeInput]::keybd_event(32,0,0,[UIntPtr]::Zero); [NativeInput]::keybd_event(32,0,2,[UIntPtr]::Zero)
 WaitFor {(All|Where-Object {$_.Current.Name -eq 'Selection' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text})} 'Space activates the focused tool button'
 if($LiveDeJpeg) {
  Click 'Remove JPEG artifacts'
  WaitFor {(Find 'Cancel')} 'native JPEG cleanup dialog'
  WaitFor {(Find 'Accept edits').Current.IsEnabled -and !(Find 'Cancel')} 'real native JPEG cleanup applies and closes' 60
  if(!(Find 'Undo').Current.IsEnabled) {throw 'JPEG cleanup did not preserve undo'}
  Click 'Undo'
  foreach($current in @(All|Where-Object {$_.Current.AutomationId.StartsWith('HistoryItem-')})) {
   if(($current.GetRuntimeId() -join ',') -ne $identities[$current.Current.AutomationId]) {throw 'JPEG cleanup rebuilt the history controls'}
  }
  'PASS: real De-JPEG processing through the native dialog, automatic apply and undo'
 }
 'PASS: stable history controls/scroll across activation, steady hover pixels across rendered frames, wheel inputs in inspector/dialog, keyboard button activation, SVG menu and image-only menu'
} catch { All|ForEach-Object { "$($_.Current.ControlType.ProgrammaticName) | $($_.Current.Name) | $($_.Current.AutomationId)" }; throw } finally {if(!$p.HasExited) {Stop-Process -Id $p.Id}}
