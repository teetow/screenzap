# Manual Test Checklist

## Undo / Redo

1. Launch Screenzap and capture or paste an image so the editor opens with real content.
2. Drag to create a selection, then press `C` (without modifiers) to apply the censor effect.
3. Press `Ctrl+Z` to undo the change and verify that the selection area returns to its pre-censored state.
4. Press `Ctrl+Shift+Z` to redo and confirm the censored pixels are restored.
5. Repeat steps 2–4 several times to ensure multiple undo/redo steps are recorded correctly and that the selection rectangle is preserved across operations.

## Crop

1. Capture or paste an image and drag a smaller selection that represents the desired crop area.
2. Press `Ctrl+T` or click **Crop** on the toolbar; the editor window should resize around the cropped content and the selection should clear.
3. Press `Ctrl+Z` to restore the original image dimensions and selection, then `Ctrl+Shift+Z` to reapply the crop.

## Replace with Background

1. Capture or paste an image, draw a selection around an object, and press `Ctrl+B`, tap `Backspace`, or click **Replace BG**.
2. Confirm the selection fills with surrounding colors that bleed inward, softening the object.
3. Press `Ctrl+Z` and `Ctrl+Shift+Z` to verify the operation integrates with undo/redo.
4. Edge-source exclusion check: place an object that touches one image edge (for example, a vertical line touching the left edge), select it, and run **Replace BG**. Confirm the touched edge is not used as a source (the line is fully removed instead of being reintroduced from edge pixels), while interpolation still uses the remaining edges.

## Save / Save As (regression spot check)

1. After editing, press `Ctrl+S` to trigger an automatic timestamped save and confirm the file appears in the configured capture folder.
2. Press `Ctrl+Shift+S`, choose a different destination, and confirm the saved file reflects the current editor state.

## Image Clipboard Reload

1. With the image editor open, capture or copy a new screenshot from another app and verify the **Reload** toolbar button displays its red badge.
2. Click **Reload** (or press `Ctrl+R`) and confirm the editor replaces its content with the latest clipboard image while clearing the badge.
3. Make an edit (for example, draw a rectangle), copy another screenshot, and click **Reload**. Ensure the warning dialog appears and only replaces the image when you confirm.
4. With the image editor still open, copy plain text in another app so the clipboard switches to text. Click **Reload** and confirm the prompt appears, then the text editor opens with the new text when you proceed.

## Clipboard Text Editor

1. Copy any block of Unicode text (no images) and double-click the tray icon. Verify the VSCode-style text editor opens, loads the clipboard contents, and paints in a monospace font.
2. Paste multiple lines, hold `Alt` and drag to create additional cursors, and confirm typing edits all carets simultaneously.
3. Press `Ctrl+F` and `Ctrl+H` to open the find/replace panel. Toggle Regex/Match Case/Whole Word and confirm `Next`, `Previous`, `Replace`, and `Replace All` respect the toggles. Invalid regex patterns should surface inline errors.
4. Use `Ctrl+S`, `Ctrl+Shift+S`, and the toolbar buttons to save text files alongside image captures. Confirm the tray balloon launches Explorer to the saved `.txt` file.
5. Copy long text back to the clipboard via the toolbar or `Ctrl+Shift+C` and ensure downstream apps receive the updated text.
6. Stage both text *and* image data on the clipboard (for example, copy an image from Paint and then copy text from Notepad without clearing). Double-click the tray icon and confirm the image editor still takes precedence.
7. With the text editor open, copy new text from another app and verify the **Reload** toolbar button shows its red badge. Click the button (or press `Ctrl+R`) and confirm the editor replaces its content with the newest clipboard text.
8. Make a local edit so the document is dirty, then copy new text in another app. When the badge appears, click **Reload** and confirm the warning dialog prevents data loss unless you choose to proceed.

## Viewport Overscroll Panning

1. Open an image and zoom in until it is larger than the editor window.
2. Middle-drag to pan the image down/right and confirm you can pull its top-left corner into the window (Photoshop-style), with the dark backdrop showing beyond the image edge, until roughly 48px of the image remains visible.
3. Pan the opposite way and confirm the same margin applies to the bottom-right corner.
4. Zoom out until the image fits the window and confirm it stays centered while panning.

## Selection Stamp / Clone Past Edges

1. Draw a marquee over some distinctive content and drag it (no modifiers) toward an image edge. Confirm it travels past the edge without stopping and no pixels change.
2. `Alt`-drag the marquee toward an edge. Confirm it also travels past the edge, and on release the clone lands clipped to the canvas.
3. Repeat with `Ctrl`-drag and confirm the stamp trail paints up to the canvas edge.
4. With a marquee active, tap plain `Arrow` keys: the marquee nudges 1px per press (10px with `Shift`) without touching pixels and without creating undo steps.
5. With a marquee active, tap `Ctrl+Arrow` a few times: the marquee nudges 1px per press, smearing the stamped content as it goes. Release `Ctrl` and confirm a single `Ctrl+Z` undoes the whole run.
6. With a marquee active, tap `Alt+Arrow` a few times: the clone floats with the marquee and is only committed when `Alt` is released; a single `Ctrl+Z` undoes it.

## Free Rotate

1. Open an image, click **Free Rotate** on the tool rail (with no selection active), and drag the handle above the image in a circle. Confirm the image spins live to match the cursor, with the canvas backdrop showing in the corners the rotated image no longer covers.
2. Hold `Shift` while dragging and confirm the angle snaps to 15° steps.
3. Click **Apply** (or press `Enter`). Confirm the canvas resizes to fit the rotated image with no clipped content, and `Ctrl+Z` / `Ctrl+Shift+Z` undo/redo it as one step.
4. Press `Escape` (or click **Cancel**) mid-drag and confirm the image is unchanged and no undo step was created.
5. Draw a selection first, then activate Free Rotate and drag. Confirm only the selection's content rotates in place — clipped to the marquee, with the surrounding image untouched — rather than resizing the canvas.
