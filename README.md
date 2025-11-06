Screenzap is a screenshot tool for Windows with similar behavior to the MacOS screenshot feature. It has configurable keyboard shortcuts, and an option to start when logged in.

## Latest release

[Download here](https://github.com/teetow/screenzap/releases/)

## Installation

Just put Screenzap.exe where you want it. Run it. nbd.

## Usage

Press the configured shortcut (default is `ctrl-alt-shift-4`), drag to select a screen region, and take your screenshot. It will go on the clipboard.

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

Screenzap is built as a 64-bit (`x64`) Windows application. Before rebuilding, make sure any running `Screenzap.exe` process is closed so the linker can overwrite the executable. Use the .NET CLI directly (for example `dotnet build screenzap/screenzap.csproj`) rather than VS Code tasks so you see any build warnings or errors in real time and so the debugger can attach to the x64 process successfully.

### Text detection prerequisites

The text-region detector now prefers [Tesseract OCR](https://github.com/tesseract-ocr/tesseract). Copy the appropriate `tessdata` directory next to the executable (for example `screenzap\tessdata`) or point the environment variable `SCREENZAP_TESSDATA_PATH` to a folder containing `eng.traineddata` (or your chosen language). If no trained data is found the legacy heuristic detector is used instead.
