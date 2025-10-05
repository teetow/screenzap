# Image Editor Backlog

## NOTES
Unless specified otherwise, all actions are destructive, but undoable.

## Image Editor Toolbar
- Save to file
- Crop 
- Each tool below as we add them

## Feature: Save, Save As (Ctrl-S, Ctrl-Shift-S)
- Save will generate a file name from the timestamp when the clipboard buffer was taken
- Save As... opens a standard file save dialog

## Feature: Image Editor now has a dedicated buffer
In order to allow more advanced editing features, when opening the image editor the contents of the clipboard is copied to a dedicated buffer, detaching it from the clipboard buffer. This allows us to copy and paste areas of the image editor buffer (which would otherwise always show the copied area).

## Feature: Revert
Since the image editor now is no longer just showing the clipboard state, on launch, we copy the buffer to an "original" that can be recalled at any time. This will not be updated -- subsequent clipboard copy operations will not touch it as long as the Image Editor is open. This is so that the user can always Revert to this state at will.

## Feature: Crop (Ctrl-T)
The current selection becomes the new image dimensions.

## Feature: Undo and Redo (Ctrl-Z, Ctrl-Shift-Z)
Any changes to the Image Editor buffer are put on an Undo stack and can be undone / redone according to standard undo/redo patterns. Suggest they're implemented as a stack of areas that stores the affected selection rect, along with its before and after state. This makes the changes bidirectional, meaning Redo and Undo are part of the same mechanism.

## Feature: Copy and paste (Ctrl-C, Ctrl-V)
Any selection in the Image Editor can receive the current contents of the clipboard buffer, resized to fit the current selection. A secondary paste command, **Paste Original Size**, will resize the selection to fit the content rather than vice versa. A recently pasted selection is left in its transient state, meaning it can be moved and resized non-destructively. The transient state is committed by clicking outside the selection or hitting enter. It can be aborted by hitting Esc

## Feature: Paste Toolbar
When pasting content, a floating Paste toolbar appears. It shows inputs for X and Y, width and height, as well as a "scaling algorithm" dropdown that has Nearest Neighbor as well as whatever other scaling algos supported by the Graphics API. I assume linear or bilinear, no need to make it more complex than that. It also contains Cancel and OK buttons for aborting or committing the paste.

## Feature: "Replace with background" tool (Ctrl-B)
Selected area edges bleed towards the middle pixel (primitive "object removal")

## Feature: Text censor tool (Ctrl-E)
Text masses in the selected area get their meaning garbled but still recognizable as having been text.

- Detect lines of text in selection (use a vertical histogram -- five or more consecutive rows of extrema is deemed a line separator)
- Using line separators, make a local selection around each line of text
- Each local selection gets its columns of pixels garbled

# Deferred (ignore for now)

## Image editor enhancements
- [ ] Implement the `Ctrl+S` shortcut in `Components/ImageEditor.cs` (within `ImageEditor_KeyDown`). The handler block is empty, so saving the edited image or selection never occurs.

## Secondary hotkey configurability
- [ ] Add UI to expose and edit the instant-capture hotkey loaded from `Properties.Settings.Default.seqCaptureCombo` in `Screenzap.cs`. Users cannot change or discover this shortcut today, so the convenience feature is only half-delivered.
