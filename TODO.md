# Image Editor Backlog

## NOTES

Unless specified otherwise, all actions are destructive, but undoable.

## Feature: Revert

Since the image editor now is no longer just showing the clipboard state, on launch, we copy the buffer to an "original" that can be recalled at any time. This will not be updated -- subsequent clipboard copy operations will not touch it as long as the Image Editor is open. This is so that the user can always Revert to this state at will.

## Feature: Copy and paste (Ctrl+C, Ctrl+V)

- Ctrl+C already copies the current selection to the clipboard buffer.
- Implement Ctrl+V to paste the clipboard contents into the current selection, resizing to fit.
- Add a **Paste Original Size** command that resizes the selection to fit the pasted content instead.
- Keep freshly pasted content in a transient state until committed (Enter) or canceled (Esc).

## Feature: Paste Toolbar

When pasting content, a floating Paste toolbar appears. It shows inputs for X and Y, width and height, as well as a "scaling algorithm" dropdown that has Nearest Neighbor as well as whatever other scaling algos supported by the Graphics API. I assume linear or bilinear, no need to make it more complex than that. It also contains Cancel and OK buttons for aborting or committing the paste.

## Feature: Variable fonts

Variable fonts (OpenType 1.8+) support multiple design axes like weight, width, slant, etc. in a single font file.

### Implementation notes:
- WinForms `System.Drawing.Font` has limited variable font support
- Need to detect if selected font is a variable font (check for `fvar` table)
- Options for implementation:
  - Use `System.Windows.Media` (WPF) for font inspection
  - Use DirectWrite interop for full variable font control
  - Use a library like HarfBuzzSharp or SkiaSharp
- UI: Add a popout panel with sliders for each available axis
- Common axes: `wght` (weight), `wdth` (width), `slnt` (slant), `ital` (italic), `opsz` (optical size)

### Basic Bold/Italic first:
- Add `FontStyle` property to `TextAnnotation` (Bold, Italic, Underline)
- Add B/I/U toggle buttons to text toolbar
- This works with standard fonts and is a prerequisite for variable font support
