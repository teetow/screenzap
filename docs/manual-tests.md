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
