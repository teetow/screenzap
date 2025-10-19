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

# Done

## Feature: Save, Save As (Ctrl-S, Ctrl-Shift-S)

- Save generates a file name from the timestamp when the clipboard buffer was taken
- Save As... opens a standard file save dialog

## Feature: Image Editor now has a dedicated buffer

In order to allow more advanced editing features, when opening the image editor the contents of the clipboard is copied to a dedicated buffer, detaching it from the clipboard buffer. This allows us to copy and paste areas of the image editor buffer (which would otherwise always show the copied area).

## Feature: Undo and Redo (Ctrl-Z, Ctrl-Shift-Z)

- Any changes to the Image Editor buffer are recorded on an undo stack that stores the affected region along with its before/after state.
- Redo re-applies the latest change using the same stored data, keeping undo and redo within a single mechanism.

## Feature: Crop (Ctrl-T)

- The current selection becomes the new image dimensions. Selection is cleared afterwards so the next action starts fresh.

## Feature: "Replace with background" tool (Ctrl-B / Backspace)

- Selected area edges bleed towards the middle pixel, filling the selection with colors sampled from the border to provide a quick object removal.

## Feature: Text censor tool (Ctrl+E)

- Detects dense text rows inside the selection, trims to their horizontal footprint, then shuffles column-sized blocks per line to render the content unreadable while preserving the overall silhouette.
- Exposed on Ctrl+E and fully undoable.

# Deferred (ignore for now)

## Secondary hotkey configurability

- [ ] Add UI to expose and edit the instant-capture hotkey loaded from `Properties.Settings.Default.seqCaptureCombo` in `Screenzap.cs`. Users cannot change or discover this shortcut today, so the convenience feature is only half-delivered.
