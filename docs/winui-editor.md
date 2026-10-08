# WinUI editor

The production editor window uses WinUI 3 and Win2D. It is an unpackaged x64 Windows
application; publishing includes the Windows App SDK runtime beside the executable.
No Store registration, MSIX installation, browser, or WebView is needed.

## Presentation and document editing

`WinUI/ScreenzapApplication.cs` runs the native XAML dispatcher. `Screenzap` remains a
private Win32 tray, hotkey, capture and clipboard service. Opening the editor creates
`WinUI/EditorWindow`; closing its window hides it back to the tray. Quit disposes the
background services and exits the XAML application.

`Components/ImageDocumentEditor.Surface.cs` is the input/render/settings boundary.
`ImageDocumentEditor` is a plain disposable document editor. It owns image operations,
selection, annotations, text editing, floating image layers and undo. `ImageViewport` owns
view geometry and GDI rendering; it is not a control and has no window handle.
`ClipboardDocumentHost` coordinates clipboard history, exports and persistence without
creating a window. The WinForms editor, designer, toolbars, history panel, dialogs and
compatibility UI harness have been deleted.

GDI+, System.Drawing and Windows Forms remain dependencies of Windows platform services
such as tray, capture and clipboard interop. Shortcut and transparency settings use native
WinUI dialogs. They no longer supply
an alternate editor presentation.

The editor renders its image and overlays into an offscreen bitmap. Win2D presents BGRA
pixels using a reusable texture. Pointer coordinates are converted from XAML DIPs to device
pixels; 100% zoom continues to mean one image pixel per device pixel. Native focus, pointer
capture, cursor, menus, popups and dialogs belong to XAML. Startup explicitly initializes
OLE before the XAML dispatcher for clipboard and desktop drag/drop interoperability. A final pointer-release position
is applied before finishing a drag because Windows can coalesce its final move.

The clipboard host retains its history coordinator and persistence behavior. Its activation
hook opens the sole WinUI editor window. Both Commit and its native
`Ctrl+Enter` shortcut use the existing host transaction: preserve view, editable objects and undo/redo, render a flat image for
the clipboard, record the exported document revision as clean, and suppress the resulting
clipboard notification. Undo/redo returning to that revision restores the clean state.

## UI organization

- **Title/menu row:** native File, Edit, View, Image and Settings menus, document title and
  dirty indicator, Windows caption controls, and a draggable title region.
- **Operations row:** direct image operations on the left; undo, redo, save, copy and Commit
  on the right. Labels give way to icons when the window is narrow.
- **Tool rail:** tools that change what a canvas gesture does. Emoji opens a native flyout
  with recent choices, drag-to-place tiles and a text input for the Windows emoji picker.
- **Inspector:** settings for the active tool or selected annotation/layer. It also exposes
  layer visibility, merging, deletion and stacking. General image actions stay out of it.
- **History:** a separate resizable bottom filmstrip. History images can be dragged onto the
  canvas as image layers; refresh, duplicate, revert and delete are direct actions.
- **Status:** image dimensions, operation status, zoom, fit, actual size and transparency.

Numeric inputs support wheel adjustments without scrolling the inspector. Shift makes fine
adjustments and Ctrl makes coarse adjustments. Delete clears the selected pixels (or the
whole canvas if no pixels are selected) to transparency, with undo/redo. Selected
annotations and layers retain their own Delete behavior; Backspace fills from the background.

History controls are retained across activation and content updates. Only a changed tile's
thumbnail is refreshed. Hover and state polling do not reset button brushes or resize the
render surface; viewport resizing follows size or DPI changes only.

File's **Copy as SVG** submenu groups the existing Poster, Photo and B&W tracing presets.
The Image menu contains image operations; the tool rail owns gesture tools.
Windows ML is provided by WindowsAppSDK's ML dependency to keep managed and native ONNX
Runtime versions aligned. De-JPEG registers installed certified providers and falls back
to the bundled CPU provider; it does not install providers during cleanup.

Resize, color correction, capture shortcut and transparency color settings use native XAML
dialogs. Save As and capture folder selection use Windows file/folder pickers associated
with the WinUI HWND. De-JPEG retains the existing cancellation, revision guard and alpha
restoration. Color correction has a preview and preserves overlays and the view when applied.

## Build and verification

Use a Windows .NET 9 SDK with the Windows SDK build tools (restored through NuGet):

```powershell
dotnet build screenzap/Screenzap.csproj -nr:false -m:1 -p:UseSharedCompilation=false
powershell -NoProfile -File tools/test-regressions.ps1
dotnet publish screenzap/Screenzap.csproj -c Release -nr:false -m:1 -p:UseSharedCompilation=false -o "$env:LOCALAPPDATA\Programs\Screenzap"
```

The app targets .NET 8 and Windows 10 build 19041 or newer. Keep **all** published files,
including PRI files, native WinUI/Win2D libraries and `Models`, alongside the executable.
The minimal `App.xaml` initializes the native theme resources and generated XAML metadata;
the rest of the UI is constructed in C#.

`--winui-smoke` opens an isolated native editor with generated sample pixels. It does not
start tray services, restore or save clipboard history, or write committed edits to the
system clipboard. It bypasses the single-instance mutex so UI automation can run beside
the installed application. `--open-editor` opens the production editor at startup.
The obsolete `--editor-harness` and `--ui-capture` entry points have been removed.

Document regression tests drive the same input/render/settings boundary as WinUI, without
constructing an editor window or toolbar. They cover exact viewport geometry, editing,
rendering, history, persistence and non-destructive clipboard exports. The regression runner
still isolates the suite on a hidden Windows desktop for platform interop fixtures; it never
switches the user's desktop. Output defaults to ignored `local/regressions.log`.
Actual-window tests
must exercise native menus, pointer capture, typing, dialogs, history and resizing; a
successful compile cannot validate those interactions.

Foreground UI automation takes control of the desktop. Run the scripts below only when
the user explicitly requests desktop automation. The default verification is the isolated
regression runner above.

`tools/test-winui.ps1` launches the isolated editor and tests its real native window through
Windows UI Automation plus pointer and keyboard input. Run it in an interactive desktop
session; it requires the editor to hold foreground focus before injecting input.

`tools/test-winui-qa.ps1` checks history control identity and scroll retention with 32 images,
settled hover stability, wheel changes in inspector/dialog fields, and menu organization.
Use `-LiveDeJpeg` to also exercise the real model through the native cleanup dialog.

`tools/test-winui-hover.ps1` captures rendered frames immediately after mouse enter/leave
on the selected tool, an unselected tool, a history image and a document action. It rejects
bright/dark intermediate colors outside the normal and hovered endpoints, and saves frames
and timing data under ignored `local/hover-transitions/`. Tooltip timing is reset so their
separate fade does not obscure the button being measured. The editor button template uses
fixed hover/pressed overlays, retaining selection colors and borders, instead of interpolating
opaque document chrome toward WinUI's translucent hover background.

Arrowhead scale starts at the shaft width at 0, grows continuously in 0.1 increments,
and retains the default head proportions at 1. Head dimensions follow pen width once
so viewport zoom and exported images agree.
