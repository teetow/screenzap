Screenzap is a screenshot tool for Windows with similar behavior to the MacOS screenshot feature. It has configurable keyboard shortcuts, and an option to start when logged in.

## Latest release

[Download here](https://github.com/teetow/screenzap/releases/)

## Installation

Extract the complete release folder and run `Screenzap.exe`. Keep its runtime libraries, resource files and `Models` folder beside the executable.

For a stable per-user install path on Windows, prefer publishing to `%LOCALAPPDATA%\Programs\Screenzap` and pointing shortcuts there. Build outputs under `bin\Debug` and `bin\Release` are for development and should not be used as long-lived shortcut targets.

## Usage

Press the configured shortcut (default is `ctrl-alt-shift-4`), drag to select a screen region, and take your screenshot. It will go on the clipboard.

Double-click the tray icon (or choose **Sanitize Clipboard**) to open the WinUI image editor. Draw and select with the left tool rail, adjust the current tool or selection in the right inspector, and revisit clipboard images in the bottom history strip. Image operations are available directly above the canvas; document actions include Save, Copy, Undo, Redo and **Commit** (`Ctrl+Enter`). Commit accepts edits into the clipboard while retaining zoom, pan and undo.

History images and emoji can be dragged onto the canvas. Resize, perspective correction, free rotation, OCR censoring, background replacement, color correction, text optimization and De-JPEG use the existing editing algorithms. The View controls below the history provide zoom, Fit, 100% and transparency.

### Modifier keys

* Shift -- make selection square
* Space -- move selection rectangle
* Alt -- draw from center of selection (bit wonky)

## Todos and known issues

* Holding Shift and Alt simultaneously doesn't work
* Not all surfaces will get captured
* What You See Isn't Quite What You'll Get -- some visual elements, like context menus, will not always get captured
* DPI awareness is experimental and only Works On My Machine. Bug reports and PR:s welcome!

## Development

Screenzap is built as a 64-bit (`x64`) Windows application. Before rebuilding, make sure any running `Screenzap.exe` process is closed so the linker can overwrite the executable. Use the .NET CLI directly (for example `dotnet build screenzap/Screenzap.csproj -nr:false -m:1 -p:UseSharedCompilation=false`) rather than VS Code tasks so you see any build warnings or errors in real time and so the debugger can attach to the x64 process successfully.

For a stable local install while developing, use `dotnet publish screenzap/Screenzap.csproj -c Release -nr:false -m:1 -p:UseSharedCompilation=false -o %LOCALAPPDATA%\Programs\Screenzap` and launch that published copy. This keeps shortcuts and autorun targets stable even when the framework moniker or build layout changes.

See [the WinUI editor architecture and verification guide](docs/winui-editor.md) for the native UI, retained document backend, publishing requirements and isolated UI smoke mode. Approved design mockups are kept locally in gitignored `local/mockups/`.

### Text detection prerequisites

The text-region detector now prefers [Tesseract OCR](https://github.com/tesseract-ocr/tesseract). Copy the appropriate `tessdata` directory next to the executable (for example `screenzap\tessdata`) or point the environment variable `SCREENZAP_TESSDATA_PATH` to a folder containing `eng.traineddata` (or your chosen language). If no trained data is found the legacy heuristic detector is used instead.
