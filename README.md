Screenzap is a screenshot tool for Windows with similar behavior to the MacOS screenshot feature. It has configurable keyboard shortcuts, and an option to start when logged in.

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
* Only puts Bitmap data on the clipboard. Some applications, like Blender, expects PNG.
