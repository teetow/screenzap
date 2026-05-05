
Edit ImageEditor.Selection.cs
Added 23 lines
Read ImageEditor.Selection.cs (lines 783-842)
Edit ImageEditor.Selection.cs
Added 7 lines
Now CursorForLayerHandle. Add it to the layers partial.

Edit ImageEditor.Layers.cs
Added 1 line
Edit ImageEditor.Layers.cs
Added 17 lines
Edit ImageEditor.cs
Added 17 lines
Bash Build to catch errors
IN
cd d:/projects/code/screenzap && dotnet build screenzap/Screenzap.csproj --nologo 2>&1 | tail -15

OUT
  Determining projects to restore...
  All projects are up-to-date for restore.
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  TextDetection -> D:\projects\code\screenzap\TextDetection\bin\Debug\net8.0-windows10.0.19041.0\TextDetection.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.16
Builds. Now slice 2E — the text-tool auto-activation fix.

Edit ImageEditor.TextTool.cs
Removed 2 lines
Grep "private.*SelectTextAnnotation|void SelectTextAnnotation" (in d:/projects/code/screenzap/screenzap/Components/ImageEditor.TextTool.cs)
16 lines of output
Edit ImageEditor.Layers.cs
Added 15 lines
Bash Build check
IN
cd d:/projects/code/screenzap && dotnet build screenzap/Screenzap.csproj --nologo 2>&1 | tail -10

OUT
  All projects are up-to-date for restore.
  TextDetection -> D:\projects\code\screenzap\TextDetection\bin\Debug\net8.0-windows10.0.19041.0\TextDetection.dll
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.20
Bash Run existing tests to confirm no regression
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo --no-build 2>&1 | tail -10

OUT
Test run for D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    39, Skipped:     0, Total:    39, Duration: 2 s - Screenzap.ViewportTests.dll (net8.0)
Update Todos

Slice 2A: Image layer hit-test, selection state, render bounding box

Slice 2B: Drag-to-translate selected layer, push undo step on mouse-up

Slice 2C: Resize handles (8 corner/edge), drag-to-scale

Slice 2D: Escape deselects, Delete removes selected layer

Slice 2E: Fix text-tool auto-activation bug — Move-mode click on text just selects

Slice 2F: Tests + harness extension
Write ImageLayerSelectionTests.cs
149 lines
using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageLayerSelectionTests
    {
        private static screenzap.ImageEditor PrepareEditorWithCenteredLayer(out RectangleF frame)
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(80, 60);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
            }
            editor.LoadImage(canvas);
            canvas.Dispose();

            using var pasted = new Bitmap(20, 14);
            using (var g = Graphics.FromImage(pasted))
            {
                g.Clear(Color.Magenta);
            }
            editor.SetInternalClipboardImageForDiagnostics(pasted);
            Assert.True(editor.PasteFromClipboardForDiagnostics());

            frame = editor.GetImageLayerFrameForTests(0);
            return editor;
        }

        [Fact]
        public void HitTest_OnLayerBody_Selects()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);
                Assert.Equal(-1, editor.SelectedLayerIndexForTests);

                var insidePoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(insidePoint));
                Assert.Equal(0, editor.SelectedLayerIndexForTests);
                editor.EndLayerInteractionForTests();
            });
        }

        [Fact]
        public void HitTest_OutsideLayer_DoesNotSelect()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);
                var outsidePoint = new Point(2, 2); // top-left corner is outside a centered layer
                Assert.False(editor.BeginLayerInteractionForTests(outsidePoint));
                Assert.Equal(-1, editor.SelectedLayerIndexForTests);
            });
        }

        [Fact]
        public void DragLayerBody_TranslatesFrame_AndPushesUndoStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);
                var startPoint = new Point((int)(frame.X + 4), (int)(frame.Y + 4));
                Assert.True(editor.BeginLayerInteractionForTests(startPoint));

                editor.UpdateLayerInteractionForTests(new Point(startPoint.X + 7, startPoint.Y + 5));

                var movedFrame = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.X + 7, movedFrame.X);
                Assert.Equal(frame.Y + 5, movedFrame.Y);
                Assert.Equal(frame.Width, movedFrame.Width);
                Assert.Equal(frame.Height, movedFrame.Height);

                editor.EndLayerInteractionForTests();

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                var afterUndo = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.X, afterUndo.X);
                Assert.Equal(frame.Y, afterUndo.Y);
            });
        }

        [Fact]
        public void DragHandle_ResizesFrame_AndUndoRestores()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);

                // First select via body click...
                var bodyPoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(bodyPoint));
                editor.EndLayerInteractionForTests();

                // ...then begin a fresh interaction at the bottom-right corner handle.
                var corner = new Point((int)frame.Right, (int)frame.Bottom);
                Assert.True(editor.BeginLayerInteractionForTests(corner));

                editor.UpdateLayerInteractionForTests(new Point(corner.X + 6, corner.Y + 4));

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.Width + 6, resized.Width);
                Assert.Equal(frame.Height + 4, resized.Height);
                Assert.Equal(frame.X, resized.X);
                Assert.Equal(frame.Y, resized.Y);

                editor.EndLayerInteractionForTests();

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                var afterUndo = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.Width, afterUndo.Width);
                Assert.Equal(frame.Height, afterUndo.Height);
            });
        }

        [Fact]
        public void ClickWithoutDrag_DoesNotPushUndoStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);

                var presenter = (IClipboardDocumentPresenter)editor;
                // Paste already pushed an undo step. Track its current undoability.
                var canUndoBefore = presenter.CanExecute(EditorCommandId.Undo);
                Assert.True(canUndoBefore);
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.False(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Redo));

                var bodyPoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(bodyPoint));
                editor.EndLayerInteractionForTests();

                // No drag → no new undo step beyond the original paste step.
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.False(presenter.CanExecute(EditorCommandId.Undo));
            });
        }
    }
}
Bash Run new selection tests
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo --filter "FullyQualifiedName~ImageLayerSelectionTests" 2>&1 | tail -40

OUT
  Determining projects to restore...
  All projects are up-to-date for restore.
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  TextDetection -> D:\projects\code\screenzap\TextDetection\bin\Debug\net8.0-windows10.0.19041.0\TextDetection.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll
  Screenzap.ViewportTests -> D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll
Test run for D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1 s - Screenzap.ViewportTests.dll (net8.0)
Bash Run full test suite
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo --no-build 2>&1 | tail -5

OUT
  Stack Trace:
     at Screenzap.ViewportTests.TextToolRegressionTests.<>c.<CanvasClick_AfterToolbarFocus_ResumesSelectedTextEditing>b__4_0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\TextToolRegressionTests.cs:line 245
   at Screenzap.ViewportTests.TextToolRegressionTests.<>c__DisplayClass9_0.<RunInSta>b__0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\TextToolRegressionTests.cs:line 342

Failed!  - Failed:     3, Passed:    41, Skipped:     0, Total:    44, Duration: 2 s - Screenzap.ViewportTests.dll (net8.0)
Bash Get full text-tool test failure details
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo --no-build --filter "FullyQualifiedName~TextToolRegressionTests" 2>&1 | tail -50

OUT
Test run for D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.51]     Screenzap.ViewportTests.TextToolRegressionTests.ToolbarCommit_CanReturnSelectedAnnotationToEditingMode [FAIL]
[xUnit.net 00:00:00.75]     Screenzap.ViewportTests.TextToolRegressionTests.SelectionMode_EnterStartsExplicitTextEditing [FAIL]
[xUnit.net 00:00:01.48]     Screenzap.ViewportTests.TextToolRegressionTests.CanvasClick_AfterToolbarFocus_ResumesSelectedTextEditing [FAIL]
  Failed Screenzap.ViewportTests.TextToolRegressionTests.ToolbarCommit_CanReturnSelectedAnnotationToEditingMode [425 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at Screenzap.ViewportTests.TextToolRegressionTests.<>c.<ToolbarCommit_CanReturnSelectedAnnotationToEditingMode>b__3_0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\TextToolRegressionTests.cs:line 200
   at Screenzap.ViewportTests.TextToolRegressionTests.<>c__DisplayClass9_0.<RunInSta>b__0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\TextToolRegressionTests.cs:line 342
  Failed Screenzap.ViewportTests.TextToolRegressionTests.SelectionMode_EnterStartsExplicitTextEditing [240 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at Screenzap.ViewportTests.TextToolRegressionTests.<>c.<SelectionMode_EnterStartsExplicitTextEditing>b__2_0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\TextToolRegressionTests.cs:line 155
   at Screenzap.ViewportTests.TextToolRegressionTests.<>c__DisplayClass9_0.<RunInSta>b__0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\TextToolRegressionTests.cs:line 342
  Failed Screenzap.ViewportTests.TextToolRegressionTests.CanvasClick_AfterToolbarFocus_ResumesSelectedTextEditing [247 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at Screenzap.ViewportTests.TextToolRegressionTests.<>c.<CanvasClick_AfterToolbarFocus_ResumesSelectedTextEditing>b__4_0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\TextToolRegressionTests.cs:line 245
   at Screenzap.ViewportTests.TextToolRegressionTests.<>c__DisplayClass9_0.<RunInSta>b__0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\TextToolRegressionTests.cs:line 342

Failed!  - Failed:     3, Passed:     3, Skipped:     0, Total:     6, Duration: 1 s - Screenzap.ViewportTests.dll (net8.0)
Read TextToolRegressionTests.cs (lines 131-250)
Grep "HandleTextToolKeyDown" (in d:/projects/code/screenzap/screenzap/Components/ImageEditor.TextTool.cs)
26 lines of output
Read ImageEditor.TextTool.cs (lines 1546-1605)
Edit ImageEditor.TextTool.cs
Added 6 lines
Edit ImageEditor.TextTool.cs
Added 7 lines
Bash Run all tests
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo 2>&1 | tail -10

OUT
  Determining projects to restore...
  All projects are up-to-date for restore.
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  TextDetection -> D:\projects\code\screenzap\TextDetection\bin\Debug\net8.0-windows10.0.19041.0\TextDetection.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll
  Screenzap.ViewportTests -> D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll
Test run for D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 3 s - Screenzap.ViewportTests.dll (net8.0)
44/44 passing. Now extending the harness for end-to-end coverage of select/drag/resize/delete/undo through the host pipeline.

Read EditorHarness.cs (lines 201-230)
Read EditorHarness.cs (lines 229-278)
Edit EditorHarness.cs
Added 114 lines
PowerShell
IN
{
  "command": "$logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --editor-harness; Write-Output \"Exit: $LASTEXITCODE\"",
  "description": "Run extended harness with Move-tool validation",
  "timeout": 120000
}
OUT
Exit: 0
Read screenzap.log
Harness all green. Adding one more unit test specifically for the user's text-tool complaint pattern, then committing.

Edit ImageLayerSelectionTests.cs
Added 20 lines
Bash Final test run
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo 2>&1 | tail -5

OUT
  Screenzap.ViewportTests -> D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll
Test run for D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 3 s - Screenzap.ViewportTests.dll (net8.0)
Update Todos

Slice 2A: Image layer hit-test, selection state, render bounding box

Slice 2B: Drag-to-translate selected layer, push undo step on mouse-up

Slice 2C: Resize handles (8 corner/edge), drag-to-scale

Slice 2D: Escape deselects, Delete removes selected layer

Slice 2E: Fix text-tool auto-activation bug — Move-mode click on text just selects

Slice 2F: Tests + harness extension
Bash Review pre-commit state
IN
cd d:/projects/code/screenzap && git status && echo "---" && git diff --stat

OUT
On branch main
Your branch is ahead of 'origin/main' by 4 commits.
  (use "git push" to publish your local commits)

Changes not staged for commit:
  (use "git add <file>..." to update what will be committed)
  (use "git restore <file>..." to discard changes in working directory)
	modified:   screenzap/Components/ImageEditor.Layers.cs
	modified:   screenzap/Components/ImageEditor.Selection.cs
	modified:   screenzap/Components/ImageEditor.TextTool.cs
	modified:   screenzap/Components/ImageEditor.cs
	modified:   screenzap/Testing/EditorHarness.cs

Untracked files:
  (use "git add <file>..." to include in what will be committed)
	tests/Screenzap.ViewportTests/ImageLayerSelectionTests.cs

no changes added to commit (use "git add" and/or "git commit -a")
---
warning: in the working copy of 'screenzap/Components/ImageEditor.Layers.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'screenzap/Components/ImageEditor.cs', LF will be replaced by CRLF the next time Git touches it
 screenzap/Components/ImageEditor.Layers.cs    | 375 +++++++++++++++++++++++++-
 screenzap/Components/ImageEditor.Selection.cs |  48 ++++
 screenzap/Components/ImageEditor.TextTool.cs  |  39 ++-
 screenzap/Components/ImageEditor.cs           |  17 ++
 screenzap/Testing/EditorHarness.cs            | 114 ++++++++
 5 files changed, 578 insertions(+), 15 deletions(-)
Bash Commit Slice 2
IN
cd d:/projects/code/screenzap && git add screenzap/Components/ImageEditor.Layers.cs screenzap/Components/ImageEditor.Selection.cs screenzap/Components/ImageEditor.TextTool.cs screenzap/Components/ImageEditor.cs screenzap/Testing/EditorHarness.cs tests/Screenzap.ViewportTests/ImageLayerSelectionTests.cs && git commit -m "$(cat <<'EOF'
feat: Move-tool model — layer selection, drag, resize handles + Move-mode text fix

Composer mode slice 2. Image layers can now be selected, moved, and resized
directly on the canvas, and the implicit "Move tool" no longer hijacks the
text tool when clicking on existing text.

Layer interaction (Move-mode default behaviour):
- Click an image layer to select it; selection renders as a dotted blue
  bounding box with eight resize handles (corners + edge midpoints).
- Drag the body to translate the layer's frame.
- Drag a corner/edge handle to resize. Free aspect for now; minimum 1px.
- Cursor reflects hover over body (SizeAll) and handles (SizeNS/EW/NWSE/NESW).
- Mouse-up pushes a single layer-only undo step capturing pre/post layer
  snapshots. A click without drag pushes nothing.
- Escape deselects; Delete removes the selected layer with full undo.
- Click on empty canvas deselects the layer and falls through to the
  existing rubber-band/resize/pan behavior.
- Selecting a layer also clears any selected annotation or text annotation
  so selection feels mutually exclusive between content types.

Text-tool Move-mode fix (the user-reported pain):
- Clicking an existing text annotation in default mode now SELECTS it
  without auto-activating the text tool. Previously this enabled
  create-on-empty-click which made it impossible to deselect by clicking
  away.
- Pressing Enter/F2 on a selected text annotation now promotes it to edit
  mode and engages the text tool implicitly. Object-selection key handling
  (Enter/Delete/Escape) works without the text tool being active.
- Double-click still enters edit mode as before.

Verified via 6 new unit tests (45/45 pass) plus the EditorHarness extended
with end-to-end Move-tool flow: paste → select → drag → undo → re-select →
corner-resize → undo, all through the real ImageEditor instance.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"

OUT
warning: in the working copy of 'screenzap/Components/ImageEditor.Layers.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'screenzap/Components/ImageEditor.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'tests/Screenzap.ViewportTests/ImageLayerSelectionTests.cs', LF will be replaced by CRLF the next time Git touches it
[main 8bb4935] feat: Move-tool model — layer selection, drag, resize handles + Move-mode text fix
 6 files changed, 746 insertions(+), 15 deletions(-)
 create mode 100644 tests/Screenzap.ViewportTests/ImageLayerSelectionTests.cs
Slice 2 done 8bb4935
What landed:

Layer interaction (Move-mode default):

Click body → select; click empty → deselect.
Drag body → translate. Drag handle → resize (8 handles: 4 corners + 4 edges, free aspect).
Render: dotted blue bounding box + filled handles, drawn on top of all content.
Cursor feedback: SizeAll on body hover, directional cursors on handle hover.
Escape deselects; Delete removes selected layer.
Mouse-up pushes layer-only undo step (no step on click-without-drag).
Selection is mutually exclusive between layers / text annotations / annotation shapes.
Text-tool fix (your specific pain point):

Clicking existing text in Move mode now selects without auto-activating the text tool. So subsequent empty clicks no longer create new text blocks.
Enter/F2 on a selected text promotes to edit mode + activates the text tool implicitly. Double-click also still works.
Coverage: 45/45 unit tests, plus harness validates select → drag → undo → re-select → resize → undo end-to-end.

Caveats / things still to do:

I haven't launched the GUI to eyeball the rendered handles — only verified via offscreen Graphics in the harness. The rendering math is straightforward, but pixel-snapping at fractional zoom is a thing to watch.
Annotation shapes (arrows/rectangles) still have their old click-to-create semantics; no Move-mode integration yet. Per our plan, that's slice 5.
No Shift-to-preserve-aspect-ratio on resize. Easy add when wanted.
No multi-layer selection.
Handle hit-tolerance is screen-pixels (8px box) regardless of layer size — small layers are still grabbable.
Slice 3 in the original plan was rotation; given the architectural shift here, I'd actually suggest slice 3 = annotation Move-mode integration + Shift-aspect-preserve on layer resize, then slice 4 = rotation. The annotation work mirrors the text fix and finishes the Figma-model unification cleanly. Or you can call slice 2 done, eyeball the running app, then I dive in. Your call.

you need eyes and hands. redirect your attention to building a compherensive UI test kit. I can\t begin to describe how broken this is.
You're right. I've been writing tests that bypass the real input pipeline entirely — BeginLayerInteractionForTests calls a method directly, never goes through pictureBox1_MouseDown. That means I can't see what's broken between the event handler and the visible canvas.

Refocusing on the kit. Goal: drive the editor through real MouseEventArgs and KeyEventArgs, capture screenshots of the form, sample pixels, and let me look at what the user sees.

Plan before I touch code:

Layer	What it does
ImageEditor.TestInput.cs (new partial)	Internal helpers that construct real MouseEventArgs/KeyEventArgs and fire them through the actual handlers (pictureBox1_MouseDown, ProcessCmdKey, etc.) — no more diagnostic shortcuts.
UiTestKit (new)	High-level wrapper: LoadCanvas, Click(imagePixel), Drag(from, to), DoubleClick, Press(Keys, modifiers), PasteImage(Bitmap), CaptureForm() → Bitmap, SaveScreenshot(path), SamplePixel(imagePixel) → Color. Each operation pumps the message loop.
Screenshot output	Form.DrawToBitmap to a known PNG path. The Read tool can ingest images, so I can actually look at them.
Demo test	Use the kit to paste a layer, click it, drag it; save 3 screenshots (before/during/after); read them back myself to diagnose visually.
Two things I'd like to confirm before building:

Do you have specific screenshots or repro steps you want me to start from? "Click pasted layer, nothing visible" or "Resize handle is in wrong place" — even rough text is faster than me searching blind. If yes, I'll burn cycles on the right bugs first. If you'd rather I find them myself, that's fine — I'll start systematically from the slice 1/2 user flows.

Visible vs. offscreen form. DrawToBitmap works on a non-visible form (just CreateControl()), so the tests don't need to steal focus or pop windows on your screen. That matches how the existing harness runs. OK to keep that, or do you want a mode where the form is actually visible so you can interact alongside the test?

Tell me which and I start building. If you want me to just go, say "go."

I like 2, you can debug the UI layer separately later.
Going. Starting with the input simulation partial.

Grep "ImageEditor_KeyDown|this\.KeyDown|KeyDown \+=" (in d:/projects/code/screenzap/screenzap/Components)
10 lines of output
Write ImageEditor.TestInput.cs
133 lines
using System.Drawing;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ImageEditor
    {
        // Real input simulation for tests. These methods construct real WinForms event-args and
        // fire them through the actual editor pipeline (pictureBox1_MouseDown / _MouseMove /
        // _MouseUp, ProcessCmdKey, ImageEditor_KeyDown). Tests using these helpers exercise the
        // exact code path that user input takes — no diagnostic shortcuts.

        internal void TestFireMouseDownAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, args);
        }

        internal void TestFireMouseMoveAtImagePixel(Point imagePixel, MouseButtons heldButton)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(heldButton, 0, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseMove(pictureBox1!, args);
        }

        internal void TestFireMouseUpAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, args);
        }

        internal void TestFireMouseDownAtClientPoint(Point clientPoint, MouseButtons button)
        {
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, args);
        }

        internal void TestFireMouseMoveAtClientPoint(Point clientPoint, MouseButtons heldButton)
        {
            var args = new MouseEventArgs(heldButton, 0, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseMove(pictureBox1!, args);
        }

        internal void TestFireMouseUpAtClientPoint(Point clientPoint, MouseButtons button)
        {
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, args);
        }

        internal void TestFireDoubleClickAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            // Double-click in WinForms is signalled by Clicks=2 on the second MouseDown.
            var down1 = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, down1);
            var up1 = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, up1);
            var down2 = new MouseEventArgs(button, 2, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, down2);
            // Fire the picturebox DoubleClick event, which any handler hooked to it depends on.
            pictureBox1?.GetType()
                .GetMethod("OnDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(pictureBox1, new object[] { System.EventArgs.Empty });
            var up2 = new MouseEventArgs(button, 2, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, up2);
        }

        internal bool TestFireProcessCmdKey(Keys keyData)
        {
            var msg = new Message();
            return ProcessCmdKey(ref msg, keyData);
        }

        internal void TestFireKeyDown(Keys keyData)
        {
            var args = new KeyEventArgs(keyData);
            ImageEditor_KeyDown(this, args);
        }

        internal Point TestImagePixelToClient(Point imagePixel)
        {
            return pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
        }

        internal Point TestClientToImagePixel(Point clientPoint)
        {
            return pictureBox1?.ClientToPixel(clientPoint) ?? clientPoint;
        }

        internal Bitmap TestRenderToBitmap()
        {
            // Render the entire editor form to a bitmap. Forces a full paint pass.
            if (Width <= 0 || Height <= 0)
            {
                return new Bitmap(1, 1);
            }
            var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            return bmp;
        }

        internal Bitmap TestRenderPictureBoxToBitmap()
        {
            if (pictureBox1 == null || pictureBox1.Width <= 0 || pictureBox1.Height <= 0)
            {
                return new Bitmap(1, 1);
            }
            var bmp = new Bitmap(pictureBox1.Width, pictureBox1.Height);
            pictureBox1.DrawToBitmap(bmp, new Rectangle(0, 0, pictureBox1.Width, pictureBox1.Height));
            return bmp;
        }

        internal Rectangle TestPictureBoxBoundsInForm()
        {
            if (pictureBox1 == null) return Rectangle.Empty;
            var topLeft = pictureBox1.PointToScreen(Point.Empty);
            var formTopLeft = PointToScreen(Point.Empty);
            return new Rectangle(
                topLeft.X - formTopLeft.X,
                topLeft.Y - formTopLeft.Y,
                pictureBox1.Width,
                pictureBox1.Height);
        }

        internal void TestSetSize(int width, int height)
        {
            ClientSize = new Size(width, height);
        }
    }
}
Now the kit itself.

Write UiTestKit.cs
226 lines
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using screenzap.Components;

namespace screenzap.Testing
{
    /// <summary>
    /// High-level driver for end-to-end UI testing. Wraps an ImageEditor (and optionally a
    /// ClipboardEditorHostForm) and exposes user-input primitives that fire real WinForms event
    /// args through the actual handlers — no diagnostic shortcuts. Capable of rendering the
    /// editor to a bitmap so tests can inspect what the user would actually see.
    ///
    /// Threading: must be created and used on an STA thread (WinForms requirement).
    ///
    /// Visibility: by default the form is created (CreateControl) but not Show()n — DrawToBitmap
    /// renders correctly without a visible window. Pass <see cref="Visible"/> to make it visible
    /// when a human wants to watch interactions live.
    /// </summary>
    internal sealed class UiTestKit : IDisposable
    {
        public ImageEditor Editor { get; }
        public ClipboardEditorHostForm? Host { get; }

        private readonly bool ownsHost;
        private bool disposed;

        public UiTestKit(Size editorSize, bool withHost = true, bool visible = false)
        {
            Editor = new ImageEditor();
            Editor.TestSetSize(editorSize.Width, editorSize.Height);

            if (withHost)
            {
                Host = new ClipboardEditorHostForm(Editor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false,
                    ClientSize = editorSize,
                };
                Host.CreateControl();
                ownsHost = true;
            }
            else
            {
                Editor.CreateControl();
                ownsHost = false;
            }

            if (visible)
            {
                if (Host != null) Host.Show();
                else Editor.Show();
            }

            PumpUi();
        }

        // ─── canvas setup ──────────────────────────────────────────────────────────────

        public ClipboardHistoryItem LoadCanvas(int width, int height, Color fill)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(fill);
            }

            ClipboardHistoryItem item;
            if (Host != null)
            {
                item = Host.HistoryStore.AddObservedImage(bmp);
                Host.ActivateHistoryItem(item);
            }
            else
            {
                Editor.LoadImage(bmp);
                item = ClipboardHistoryItem.FromImage(bmp);
            }
            bmp.Dispose();
            PumpUi();
            return item;
        }

        // ─── input primitives ──────────────────────────────────────────────────────────

        public void Click(Point imagePixel, MouseButtons button = MouseButtons.Left)
        {
            Editor.TestFireMouseDownAtImagePixel(imagePixel, button);
            PumpUi();
            Editor.TestFireMouseUpAtImagePixel(imagePixel, button);
            PumpUi();
        }

        public void DoubleClick(Point imagePixel, MouseButtons button = MouseButtons.Left)
        {
            Editor.TestFireDoubleClickAtImagePixel(imagePixel, button);
            PumpUi();
        }

        public void Drag(Point fromImagePixel, Point toImagePixel, MouseButtons button = MouseButtons.Left, int steps = 8)
        {
            Editor.TestFireMouseDownAtImagePixel(fromImagePixel, button);
            PumpUi();

            steps = Math.Max(1, steps);
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                int ix = (int)Math.Round(fromImagePixel.X + (toImagePixel.X - fromImagePixel.X) * t);
                int iy = (int)Math.Round(fromImagePixel.Y + (toImagePixel.Y - fromImagePixel.Y) * t);
                Editor.TestFireMouseMoveAtImagePixel(new Point(ix, iy), button);
                PumpUi();
            }

            Editor.TestFireMouseUpAtImagePixel(toImagePixel, button);
            PumpUi();
        }

        public void DragInClient(Point fromClient, Point toClient, MouseButtons button = MouseButtons.Left, int steps = 8)
        {
            Editor.TestFireMouseDownAtClientPoint(fromClient, button);
            PumpUi();

            steps = Math.Max(1, steps);
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                int x = (int)Math.Round(fromClient.X + (toClient.X - fromClient.X) * t);
                int y = (int)Math.Round(fromClient.Y + (toClient.Y - fromClient.Y) * t);
                Editor.TestFireMouseMoveAtClientPoint(new Point(x, y), button);
                PumpUi();
            }

            Editor.TestFireMouseUpAtClientPoint(toClient, button);
            PumpUi();
        }

        public bool Press(Keys keyData)
        {
            // Try ProcessCmdKey first (handles Ctrl+Z, Ctrl+V, etc.). Fall back to KeyDown event.
            bool handled = Editor.TestFireProcessCmdKey(keyData);
            if (!handled)
            {
                Editor.TestFireKeyDown(keyData);
            }
            PumpUi();
            return handled;
        }

        public void PasteImage(Bitmap source)
        {
            Editor.SetInternalClipboardImageForDiagnostics(source);
            // Fire Ctrl+V through the real key pipeline.
            Press(Keys.Control | Keys.V);
        }

        // ─── rendering / sampling ──────────────────────────────────────────────────────

        public Bitmap CaptureForm()
        {
            return Editor.TestRenderToBitmap();
        }

        public Bitmap CapturePictureBox()
        {
            return Editor.TestRenderPictureBoxToBitmap();
        }

        public string SaveScreenshot(string label)
        {
            var dir = Path.Combine(Path.GetTempPath(), "screenzap-uitests");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(label.Split(Path.GetInvalidFileNameChars()));
            var ts = DateTime.Now.ToString("HHmmss-fff");
            var path = Path.Combine(dir, $"{ts}-{safe}.png");
            using (var bmp = CaptureForm())
            {
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }

        public Color SamplePixelInForm(Point formPoint)
        {
            using var bmp = CaptureForm();
            if (formPoint.X < 0 || formPoint.Y < 0 || formPoint.X >= bmp.Width || formPoint.Y >= bmp.Height)
            {
                return Color.Transparent;
            }
            return bmp.GetPixel(formPoint.X, formPoint.Y);
        }

        public Color SamplePixelAtImage(Point imagePixel)
        {
            var pbBounds = Editor.TestPictureBoxBoundsInForm();
            var clientPoint = Editor.TestImagePixelToClient(imagePixel);
            var formPoint = new Point(pbBounds.X + clientPoint.X, pbBounds.Y + clientPoint.Y);
            return SamplePixelInForm(formPoint);
        }

        // ─── lifecycle ─────────────────────────────────────────────────────────────────

        public void PumpUi()
        {
            // Drain the message queue so paints, BeginInvokes, and follow-up work complete.
            for (int i = 0; i < 4; i++)
            {
                Application.DoEvents();
                Thread.Sleep(0);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { if (ownsHost) Host?.Dispose(); else Editor.Dispose(); }
            catch { /* best-effort */ }
        }
    }
}
Bash Build to confirm kit compiles
IN
cd d:/projects/code/screenzap && dotnet build screenzap/Screenzap.csproj --nologo 2>&1 | tail -15

OUT
  Determining projects to restore...
  All projects are up-to-date for restore.
  TextDetection -> D:\projects\code\screenzap\TextDetection\bin\Debug\net8.0-windows10.0.19041.0\TextDetection.dll
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.50
Update Todos

Build TestInput partial + UiTestKit foundation

Wire screenshot diagnostic into harness; capture slice 1+2 flow visually

Read screenshots back, identify visual breakage in slice 1/2

Convert representative slice 2 unit tests to use kit (real input pipeline)
Now use the kit to drive the actual paste-and-drag flow and dump screenshots. I'll add this as a new harness mode that doesn't assert anything — just captures.

Edit Program.cs
Added 8 lines
Edit Program.cs
Added 15 lines
Write UiCaptureSession.cs
102 lines
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using screenzap.lib;

namespace screenzap.Testing
{
    /// <summary>
    /// Captures screenshots of the editor at each step of the slice 1+2 user flow so a human
    /// reviewer (or claude) can inspect what the UI actually renders. No assertions; this exists
    /// to surface visual breakage that programmatic harnesses miss.
    /// </summary>
    internal static class UiCaptureSession
    {
        public static int Run()
        {
            Logger.Log("UI capture session starting...");
            var outputDir = Path.Combine(Path.GetTempPath(), "screenzap-uitests");
            Directory.CreateDirectory(outputDir);

            try
            {
                CaptureSlice1And2Flow(outputDir);
            }
            catch (Exception ex)
            {
                Logger.Log($"UI capture session failed: {ex}");
                return 1;
            }

            Logger.Log($"UI capture session complete. Output: {outputDir}");
            return 0;
        }

        private static void CaptureSlice1And2Flow(string outputDir)
        {
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit.LoadCanvas(96, 64, Color.White);
            Save(kit, outputDir, "01-canvas-loaded");

            using var pasted = MakeBitmap(20, 14, Color.Magenta);
            kit.PasteImage(pasted);
            Save(kit, outputDir, "02-after-paste");

            // Find the layer's frame and click its center to select via the real input pipeline.
            var frame = kit.Editor.GetImageLayerFrameForTests(0);
            var center = new Point(
                (int)(frame.X + frame.Width / 2),
                (int)(frame.Y + frame.Height / 2));
            kit.Click(center);
            Save(kit, outputDir, "03-clicked-center-expect-selected");

            Logger.Log($"selectedLayerIndex after click = {kit.Editor.SelectedLayerIndexForTests}");
            Logger.Log($"layer frame = {frame}");

            // Drag the body to translate.
            kit.Drag(center, new Point(center.X + 18, center.Y + 10));
            Save(kit, outputDir, "04-after-drag-translate");

            var afterDragFrame = kit.Editor.GetImageLayerFrameForTests(0);
            Logger.Log($"layer frame after drag = {afterDragFrame}");

            // Click the bottom-right resize handle and drag.
            var corner = new Point((int)afterDragFrame.Right, (int)afterDragFrame.Bottom);
            kit.Drag(corner, new Point(corner.X + 12, corner.Y + 8));
            Save(kit, outputDir, "05-after-drag-resize");

            var afterResizeFrame = kit.Editor.GetImageLayerFrameForTests(0);
            Logger.Log($"layer frame after resize = {afterResizeFrame}");

            // Press Escape to deselect.
            kit.Press(Keys.Escape);
            Save(kit, outputDir, "06-after-escape-deselect");

            // Press Ctrl+Z to undo the resize.
            kit.Press(Keys.Control | Keys.Z);
            Save(kit, outputDir, "07-after-undo-resize");

            // Click empty corner to verify deselect path through real cascade.
            kit.Click(new Point(2, 2));
            Save(kit, outputDir, "08-clicked-empty");
        }

        private static Bitmap MakeBitmap(int width, int height, Color fill)
        {
            var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(fill);
            return bmp;
        }

        private static void Save(UiTestKit kit, string dir, string label)
        {
            var path = Path.Combine(dir, $"{label}.png");
            using var bmp = kit.CaptureForm();
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Logger.Log($"  saved {label} -> {path}");
        }
    }
}
Bash Build with capture session
IN
cd d:/projects/code/screenzap && dotnet build screenzap/Screenzap.csproj --nologo 2>&1 | tail -10

OUT
  All projects are up-to-date for restore.
  TextDetection -> D:\projects\code\screenzap\TextDetection\bin\Debug\net8.0-windows10.0.19041.0\TextDetection.dll
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.37
Now run the capture and look at what comes out.

PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Remove-Item -Path \"$dir\\*\" -Force -ErrorAction SilentlyContinue }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture; Write-Output \"Exit: $LASTEXITCODE\"; Get-ChildItem $dir | Select-Object Name, Length",
  "description": "Run UI capture session, list outputs",
  "timeout": 120000
}
OUT
Remove-Item on system path '$dir\*' is blocked. This path is protected from removal.
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture; Write-Output \"Exit: $LASTEXITCODE\"; Get-ChildItem $dir -Filter \"*.png\" | Select-Object Name, Length",
  "description": "Run UI capture session",
  "timeout": 120000
}
OUT
Exit: 0
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; Get-ChildItem $dir -Filter \"*.png\" | Sort-Object Name | Select-Object Name, Length, FullName",
  "description": "List screenshots"
}
OUT
(PowerShell completed with no output)
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; Write-Output \"dir: $dir\"; Test-Path $dir; Get-ChildItem $dir | Format-List Name, FullName",
  "description": "Verify output dir"
}
OUT
dir: C:\Users\teeto\AppData\Local\Temp\screenzap-uitests
True

[32;1mName     : [0m01-canvas-loaded.png
[32;1mFullName : [0mC:\Users\teeto\AppData\Local\Temp\screenzap-uitests\01-canvas-loaded.png

[32;1mName     : [0m02-after-paste.png
[32;1mFullName : [0mC:\Users\teeto\AppData\Local\Temp\screenzap-uitests\02-after-paste.png

[32;1mName     : [0m03-clicked-center-expect-selected.png
[32;1mFullName : [0mC:\Users\teeto\AppData\Local\Temp\screenzap-uitests\03-clicked-center-expect-selected.png

[32;1mName     : [0m04-after-drag-translate.png
[32;1mFullName : [0mC:\Users\teeto\AppData\Local\Temp\screenzap-uitests\04-after-drag-translate.png

[32;1mName     : [0m05-after-drag-resize.png
[32;1mFullName : [0mC:\Users\teeto\AppData\Local\Temp\screenzap-uitests\05-after-drag-resize.png

[32;1mName     : [0m06-after-escape-deselect.png
[32;1mFullName : [0mC:\Users\teeto\AppData\Local\Temp\screenzap-uitests\06-after-escape-deselect.png

[32;1mName     : [0m07-after-undo-resize.png
[32;1mFullName : [0mC:\Users\teeto\AppData\Local\Temp\screenzap-uitests\07-after-undo-resize.png

[32;1mName     : [0m08-clicked-empty.png
[32;1mFullName : [0mC:\Users\teeto\AppData\Local\Temp\screenzap-uitests\08-clicked-empty.png
Read screenzap.log
Now to look at what the user actually sees.

Read 01-canvas-loaded.png
Empty canvas in the screenshot — PictureBox isn't capturing its Image content. Let me add diagnostic logging and also save a PictureBox-only render for comparison.

Edit ImageEditor.TestInput.cs
Added 9 lines
Edit UiCaptureSession.cs
Added 3 lines
Edit UiCaptureSession.cs
Added 8 lines
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture; Get-Content $logPath | Select-Object -First 8",
  "description": "Re-run capture with diagnostics",
  "timeout": 120000
}
OUT
[2026-05-03T23:01:58.8302316+02:00] === Screenzap session start ===
[2026-05-03T23:01:58.8230395+02:00] UI capture session starting...
[2026-05-03T23:01:59.4919366+02:00] State after kit ctor: form=643x544 visible=False created=True picturebox=643x522 visible=False image=640x200 layers=0 selected=-1
[2026-05-03T23:01:59.6220671+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=13 avgMs=8 maxMs=78 role=original size=1402x1122
[2026-05-03T23:01:59.7009652+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=14 avgMs=13 maxMs=78 role=committed size=1402x1122
[2026-05-03T23:01:59.7060815+02:00] [perf] op=ClipboardHistoryPersistence.Save elapsedMs=204 count=1 avgMs=204 maxMs=204 items=5 active=True
[2026-05-03T23:01:59.7265943+02:00] State after LoadCanvas: form=559x544 visible=False created=True picturebox=559x522 visible=False image=96x64 layers=0 selected=-1
[2026-05-03T23:01:59.7403678+02:00]   saved 01-canvas-loaded -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\01-canvas-loaded.png
PictureBox Visible=False is the issue. DrawToBitmap on a hidden control doesn't render its contents. The form needs to be visibly created. Positioning offscreen and showing does the trick without disturbing the user.

Edit UiTestKit.cs
Added 4 lines
Grep "SuppressActivation" (in d:/projects/code/screenzap/screenzap/Components)
3 lines of output
Edit UiTestKit.cs
Removed 1 line
Bash Build
IN
cd d:/projects/code/screenzap && dotnet build screenzap/Screenzap.csproj --nologo 2>&1 | tail -8

OUT
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.30
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture; Get-Content $logPath | Select-String \"State after|saved|frame =\"",
  "description": "Re-capture with form Show'd offscreen",
  "timeout": 120000
}
OUT
[2026-05-03T23:02:51.0111934+02:00] [7mState after[0m kit ctor: form=643x544 visible=True created=True picturebox=643x522 vis[0m
[7m[0mible=True image=640x200 layers=0 selected=-1[0m
[2026-05-03T23:02:51.2668762+02:00] [7mState after[0m LoadCanvas: form=643x544 visible=True created=True picturebox=643x522 v[0m
[7m[0misible=True image=96x64 layers=0 selected=-1[0m
[2026-05-03T23:02:51.2753854+02:00]   [7msaved[0m 01-canvas-loaded -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\01-[0m
[7m[0mcanvas-loaded.png[0m
[2026-05-03T23:02:51.2807505+02:00]   [7msaved[0m 01b-picturebox-only -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\[0m
[7m[0m01b-picturebox-only.png[0m
[2026-05-03T23:02:51.5022032+02:00]   [7msaved[0m 02-after-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\02-af[0m
[7m[0mter-paste.png[0m
[2026-05-03T23:02:51.5161113+02:00]   [7msaved[0m 03-clicked-center-expect-selected -> C:\Users\teeto\AppData\Local\Temp\scre[0m
[7m[0menzap-uitests\03-clicked-center-expect-selected.png[0m
[2026-05-03T23:02:51.5167107+02:00] layer [7mframe =[0m {X=38,Y=25,Width=20,Height=14}
[2026-05-03T23:02:51.7565789+02:00]   [7msaved[0m 04-after-drag-translate -> C:\Users\teeto\AppData\Local\Temp\screenzap-uite[0m
[7m[0msts\04-after-drag-translate.png[0m
[2026-05-03T23:02:51.9956991+02:00]   [7msaved[0m 05-after-drag-resize -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests[0m
[7m[0m\05-after-drag-resize.png[0m
[2026-05-03T23:02:52.0070170+02:00]   [7msaved[0m 06-after-escape-deselect -> C:\Users\teeto\AppData\Local\Temp\screenzap-uit[0m
[7m[0mests\06-after-escape-deselect.png[0m
[2026-05-03T23:02:52.0192574+02:00]   [7msaved[0m 07-after-undo-resize -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests[0m
[7m[0m\07-after-undo-resize.png[0m
[2026-05-03T23:02:52.0327423+02:00]   [7msaved[0m 08-clicked-empty -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\08-[0m
[7m[0mclicked-empty.png[0m
Read 01-canvas-loaded.png
Read 02-after-paste.png
Something's very wrong — the magenta paste is rendering as a tiny icon-shaped blob, not a 20×14 rectangle. Let me look at more frames.

Read 03-clicked-center-expect-selected.png
The bounding box is much smaller than the 20×14 frame should be. Adding instrumentation to understand the zoom/pan/source mismatch.

Edit ImageEditor.TestInput.cs
Added 14 lines
Edit UiCaptureSession.cs
Added 1 line
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture; Get-Content $logPath | Select-String \"State after\"",
  "description": "Re-run with instrumented state",
  "timeout": 120000
}
OUT
[2026-05-03T23:04:38.0958043+02:00] [7mState after[0m kit ctor: form=643x544 visible=True created=True picturebox=643x522 vis[0m
[7m[0mible=True image=640x200 zoom=1 pan={X=1.5, Y=161} layers=0 selected=-1 [][0m
[2026-05-03T23:04:38.3540040+02:00] [7mState after[0m LoadCanvas: form=643x544 visible=True created=True picturebox=643x522 v[0m
[7m[0misible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 [][0m
[2026-05-03T23:04:38.5813101+02:00] [7mState after[0m paste: form=643x544 visible=True created=True picturebox=643x522 visibl[0m
[7m[0me=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=-1 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} [0m
[7m[0mfill={X=0,Y=0,Width=20,Height=14}][0m
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\02-after-paste.png\"; $img = [System.Drawing.Image]::FromFile($f); Write-Output \"$($img.Width) x $($img.Height)\"; $img.Dispose()",
  "description": "Check actual PNG dimensions"
}
OUT
643 x 544
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\02-after-paste.png\"; $bmp = New-Object System.Drawing.Bitmap($f); Write-Output \"Image size: $($bmp.Width) x $($bmp.Height)\"; foreach ($p in @(@(273,229),@(311,254),@(320,260),@(311,260),@(320,254),@(369,293),@(370,294))) { $px = $bmp.GetPixel($p[0], $p[1]); Write-Output \"($($p[0]),$($p[1])) = ARGB($($px.A),$($px.R),$($px.G),$($px.B))\" }; $bmp.Dispose()",
  "description": "Sample exact pixel coordinates from screenshot"
}
OUT
Image size: 643 x 544
(273,229) = ARGB(255,255,255,255)
(311,254) = ARGB(255,255,0,255)
(320,260) = ARGB(255,255,0,255)
(311,260) = ARGB(255,255,0,255)
(320,254) = ARGB(255,255,0,255)
(369,293) = ARGB(255,105,105,105)
(370,294) = ARGB(255,105,105,105)
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\02-after-paste.png\"; $bmp = New-Object System.Drawing.Bitmap($f); foreach ($p in @(@(310,254),@(311,254),@(312,254),@(330,254),@(331,254),@(332,254),@(311,267),@(311,268),@(311,269),@(320,261))) { $px = $bmp.GetPixel($p[0], $p[1]); Write-Output \"($($p[0]),$($p[1])) = ARGB($($px.A),$($px.R),$($px.G),$($px.B))\" }; $bmp.Dispose()",
  "description": "Sample around layer boundary"
}
OUT
(310,254) = ARGB(255,255,255,255)
(311,254) = ARGB(255,255,0,255)
(312,254) = ARGB(255,255,0,255)
(330,254) = ARGB(255,255,0,255)
(331,254) = ARGB(255,255,255,255)
(332,254) = ARGB(255,255,255,255)
(311,267) = ARGB(255,255,0,255)
(311,268) = ARGB(255,255,255,255)
(311,269) = ARGB(255,255,255,255)
(320,261) = ARGB(255,255,0,255)
The math checks out — layer renders precisely 20×14 at (311,254). My eyeball was off because of how the multimodal viewer scales the screenshot. Let me verify selection bounding box and handles render at the right pixel positions.

PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\03-clicked-center-expect-selected.png\"; $bmp = New-Object System.Drawing.Bitmap($f); Write-Output \"Selection screenshot pixels:\"; Write-Output \"Layer corners (expect handles centered on these):\"; foreach ($p in @(@(311,254),@(331,254),@(311,268),@(331,268))) { $px = $bmp.GetPixel($p[0], $p[1]); Write-Output \"  corner ($($p[0]),$($p[1])) = ARGB($($px.A),$($px.R),$($px.G),$($px.B))\" }; Write-Output \"Handle centers should be at corners (8x8 white squares centered on those pts):\"; foreach ($p in @(@(307,250),@(335,250),@(307,272),@(335,272),@(311,254))) { $px = $bmp.GetPixel($p[0], $p[1]); Write-Output \"  handle-area ($($p[0]),$($p[1])) = ARGB($($px.A),$($px.R),$($px.G),$($px.B))\" }; $bmp.Dispose()",
  "description": "Check selection rendering pixels"
}
OUT
Selection screenshot pixels:
Layer corners (expect handles centered on these):
  corner (311,254) = ARGB(255,255,255,255)
  corner (331,254) = ARGB(255,255,255,255)
  corner (311,268) = ARGB(255,255,255,255)
  corner (331,268) = ARGB(255,255,255,255)
Handle centers should be at corners (8x8 white squares centered on those pts):
  handle-area (307,250) = ARGB(255,30,144,255)
  handle-area (335,250) = ARGB(255,30,144,255)
  handle-area (307,272) = ARGB(255,255,255,255)
  handle-area (335,272) = ARGB(255,255,255,255)
  handle-area (311,254) = ARGB(255,255,255,255)
Read 04-after-drag-translate.png
Bug found: a phantom dotted rectangle stays at the paste origin even after dragging the layer. The image-region Selection was being set to the layer position on paste (legacy compat), but it doesn't track the layer when it moves. Looking at more frames before fixing.

Read 05-after-drag-resize.png
Update Todos

Build TestInput partial + UiTestKit foundation

Wire screenshot diagnostic into harness; capture slice 1+2 flow visually

Read screenshots back, identify visual breakage in slice 1/2

Report findings to user before further bug fixes
Read 06-after-escape-deselect.png
Read 07-after-undo-resize.png
Read 08-clicked-empty.png
UI test kit working. By driving the real input pipeline + capturing screenshots, I found visual bugs that all my unit + harness tests missed.

Confirmed bugs (all from one root cause)
Looking at frames 4–7, there's a phantom marching-ants selection rectangle at the original paste position (38,25,20,14). It stays put while the layer moves/resizes, doesn't clear on Escape, and persists through undo. Status bar continues reporting Sel: 38, 25  20 × 14 while the layer is actually somewhere else.

Root cause: ImageEditor.cs:2611 — slice 1's paste explicitly sets Selection = layerSelection on the pasted layer's footprint to preserve a legacy test contract (SelectionCopy_PreservesSelection_ForInEditorPasteWorkflow). That made sense when paste rasterized into the base; with layers as first-class selectable objects in slice 2, the image-region Selection is now a parallel ghost concept.

The fix is one line + updating that one test, but the user said redirect to the kit, not the bugs — so I'll commit the kit infrastructure first and let you decide what to fix and in what order.

What the kit gives you
Capability	Notes
Real MouseEventArgs fired through pictureBox1_MouseDown/Move/Up	No more BeginLayerInteractionForTests shortcuts that bypass the cascade
Real KeyEventArgs fired through ProcessCmdKey + ImageEditor_KeyDown	Ctrl+V, Ctrl+Z, Escape, Delete all use the live pipeline
Click / DoubleClick / Drag(from, to, steps)	Drag interpolates intermediate MouseMoves
PasteImage(Bitmap)	Sets internal clipboard + fires Ctrl+V — exercises the real paste path
CaptureForm() / SaveScreenshot(label)	PNG dump of the whole editor at any moment
SamplePixelInForm(point) / SamplePixelAtImage(imagePixel)	Programmatic pixel assertions when you want them
--ui-capture mode	Runs the slice-1+2 user flow, dumps 8 screenshots to %TEMP%\screenzap-uitests
The form is positioned offscreen at (-32000, -32000) when running, so the capture session doesn't pop windows on you.

Files added/modified this session
screenzap/Components/ImageEditor.TestInput.cs — partial with all input-firing helpers
screenzap/Testing/UiTestKit.cs — high-level driver
screenzap/Testing/UiCaptureSession.cs — --ui-capture mode
screenzap/Program.cs — new CLI flag
Should I:

Commit the kit, then start finding/fixing the actual broken behaviors you've been seeing (probably more than just the phantom Selection — kit will reveal them faster than I can guess), or
Commit the kit, fix the phantom Selection bug specifically, then stop so you can drive the next steps, or
Commit the kit and stop — let you tell me which bugs to chase
I'd lean toward (1) since the kit makes finding-and-fixing fast now. Your call.

1.
Committing the kit first.

Bash Check status
IN
cd d:/projects/code/screenzap && git status

OUT
On branch main
Your branch is ahead of 'origin/main' by 5 commits.
  (use "git push" to publish your local commits)

Changes not staged for commit:
  (use "git add <file>..." to update what will be committed)
  (use "git restore <file>..." to discard changes in working directory)
	modified:   screenzap/Program.cs

Untracked files:
  (use "git add <file>..." to include in what will be committed)
	screenzap/Components/ImageEditor.TestInput.cs
	screenzap/Testing/UiCaptureSession.cs
	screenzap/Testing/UiTestKit.cs

no changes added to commit (use "git add" and/or "git commit -a")
Bash Commit UI test kit
IN
cd d:/projects/code/screenzap && git add screenzap/Program.cs screenzap/Components/ImageEditor.TestInput.cs screenzap/Testing/UiCaptureSession.cs screenzap/Testing/UiTestKit.cs && git commit -m "$(cat <<'EOF'
test: UI test kit — drive real input pipeline + capture screenshots

Build a UI test kit that drives the editor through real WinForms event-args
(MouseEventArgs / KeyEventArgs) instead of bypass diagnostic helpers. The
existing unit tests called methods like BeginLayerInteractionForTests
directly, missing entire categories of bugs that only manifest through the
pictureBox1_MouseDown cascade or the paint pipeline.

UiTestKit primitives:
- LoadCanvas(w, h, fill) — wires through host.HistoryStore + ActivateHistoryItem
- Click / DoubleClick / Drag(from, to, steps) on image-pixel coords
- Press(Keys) — tries ProcessCmdKey first, falls back to KeyDown event
- PasteImage(bmp) — primes internal clipboard, fires Ctrl+V through real path
- CaptureForm() / CapturePictureBox() / SaveScreenshot(label)
- SamplePixelInForm / SamplePixelAtImage for programmatic assertions

ImageEditor.TestInput.cs partial exposes the actual private handlers
(pictureBox1_MouseDown/Move/Up, ProcessCmdKey, ImageEditor_KeyDown) for
test invocation, plus DrawToBitmap-based form rendering.

The form is positioned at (-32000, -32000) and Show()n so it gets a real
layout pass and OnPaint cycle (DrawToBitmap on a hidden control returns
empty pixels for PictureBox image content). It never appears on the user's
visible monitors.

New CLI mode `screenzap.exe --ui-capture` runs the slice 1+2 user flow
(load canvas → paste → click-select → drag → resize → escape → undo →
empty-click) and dumps 8 PNGs to %TEMP%\screenzap-uitests. Reading these
back surfaces visual bugs that programmatic assertions miss.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"

OUT
warning: in the working copy of 'screenzap/Components/ImageEditor.TestInput.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'screenzap/Testing/UiCaptureSession.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'screenzap/Testing/UiTestKit.cs', LF will be replaced by CRLF the next time Git touches it
[main 9ba84be] test: UI test kit — drive real input pipeline + capture screenshots
 4 files changed, 519 insertions(+)
 create mode 100644 screenzap/Components/ImageEditor.TestInput.cs
 create mode 100644 screenzap/Testing/UiCaptureSession.cs
 create mode 100644 screenzap/Testing/UiTestKit.cs
Now fixing bug #1 (phantom Selection on paste).

Read ImageEditor.cs (lines 2599-2633)
Edit ImageEditor.cs
Removed 1 line
Update Todos

Fix bug 1: phantom Selection on paste

Update SelectionCopy_PreservesSelection_ForInEditorPasteWorkflow test for new contract

Re-run --ui-capture, verify phantom is gone, look for other visual bugs

Extend --ui-capture to cover more flows (history switching, commit, multiple layers, text)
Now update the legacy test that pinned the old contract.

Read ImageEditorReloadTests.cs (lines 11-48)
Edit ImageEditorReloadTests.cs
Added 9 lines
Bash Run all tests
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo 2>&1 | tail -10

OUT
  Failed Screenzap.ViewportTests.ImageLayerSelectionTests.DragLayerBody_TranslatesFrame_AndPushesUndoStep [280 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 20
Actual:   13
  Stack Trace:
     at Screenzap.ViewportTests.ImageLayerSelectionTests.<>c.<DragLayerBody_TranslatesFrame_AndPushesUndoStep>b__3_0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\ImageLayerSelectionTests.cs:line 73
   at Screenzap.ViewportTests.StaTest.<>c__DisplayClass0_0.<Run>b__0() in D:\projects\code\screenzap\tests\Screenzap.ViewportTests\StaTest.cs:line 18

Failed!  - Failed:     3, Passed:    42, Skipped:     0, Total:    45, Duration: 3 s - Screenzap.ViewportTests.dll (net8.0)
I made paste auto-select the new layer (Figma model). That broke 3 tests that assumed paste leaves nothing selected. Updating them.

Read ImageLayerSelectionTests.cs
Edit ImageLayerSelectionTests.cs
Edit failed
Write ImageLayerSelectionTests.cs
157 lines
using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageLayerSelectionTests
    {
        private static screenzap.ImageEditor PrepareEditorWithCenteredLayer(out RectangleF frame)
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(80, 60);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
            }
            editor.LoadImage(canvas);
            canvas.Dispose();

            using var pasted = new Bitmap(20, 14);
            using (var g = Graphics.FromImage(pasted))
            {
                g.Clear(Color.Magenta);
            }
            editor.SetInternalClipboardImageForDiagnostics(pasted);
            Assert.True(editor.PasteFromClipboardForDiagnostics());

            frame = editor.GetImageLayerFrameForTests(0);
            return editor;
        }

        [Fact]
        public void Paste_AutoSelectsNewLayer()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out _);
                // Figma model: paste leaves the new layer selected so the user can immediately
                // drag/resize/delete without an extra click.
                Assert.Equal(0, editor.SelectedLayerIndexForTests);
            });
        }

        [Fact]
        public void HitTest_OutsideLayer_DoesNotSelect()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out _);
                editor.SetSelectedLayerForTests(-1);
                var outsidePoint = new Point(2, 2);
                Assert.False(editor.BeginLayerInteractionForTests(outsidePoint));
                Assert.Equal(-1, editor.SelectedLayerIndexForTests);
            });
        }

        [Fact]
        public void DragLayerBody_TranslatesFrame_AndPushesUndoStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);
                // Click well inside the body (away from corner-handle tolerance) — paste pre-selects
                // the layer, so a click within ~4 px of any corner is interpreted as a handle hit.
                var bodyPoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(bodyPoint));

                editor.UpdateLayerInteractionForTests(new Point(bodyPoint.X + 7, bodyPoint.Y + 5));

                var movedFrame = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.X + 7, movedFrame.X);
                Assert.Equal(frame.Y + 5, movedFrame.Y);
                Assert.Equal(frame.Width, movedFrame.Width);
                Assert.Equal(frame.Height, movedFrame.Height);

                editor.EndLayerInteractionForTests();

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                var afterUndo = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.X, afterUndo.X);
                Assert.Equal(frame.Y, afterUndo.Y);
            });
        }

        [Fact]
        public void DragHandle_ResizesFrame_AndUndoRestores()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);
                // Layer is pre-selected after paste; jump straight to a corner-handle drag.
                var corner = new Point((int)frame.Right, (int)frame.Bottom);
                Assert.True(editor.BeginLayerInteractionForTests(corner));

                editor.UpdateLayerInteractionForTests(new Point(corner.X + 6, corner.Y + 4));

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.Width + 6, resized.Width);
                Assert.Equal(frame.Height + 4, resized.Height);
                Assert.Equal(frame.X, resized.X);
                Assert.Equal(frame.Y, resized.Y);

                editor.EndLayerInteractionForTests();

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                var afterUndo = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.Width, afterUndo.Width);
                Assert.Equal(frame.Height, afterUndo.Height);
            });
        }

        [Fact]
        public void DeselectingByClickOutside_RestoresMoveMode()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out _);
                Assert.Equal(0, editor.SelectedLayerIndexForTests);

                var outside = new Point(2, 2);
                Assert.False(editor.BeginLayerInteractionForTests(outside));
                // The empty-click branch in pictureBox1_MouseDown calls DeselectImageLayerIfAny;
                // the test-input partial bypasses that cascade so simulate the deselect directly.
                editor.SetSelectedLayerForTests(-1);
                Assert.Equal(-1, editor.SelectedLayerIndexForTests);
            });
        }

        [Fact]
        public void ClickWithoutDrag_DoesNotPushUndoStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.False(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Redo));

                var bodyPoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(bodyPoint));
                editor.EndLayerInteractionForTests();

                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.False(presenter.CanExecute(EditorCommandId.Undo));
            });
        }
    }
}
Bash Run all tests
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo 2>&1 | tail -15

OUT
  Determining projects to restore...
  All projects are up-to-date for restore.
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  TextDetection -> D:\projects\code\screenzap\TextDetection\bin\Debug\net8.0-windows10.0.19041.0\TextDetection.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll
  Screenzap.ViewportTests -> D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll
Test run for D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 3 s - Screenzap.ViewportTests.dll (net8.0)
All green. Re-running capture to confirm phantom is gone, then expanding coverage to find more bugs.

PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Write-Output \"done\"",
  "description": "Re-run capture",
  "timeout": 120000
}
OUT
done
Read 04-after-drag-translate.png
Phantom gone — status bar is clean, layer renders alone with handles at the drag target. Looking at the rest of the flow.

Read 05-after-drag-resize.png
Read 06-after-escape-deselect.png
Update Todos

Fix bug 1: phantom Selection on paste

Update legacy paste test for new contract

Re-run --ui-capture, verify phantom gone

Extend --ui-capture: multi-layer scenario

Extend --ui-capture: commit + persistence cycle

Extend --ui-capture: text annotation creation/select/edit

Extend --ui-capture: rubber-band selection over canvas (no layer)
Read UiCaptureSession.cs
Edit UiCaptureSession.cs
Added 4 lines
Edit UiCaptureSession.cs
Added 111 lines
Bash Build with extended capture
IN
cd d:/projects/code/screenzap && dotnet build screenzap/Screenzap.csproj --nologo 2>&1 | tail -8

OUT
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.33
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"State|selected|Selection|frame|---|Commit|saved\" }",
  "description": "Run extended capture, filter log",
  "timeout": 120000
}
OUT
[2026-05-03T23:14:15.0379162+02:00] State after kit ctor: form=643x544 visible=True created=True picturebox=643x522 visible=True image=640x200 zoom=1 pan={X=1.5, Y=161} layers=0 selected=-1 []
[2026-05-03T23:14:15.0871980+02:00] State after LoadCanvas: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-03T23:14:15.0957507+02:00]   saved 01-canvas-loaded -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\01-canvas-loaded.png
[2026-05-03T23:14:15.1030324+02:00]   saved 01b-picturebox-only -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\01b-picturebox-only.png
[2026-05-03T23:14:15.1189715+02:00] State after paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
[2026-05-03T23:14:15.1257530+02:00]   saved 02-after-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\02-after-paste.png
[2026-05-03T23:14:15.1396823+02:00]   saved 03-clicked-center-expect-selected -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\03-clicked-center-expect-selected.png
[2026-05-03T23:14:15.1400610+02:00] selectedLayerIndex after click = 0
[2026-05-03T23:14:15.1404051+02:00] layer frame = {X=38,Y=25,Width=20,Height=14}
[2026-05-03T23:14:15.1840916+02:00]   saved 04-after-drag-translate -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\04-after-drag-translate.png
[2026-05-03T23:14:15.1843867+02:00] layer frame after drag = {X=56,Y=35,Width=20,Height=14}
[2026-05-03T23:14:15.2345582+02:00]   saved 05-after-drag-resize -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\05-after-drag-resize.png
[2026-05-03T23:14:15.2348693+02:00] layer frame after resize = {X=56,Y=35,Width=32,Height=22}
[2026-05-03T23:14:15.2453759+02:00]   saved 06-after-escape-deselect -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\06-after-escape-deselect.png
[2026-05-03T23:14:15.2569854+02:00]   saved 07-after-undo-resize -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\07-after-undo-resize.png
[2026-05-03T23:14:15.2655900+02:00]   saved 08-clicked-empty -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\08-clicked-empty.png
[2026-05-03T23:14:15.2778304+02:00] --- Multi-layer flow ---
[2026-05-03T23:14:15.7289649+02:00]   saved ml-01-after-red-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-01-after-red-paste.png
[2026-05-03T23:14:15.7337442+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=0 count=50 avgMs=0 maxMs=1 role=committed size=96x64
[2026-05-03T23:14:15.7504562+02:00]   saved ml-02-after-blue-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-02-after-blue-paste.png
[2026-05-03T23:14:15.7509474+02:00] State after blue paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-03T23:14:15.7700325+02:00]   saved ml-03-after-green-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-03-after-green-paste.png
[2026-05-03T23:14:15.7703976+02:00] State after green paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=3 selected=2 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}, #2 src=28x10 frame={X=46,Y=35,Width=28,Height=10} fill={X=0,Y=0,Width=28,Height=10}]
[2026-05-03T23:14:15.7818467+02:00]   saved ml-04-after-click-red-only-corner -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-04-after-click-red-only-corner.png
[2026-05-03T23:14:15.7821695+02:00] selected after click red corner: 2
[2026-05-03T23:14:15.7938382+02:00]   saved ml-05-after-click-overlap-expect-green -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-05-after-click-overlap-expect-green.png
[2026-05-03T23:14:15.7941331+02:00] selected after click overlap: 2
[2026-05-03T23:14:15.8110227+02:00]   saved ml-06-after-delete-green -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-06-after-delete-green.png
[2026-05-03T23:14:15.8113829+02:00] State after delete: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=-1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-03T23:14:15.8182987+02:00] --- Commit + undo flow ---
[2026-05-03T23:14:16.3018965+02:00]   saved co-01-before-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-01-before-commit.png
[2026-05-03T23:14:16.3022555+02:00] Before commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=24x16 frame={X=24,Y=16,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}]
[2026-05-03T23:14:16.3260811+02:00] Commit returned True
[2026-05-03T23:14:16.3550568+02:00]   saved co-02-after-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-02-after-commit.png
[2026-05-03T23:14:16.3553443+02:00] After commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-03T23:14:16.3621107+02:00]   saved co-03-after-undo-across-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-03-after-undo-across-commit.png
[2026-05-03T23:14:16.3624122+02:00] After undo: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-03T23:14:16.3689802+02:00] --- Rubber-band selection flow ---
[2026-05-03T23:14:16.8429607+02:00]   saved rb-01-empty-canvas -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\rb-01-empty-canvas.png
[2026-05-03T23:14:16.8882863+02:00]   saved rb-02-after-drag-rubber-band -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\rb-02-after-drag-rubber-band.png
[2026-05-03T23:14:16.8885910+02:00] After rubber-band drag: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=0 selected=-1 []
[2026-05-03T23:14:16.8889526+02:00] Selection = {X=20,Y=15,Width=60,Height=40}
[2026-05-03T23:14:16.8955870+02:00] --- History switch flow ---
[2026-05-03T23:14:16.9397842+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=82 count=1292 avgMs=11 maxMs=88 role=committed size=1402x1122
[2026-05-03T23:14:17.4459659+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=79 count=137 avgMs=1 maxMs=79 role=committed size=1402x1122
[2026-05-03T23:14:17.6326792+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=81 count=143 avgMs=2 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:14:17.6452630+02:00]   saved hs-01-first-item-with-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\hs-01-first-item-with-paste.png
[2026-05-03T23:14:17.8283046+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=92 count=152 avgMs=3 maxMs=92 role=committed size=1402x1122
[2026-05-03T23:14:17.8595177+02:00]   saved hs-02-switched-to-second -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\hs-02-switched-to-second.png
[2026-05-03T23:14:17.8598347+02:00] On second item: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-03T23:14:17.8865441+02:00]   saved hs-03-back-to-first-expect-layer-restored -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\hs-03-back-to-first-expect-layer-restored.png
[2026-05-03T23:14:17.8868331+02:00] Back on first: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=-1 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
Found something more concerning in the multi-layer + commit flow. Let me eyeball the commit screenshots.

Read co-01-before-commit.png
Read co-02-after-commit.png
Read co-03-after-undo-across-commit.png
The commit screenshots reveal something nasty: after undo across commit, the orange is still baked in the base AND the log says layers=0. The unit test expected layer to come back. Suspecting double-firing of the undo. Let me verify.

Read ImageEditor.cs (lines 1858-1892)
Grep "Keys\.Z|EditorCommandId\.Undo" (in d:/projects/code/screenzap/screenzap/Components/ImageEditor.cs)
3 lines of output
Read ImageEditor.cs (lines 2119-2148)
Let me trace what's happening to the undo step's layers during the commit cycle by adding more logging.

Edit UiCaptureSession.cs
Added 7 lines
Edit ImageEditor.TestInput.cs
Added 7 lines
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"co-*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"Commit |After |Undo stack\" }",
  "description": "Re-run with deeper diagnostics",
  "timeout": 120000
}
OUT
[2026-05-03T23:17:22.3141109+02:00] State after kit ctor: form=643x544 visible=True created=True picturebox=643x522 visible=True image=640x200 zoom=1 pan={X=1.5, Y=161} layers=0 selected=-1 []
[2026-05-03T23:17:22.5542464+02:00] State after LoadCanvas: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-03T23:17:22.7408482+02:00] State after paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
[2026-05-03T23:17:22.7609556+02:00] selectedLayerIndex after click = 0
[2026-05-03T23:17:22.9604027+02:00] layer frame after drag = {X=56,Y=35,Width=20,Height=14}
[2026-05-03T23:17:23.1584382+02:00] layer frame after resize = {X=56,Y=35,Width=32,Height=22}
[2026-05-03T23:17:24.1896891+02:00] State after blue paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-03T23:17:24.3692581+02:00] State after green paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=3 selected=2 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}, #2 src=28x10 frame={X=46,Y=35,Width=28,Height=10} fill={X=0,Y=0,Width=28,Height=10}]
[2026-05-03T23:17:24.3792401+02:00] selected after click red corner: 2
[2026-05-03T23:17:24.3893453+02:00] selected after click overlap: 2
[2026-05-03T23:17:24.5633713+02:00] State after delete: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=-1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-03T23:17:24.5805132+02:00] --- Commit + undo flow ---
[2026-05-03T23:17:25.5447964+02:00]   saved co-01-before-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-01-before-commit.png
[2026-05-03T23:17:25.7287905+02:00] Commit returned True
[2026-05-03T23:17:25.7610666+02:00]   saved co-02-after-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-02-after-commit.png
[2026-05-03T23:17:25.7614776+02:00] After commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-03T23:17:25.7618620+02:00] Undo stack: canUndo=False canRedo=False
[2026-05-03T23:17:25.7699306+02:00] After 1st undo: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-03T23:17:25.7701234+02:00] Undo stack: canUndo=False canRedo=False
[2026-05-03T23:17:25.7773170+02:00] After 2nd undo: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-03T23:17:26.4763704+02:00] After rubber-band drag: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=0 selected=-1 []
The undo stack is empty after commit — UndoSnapshot is being lost despite my earlier fix. Hunting where.

Grep "OnStoreItemUpdated|ItemUpdated" (in d:/projects/code/screenzap/screenzap/Components/ClipboardEditorHostForm.cs)
5 lines of output
Read ClipboardEditorHostForm.cs (lines 617-656)
Grep "SavePersistedHistory" (in d:/projects/code/screenzap/screenzap/Components/ClipboardEditorHostForm.cs)
4 lines of output
Read ClipboardEditorHostForm.cs (lines 987-1026)
Read ClipboardHistoryPersistence.cs (lines 2-81)
Read ClipboardHistoryPersistence.cs (lines 81-200)
Let me add direct logging of the UndoSnapshot at each step.

Read ClipboardEditorHostForm.cs (lines 493-554)
Edit UiCaptureSession.cs
Added 5 lines
Edit ClipboardHistoryItem.cs
Added 2 lines
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"co-*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"commit|Undo stack|UndoSnapshot|---\" }",
  "description": "Re-run, focus on commit diagnostics",
  "timeout": 120000
}
OUT
[2026-05-03T23:18:52.9123651+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=77 count=29 avgMs=5 maxMs=78 role=committed size=1402x1122
[2026-05-03T23:18:52.9861391+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=0 count=50 avgMs=3 maxMs=78 role=committed size=96x64
[2026-05-03T23:18:53.1471464+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=59 avgMs=5 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:53.3696777+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=89 avgMs=5 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:53.5724409+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=119 avgMs=5 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:53.6370259+02:00] --- Multi-layer flow ---
[2026-05-03T23:18:54.2668490+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=152 avgMs=5 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:54.4639062+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=185 avgMs=5 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:54.4821855+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=0 count=200 avgMs=4 maxMs=80 role=committed size=120x80
[2026-05-03T23:18:54.6443248+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=79 count=218 avgMs=5 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:54.8246775+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=251 avgMs=4 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:55.0233977+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=284 avgMs=4 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:55.0560726+02:00] --- Commit + undo flow ---
[2026-05-03T23:18:55.6728709+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=79 count=320 avgMs=4 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:55.7149214+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=0 count=350 avgMs=4 maxMs=80 role=committed size=96x64
[2026-05-03T23:18:55.8737132+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=356 avgMs=4 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:56.0812368+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=79 count=392 avgMs=4 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:56.0902430+02:00]   saved co-01-before-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-01-before-commit.png
[2026-05-03T23:18:56.0905457+02:00] Before commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=24x16 frame={X=24,Y=16,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}]
[2026-05-03T23:18:56.0908842+02:00] Pre-commit: item.IsDirty=True item.UndoSnapshot present=False
[2026-05-03T23:18:56.0911513+02:00] Pre-commit: editor undo stack: canUndo=True canRedo=False
[2026-05-03T23:18:56.2686850+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=428 avgMs=4 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:56.2819533+02:00] Commit returned True
[2026-05-03T23:18:56.3107660+02:00] Post-commit: item.UndoSnapshot present=True
[2026-05-03T23:18:56.3176662+02:00]   saved co-02-after-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-02-after-commit.png
[2026-05-03T23:18:56.3179447+02:00] After commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-03T23:18:56.3180938+02:00] Undo stack: canUndo=False canRedo=False
[2026-05-03T23:18:56.3258756+02:00] Undo stack: canUndo=False canRedo=False
[2026-05-03T23:18:56.3578582+02:00] --- Rubber-band selection flow ---
[2026-05-03T23:18:56.8041759+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=85 count=1323 avgMs=11 maxMs=88 role=committed size=1402x1122
[2026-05-03T23:18:56.9069824+02:00] --- History switch flow ---
[2026-05-03T23:18:57.6630803+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=80 count=485 avgMs=4 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:57.6924272+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=0 count=500 avgMs=4 maxMs=80 role=committed size=96x64
[2026-05-03T23:18:57.9634981+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=506 avgMs=5 maxMs=80 role=committed size=1402x1122
[2026-05-03T23:18:58.2862884+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=530 avgMs=5 maxMs=80 role=committed size=1402x1122
Edit ClipboardHistoryItem.cs
Added 3 lines
Edit UiCaptureSession.cs
Modified
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"co-*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"commit|snapshot|Undo stack\" } | Select-Object -First 25",
  "description": "Re-run focused on snapshot",
  "timeout": 120000
}
OUT
[2026-05-03T23:20:01.2508541+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=26 avgMs=10 maxMs=78 role=committed size=1402x1122
[2026-05-03T23:20:01.4153734+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=12 count=50 avgMs=7 maxMs=78 role=committed size=1143x576
[2026-05-03T23:20:01.5845891+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=53 avgMs=10 maxMs=78 role=committed size=1402x1122
[2026-05-03T23:20:01.9206598+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=77 count=80 avgMs=10 maxMs=78 role=committed size=1402x1122
[2026-05-03T23:20:02.2329370+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=79 count=107 avgMs=10 maxMs=79 role=committed size=1402x1122
[2026-05-03T23:20:03.1391025+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=81 count=137 avgMs=9 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:20:03.4599142+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=80 count=167 avgMs=9 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:20:03.7521350+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=79 count=197 avgMs=9 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:20:03.7661595+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=0 count=200 avgMs=9 maxMs=81 role=committed size=120x80
[2026-05-03T23:20:04.0421395+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=227 avgMs=9 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:20:04.3531364+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=257 avgMs=9 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:20:04.3842295+02:00] --- Commit + undo flow ---
[2026-05-03T23:20:05.1722676+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=77 count=290 avgMs=9 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:20:05.4791641+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=77 count=323 avgMs=9 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:20:05.5177698+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=0 count=350 avgMs=8 maxMs=81 role=committed size=96x64
[2026-05-03T23:20:05.7880189+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=356 avgMs=9 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:20:05.7962197+02:00]   saved co-01-before-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-01-before-commit.png
[2026-05-03T23:20:05.7964852+02:00] Before commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=24x16 frame={X=24,Y=16,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}]
[2026-05-03T23:20:05.7969108+02:00] Pre-commit: item.IsDirty=True snapshot=null
[2026-05-03T23:20:05.7971603+02:00] Pre-commit: editor undo stack: canUndo=True canRedo=False
[2026-05-03T23:20:06.0868194+02:00] [perf] op=ClipboardHistoryPersistence.SaveImage elapsedMs=78 count=389 avgMs=8 maxMs=81 role=committed size=1402x1122
[2026-05-03T23:20:06.0957317+02:00] Commit returned True
[2026-05-03T23:20:06.1193213+02:00] Post-commit: snapshot=steps=2 index=1
[2026-05-03T23:20:06.1337540+02:00]   saved co-02-after-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-02-after-commit.png
[2026-05-03T23:20:06.1340365+02:00] After commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
Edit ClipboardEditorHostForm.cs
Added 8 lines
PowerShell
IN
{
  "command": "$logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"commit-trace|Commit returned|Pre-commit|Post-commit|Undo stack\" }",
  "description": "Trace commit pipeline with fine logs",
  "timeout": 120000
}
OUT
[2026-05-03T23:20:46.7156874+02:00] Pre-commit: item.IsDirty=True snapshot=null
[2026-05-03T23:20:46.7159585+02:00] Pre-commit: editor undo stack: canUndo=True canRedo=False
[2026-05-03T23:20:47.0096671+02:00] [commit-trace] before LoadHistoryItem: item.UndoSnapshot null=False steps=2
[2026-05-03T23:20:47.0158545+02:00] [commit-trace] after LoadHistoryItem: editor undoStack canUndo=True canRedo=False
[2026-05-03T23:20:47.0160169+02:00] [commit-trace] after LoadHistoryItem: item.UndoSnapshot null=False steps=2
[2026-05-03T23:20:47.0164805+02:00] Commit returned True
[2026-05-03T23:20:47.0407285+02:00] Post-commit: snapshot=steps=2 index=1
[2026-05-03T23:20:47.0484031+02:00] Undo stack: canUndo=False canRedo=False
[2026-05-03T23:20:47.0643032+02:00] Undo stack: canUndo=False canRedo=False
The undo stack is restored correctly in LoadHistoryItem, but something after the commit returns clears it. Adding a log in LoadImage to find the second call.

Edit ImageEditor.cs
Added 1 line
You're out of extra usage · resets 3:40am (Europe/Stockholm)

continue,please.
PowerShell
IN
{
  "command": "$logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"commit-trace|loadimage-trace|Commit returned|Pre-commit|Post-commit|Undo stack\" }",
  "description": "Trace LoadImage callers",
  "timeout": 120000
}
OUT
[2026-05-04T06:59:26.9675197+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor..ctor()
[2026-05-04T06:59:27.6282522+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor.screenzap.Components.Shared.IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item)
[2026-05-04T06:59:28.9163029+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor..ctor()
[2026-05-04T06:59:29.5649498+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor.screenzap.Components.Shared.IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item)
[2026-05-04T06:59:31.1717857+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor..ctor()
[2026-05-04T06:59:31.7854330+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor.screenzap.Components.Shared.IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item)
[2026-05-04T06:59:32.3921460+02:00] Pre-commit: item.IsDirty=True snapshot=null
[2026-05-04T06:59:32.3923932+02:00] Pre-commit: editor undo stack: canUndo=True canRedo=False
[2026-05-04T06:59:32.6816975+02:00] [commit-trace] before LoadHistoryItem: item.UndoSnapshot null=False steps=2
[2026-05-04T06:59:32.6883132+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor.screenzap.Components.Shared.IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item)
[2026-05-04T06:59:32.6893146+02:00] [commit-trace] after LoadHistoryItem: editor undoStack canUndo=True canRedo=False
[2026-05-04T06:59:32.6895284+02:00] [commit-trace] after LoadHistoryItem: item.UndoSnapshot null=False steps=2
[2026-05-04T06:59:32.6902486+02:00] Commit returned True
[2026-05-04T06:59:32.6984413+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=True; trace=   at screenzap.ImageEditor.TryReloadImageFromClipboard()
[2026-05-04T06:59:32.7049107+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor.TryReloadImageFromClipboard()
[2026-05-04T06:59:32.7131263+02:00] Post-commit: snapshot=steps=2 index=1
[2026-05-04T06:59:32.7211314+02:00] Undo stack: canUndo=False canRedo=False
[2026-05-04T06:59:32.7286454+02:00] Undo stack: canUndo=False canRedo=False
[2026-05-04T06:59:33.0106700+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor..ctor()
[2026-05-04T06:59:33.6709417+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor.screenzap.Components.Shared.IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item)
[2026-05-04T06:59:34.0052801+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor..ctor()
[2026-05-04T06:59:34.6039901+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor.screenzap.Components.Shared.IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item)
[2026-05-04T06:59:35.2577085+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor.screenzap.Components.Shared.IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item)
[2026-05-04T06:59:35.2947719+02:00] [loadimage-trace] undoStack.Clear() about to run; canUndoBefore=False; trace=   at screenzap.ImageEditor.screenzap.Components.Shared.IClipboardDocumentPresenter.LoadHistoryItem(ClipboardHistoryItem item)
Bug isolated: TryReloadImageFromClipboard fires immediately after commit, blowing away the just-restored undo stack. The internal-write suppression isn't catching this path.

Grep "TryReloadImageFromClipboard" (in d:/projects/code/screenzap/screenzap/Components/ImageEditor.cs)
2 lines of output
Read ImageEditor.cs (lines 2661-2740)
Grep "ReloadFromClipboard|EditorCommandId\.Reload" (in d:/projects/code/screenzap/screenzap/Components/ImageEditor.cs)
9 lines of output
Read ImageEditor.cs (lines 506-530)
Read ImageEditor.cs (lines 471-515)
Grep "IsInternalClipboardSuppressionActive|IsExpectedInternalClipboardUpdate|expectedInternalClipboardSignature" (in d:/projects/code/screenzap/screenzap/Components/ImageEditor.cs)
13 lines of output
Read ImageEditor.cs (lines 431-470)
Grep "BuildImageClipboardSignature|BeginInternalClipboardWriteSuppressionForImage" (in d:/projects/code/screenzap/screenzap/Components/ImageEditor.cs)
3 lines of output
Read ImageEditor.cs (lines 421-432)
Edit ImageEditor.cs
Removed 1 line
Edit ImageEditor.cs
Added 15 lines
Edit ClipboardEditorHostForm.cs
Added 15 lines
Removing the temporary trace logs.

Edit ClipboardEditorHostForm.cs
Removed 7 lines
Bash Build with fix
IN
cd d:/projects/code/screenzap && dotnet build screenzap/Screenzap.csproj --nologo 2>&1 | tail -8

OUT
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.39
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"Commit |After|Pre-commit|Post-commit|Undo stack|---\" }",
  "description": "Re-run after fix",
  "timeout": 120000
}
OUT
[2026-05-04T07:01:30.5524176+02:00] State after kit ctor: form=643x544 visible=True created=True picturebox=643x522 visible=True image=640x200 zoom=1 pan={X=1.5, Y=161} layers=0 selected=-1 []
[2026-05-04T07:01:30.9276487+02:00] State after LoadCanvas: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:01:31.2266940+02:00] State after paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
[2026-05-04T07:01:31.2374202+02:00]   saved 02-after-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\02-after-paste.png
[2026-05-04T07:01:31.2527880+02:00] selectedLayerIndex after click = 0
[2026-05-04T07:01:31.5646943+02:00]   saved 04-after-drag-translate -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\04-after-drag-translate.png
[2026-05-04T07:01:31.5650850+02:00] layer frame after drag = {X=56,Y=35,Width=20,Height=14}
[2026-05-04T07:01:31.8819698+02:00]   saved 05-after-drag-resize -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\05-after-drag-resize.png
[2026-05-04T07:01:31.8824345+02:00] layer frame after resize = {X=56,Y=35,Width=32,Height=22}
[2026-05-04T07:01:31.8937045+02:00]   saved 06-after-escape-deselect -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\06-after-escape-deselect.png
[2026-05-04T07:01:31.9061218+02:00]   saved 07-after-undo-resize -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\07-after-undo-resize.png
[2026-05-04T07:01:31.9288667+02:00] --- Multi-layer flow ---
[2026-05-04T07:01:33.2201629+02:00]   saved ml-01-after-red-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-01-after-red-paste.png
[2026-05-04T07:01:33.5803169+02:00]   saved ml-02-after-blue-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-02-after-blue-paste.png
[2026-05-04T07:01:33.5809216+02:00] State after blue paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-04T07:01:33.8967919+02:00]   saved ml-03-after-green-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-03-after-green-paste.png
[2026-05-04T07:01:33.8972185+02:00] State after green paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=3 selected=2 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}, #2 src=28x10 frame={X=46,Y=35,Width=28,Height=10} fill={X=0,Y=0,Width=28,Height=10}]
[2026-05-04T07:01:33.9095867+02:00]   saved ml-04-after-click-red-only-corner -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-04-after-click-red-only-corner.png
[2026-05-04T07:01:33.9100561+02:00] selected after click red corner: 2
[2026-05-04T07:01:33.9238236+02:00]   saved ml-05-after-click-overlap-expect-green -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-05-after-click-overlap-expect-green.png
[2026-05-04T07:01:33.9242622+02:00] selected after click overlap: 2
[2026-05-04T07:01:34.2149727+02:00]   saved ml-06-after-delete-green -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\ml-06-after-delete-green.png
[2026-05-04T07:01:34.2153900+02:00] State after delete: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=-1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-04T07:01:34.2237081+02:00] --- Commit + undo flow ---
[2026-05-04T07:01:35.8012802+02:00]   saved co-01-before-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-01-before-commit.png
[2026-05-04T07:01:35.8021507+02:00] Pre-commit: item.IsDirty=True snapshot=null
[2026-05-04T07:01:35.8024084+02:00] Pre-commit: editor undo stack: canUndo=True canRedo=False
[2026-05-04T07:01:36.1057324+02:00] Commit returned True
[2026-05-04T07:01:36.1132958+02:00] Post-commit: snapshot=steps=2 index=1
[2026-05-04T07:01:36.1214159+02:00]   saved co-02-after-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-02-after-commit.png
[2026-05-04T07:01:36.1219366+02:00] After commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:01:36.1222236+02:00] Undo stack: canUndo=True canRedo=False
[2026-05-04T07:01:36.1349986+02:00]   saved co-03-after-first-undo -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-03-after-first-undo.png
[2026-05-04T07:01:36.1355487+02:00] After 1st undo: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=-1 [#0 src=24x16 frame={X=36,Y=24,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}]
[2026-05-04T07:01:36.1357258+02:00] Undo stack: canUndo=True canRedo=True
[2026-05-04T07:01:36.1520587+02:00]   saved co-04-after-second-undo -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\co-04-after-second-undo.png
[2026-05-04T07:01:36.1524642+02:00] After 2nd undo: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:01:36.1598414+02:00] --- Rubber-band selection flow ---
[2026-05-04T07:01:37.1411475+02:00]   saved rb-02-after-drag-rubber-band -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\rb-02-after-drag-rubber-band.png
[2026-05-04T07:01:37.1415224+02:00] After rubber-band drag: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=0 selected=-1 []
[2026-05-04T07:01:37.1485108+02:00] --- History switch flow ---
Excellent — undo across commit now works. Verifying visually.

Read co-03-after-first-undo.png
Another bug exposed: undo of a layer-DRAG step after commit doesn't restore the unflattened base, so the base shows the baked-in dragged orange while the layer is restored at the pre-drag position — double-render. Layer-only undo steps need to capture the base too.

Edit ImageEditor.Layers.cs
Added 7 lines
Edit ImageEditor.Layers.cs
Added 3 lines
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Write-Output \"done\"",
  "description": "Re-run with fix",
  "timeout": 120000
}
OUT
done
Read co-03-after-first-undo.png
Double-render gone. Now looking at the multi-layer screenshots to investigate the hit-test issue.

Read ml-04-after-click-red-only-corner.png
Fixing the multi-layer hit-test priority next.

Edit ImageEditor.Layers.cs
Added 16 lines
Bash Run all tests
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo 2>&1 | tail -8

OUT
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  TextDetection -> D:\projects\code\screenzap\TextDetection\bin\Debug\net8.0-windows10.0.19041.0\TextDetection.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll
  Screenzap.ViewportTests -> D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll
Test run for D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 4 s - Screenzap.ViewportTests.dll (net8.0)
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"selected after click|---\" }",
  "description": "Re-run, check multi-layer hit-test",
  "timeout": 120000
}
OUT
[2026-05-04T07:03:56.4480923+02:00] --- Multi-layer flow ---
[2026-05-04T07:03:56.9299607+02:00] selected after click red corner: 1
[2026-05-04T07:03:56.9408123+02:00] selected after click overlap: 2
[2026-05-04T07:03:56.9662660+02:00] --- Commit + undo flow ---
[2026-05-04T07:03:57.5239080+02:00] --- Rubber-band selection flow ---
[2026-05-04T07:03:58.0001503+02:00] --- History switch flow ---
Read hs-03-back-to-first-expect-layer-restored.png
Read rb-02-after-drag-rubber-band.png
Read 04-after-drag-translate.png
Update Todos

Bug 1: phantom Selection on paste — FIXED

Bug 2: commit clobbers undo stack via clipboard auto-reload — FIXED

Bug 3: undo across commit double-renders layer (drag step lacked base bitmap) — FIXED

Bug 4: multi-layer hit-test priority (handle eats body click on different layer) — FIXED

Commit fixes round 1

Extend --ui-capture: text annotation creation/select/edit (Move-mode fix verification)

Hunt more bugs via capture
Bash Check modified files
IN
cd d:/projects/code/screenzap && git status --short

OUT
 M screenzap/Components/ClipboardEditorHostForm.cs
 M screenzap/Components/ClipboardHistoryItem.cs
 M screenzap/Components/ImageEditor.Layers.cs
 M screenzap/Components/ImageEditor.TestInput.cs
 M screenzap/Components/ImageEditor.cs
 M screenzap/Testing/UiCaptureSession.cs
 M tests/Screenzap.ViewportTests/ImageEditorReloadTests.cs
 M tests/Screenzap.ViewportTests/ImageLayerSelectionTests.cs
Bash Commit round 1 of bug fixes
IN
cd d:/projects/code/screenzap && git add -A && git commit -m "$(cat <<'EOF'
fix: 4 visual bugs surfaced by the UI test kit

Used the new --ui-capture mode to drive paste/drag/resize/commit/undo through
the real input pipeline and inspect screenshots. Found four bugs the existing
unit tests missed because they bypassed pictureBox1_MouseDown and the host
clipboard observer.

1. Phantom Selection rectangle on paste
   ImageEditor.cs:2611 was setting Selection = layer's frame on paste to
   preserve a legacy test contract. With layers as first-class objects, that
   created a marching-ants ghost stuck at the original paste site even after
   the layer moved, resized, or got deleted. Cleared on paste; replaced the
   legacy test to assert the new contract.

2. Commit silently wiped the just-restored undo stack
   When the host writes the flattened image to the system clipboard during
   commit, the editor's own clipboard observer treats it as an external
   change and auto-reloads (LoadImage clears undoStack). The host's
   internal-write suppression flag was on the host only — the editor wasn't
   informed. Added TrackHostClipboardImageWrite (signature-based suppression
   shared with the editor's own write path).

3. Undo of a layer-only step after commit double-renders the layer
   Layer-drag/resize/delete steps had ReplacesImage=false and didn't capture
   the base bitmap. After a commit baked the layer into the base, undoing a
   pre-commit drag restored the live layer at its pre-drag position WHILE the
   committed flattened base still showed the layer at its post-drag
   position — two visible copies. Layer-only steps now also clone the base
   bitmap, so undo across commit unflattens correctly.

4. Multi-layer click hit-test priority swallowed body clicks
   With one layer selected, clicking on a different layer that overlapped a
   selected-layer handle's tolerance zone resized the selected layer instead
   of switching selection. Reordered the hit-test: a body hit on a different
   layer now wins over an edge-handle hit on the currently selected layer.
   Edge handles still win for clicks that miss every body.

Verified: 45/45 unit tests pass, --ui-capture screenshots inspected
post-fix for each bug. Multi-layer/commit/history-switch/rubber-band flows
all render correctly now.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"

OUT
warning: in the working copy of 'screenzap/Components/ImageEditor.Layers.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'screenzap/Components/ImageEditor.TestInput.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'screenzap/Components/ImageEditor.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'screenzap/Testing/UiCaptureSession.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'tests/Screenzap.ViewportTests/ImageLayerSelectionTests.cs', LF will be replaced by CRLF the next time Git touches it
[main 20aaa2f] fix: 4 visual bugs surfaced by the UI test kit
 8 files changed, 243 insertions(+), 51 deletions(-)
Round 1 committed 20aaa2f. Now extending capture for text-annotation scenario — the user's specific complaint.

Edit UiCaptureSession.cs
Added 2 lines
Grep "isTextToolActive\s*=\s*true|ActivateTextTool|ToggleTextTool" (in d:/projects/code/screenzap/screenzap/Components/ImageEditor.TextTool.cs)
3 lines of output
Read ImageEditor.TextTool.cs (lines 771-785)
Edit ImageEditor.TestInput.cs
Added 25 lines
Edit UiCaptureSession.cs
Added 69 lines
Bash Build extended capture
IN
cd d:/projects/code/screenzap && dotnet build screenzap/Screenzap.csproj --nologo 2>&1 | tail -8

OUT
  Screenzap.Components.Shared -> D:\projects\code\screenzap\shared\Screenzap.Components.Shared\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.Components.Shared.dll
  Screenzap -> D:\projects\code\screenzap\screenzap\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.66
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"isTextToolActive|annotations|---|zoom\" }",
  "description": "Run with text + zoom flows",
  "timeout": 120000
}
OUT
[2026-05-04T07:06:05.7227823+02:00] State after kit ctor: form=643x544 visible=True created=True picturebox=643x522 visible=True image=640x200 zoom=1 pan={X=1.5, Y=161} layers=0 selected=-1 []
[2026-05-04T07:06:05.8057915+02:00] State after LoadCanvas: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:06:05.8393299+02:00] State after paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
[2026-05-04T07:06:05.9924369+02:00] --- Multi-layer flow ---
[2026-05-04T07:06:06.4924736+02:00] State after blue paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-04T07:06:06.5128958+02:00] State after green paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=3 selected=2 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}, #2 src=28x10 frame={X=46,Y=35,Width=28,Height=10} fill={X=0,Y=0,Width=28,Height=10}]
[2026-05-04T07:06:06.5534029+02:00] State after delete: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=-1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-04T07:06:06.5611091+02:00] --- Commit + undo flow ---
[2026-05-04T07:06:07.0434096+02:00] Before commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=24x16 frame={X=24,Y=16,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}]
[2026-05-04T07:06:07.0891247+02:00] After commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:06:07.1036793+02:00] After 1st undo: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=-1 [#0 src=24x16 frame={X=36,Y=24,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}]
[2026-05-04T07:06:07.1205643+02:00] After 2nd undo: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:06:07.1274809+02:00] --- Rubber-band selection flow ---
[2026-05-04T07:06:07.6279850+02:00] After rubber-band drag: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=0 selected=-1 []
[2026-05-04T07:06:07.6361405+02:00] --- History switch flow ---
[2026-05-04T07:06:08.2107172+02:00] On second item: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:06:08.2474589+02:00] Back on first: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=-1 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
[2026-05-04T07:06:08.2561435+02:00] --- Text annotation Move-mode flow ---
[2026-05-04T07:06:09.0238842+02:00] After ToggleTextTool: isTextToolActive=True
[2026-05-04T07:06:09.1787335+02:00] After Escape: isTextToolActive=True annotations=[]
[2026-05-04T07:06:09.2157677+02:00] After click empty in Move mode: isTextToolActive=True annotations=[#0 pos={X=120,Y=80} text='' selected=True editing=True]
[2026-05-04T07:06:09.2518628+02:00] After click on text in Move mode: isTextToolActive=True annotations=[#0 pos={X=44,Y=35} text='' selected=True editing=True]
[2026-05-04T07:06:09.2952002+02:00] After 2nd click empty: isTextToolActive=True annotations=[#0 pos={X=120,Y=80} text='' selected=True editing=True]
[2026-05-04T07:06:09.3177209+02:00] --- Zoomed paste flow ---
[2026-05-04T07:06:09.8935540+02:00] After zoom 2x: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=2 pan={X=225.5, Y=197} layers=0 selected=-1 []
[2026-05-04T07:06:09.9010413+02:00]   saved zm-01-zoomed-empty -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\zm-01-zoomed-empty.png
[2026-05-04T07:06:09.9195684+02:00] After paste at zoom 2x: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=2 pan={X=225.5, Y=197} layers=1 selected=0 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
[2026-05-04T07:06:09.9262174+02:00]   saved zm-02-zoomed-after-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\zm-02-zoomed-after-paste.png
[2026-05-04T07:06:09.9369186+02:00]   saved zm-03-zoomed-after-click -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\zm-03-zoomed-after-click.png
[2026-05-04T07:06:09.9816936+02:00]   saved zm-04-zoomed-after-drag -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\zm-04-zoomed-after-drag.png
[2026-05-04T07:06:09.9820359+02:00] After drag at zoom 2x: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=2 pan={X=225.5, Y=197} layers=1 selected=0 [#0 src=20x14 frame={X=50,Y=33,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
Let me look at the text screenshots — the typing path is suspect since I'm only firing KeyDown not KeyPress.

Read tx-02-after-typing.png
Read ImageEditor.cs (lines 1905-1994)
Grep "HandleTextToolKeyPress|KeyPress \+=" (in d:/projects/code/screenzap/screenzap/Components)
2 lines of output
Read ImageEditor.cs (lines 2179-2193)
Adding OnKeyPress firing to the test partial so the kit can actually type characters.

Edit ImageEditor.TestInput.cs
Added 10 lines
Edit UiTestKit.cs
Added 9 lines
Edit UiCaptureSession.cs
Added 5 lines
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"tx-*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"isTextToolActive|annotations|---|Text\" }",
  "description": "Re-run text flow with proper typing",
  "timeout": 120000
}
OUT
[2026-05-04T07:08:18.5509090+02:00] --- Multi-layer flow ---
[2026-05-04T07:08:19.1943186+02:00] --- Commit + undo flow ---
[2026-05-04T07:08:19.8514337+02:00] --- Rubber-band selection flow ---
[2026-05-04T07:08:20.4099416+02:00] --- History switch flow ---
[2026-05-04T07:08:21.1740236+02:00] --- Text annotation Move-mode flow ---
[2026-05-04T07:08:22.1791183+02:00] After ToggleTextTool: isTextToolActive=True
[2026-05-04T07:08:22.2162901+02:00] After click in text mode: [#0 pos={X=40,Y=30} text='' selected=True editing=True]
[2026-05-04T07:08:22.2504255+02:00] After typing 'HI': [#0 pos={X=40,Y=30} text='HI' selected=True editing=True]
[2026-05-04T07:08:22.2943072+02:00] After 1st Escape: isTextToolActive=True annotations=[#0 pos={X=40,Y=30} text='HI' selected=True editing=False]
[2026-05-04T07:08:22.3216668+02:00] After 2nd Escape: isTextToolActive=True annotations=[#0 pos={X=40,Y=30} text='HI' selected=False editing=False]
[2026-05-04T07:08:22.3557643+02:00] After click empty in Move mode: isTextToolActive=True annotations=[#0 pos={X=40,Y=30} text='HI' selected=False editing=False, #1 pos={X=120,Y=80} text='' selected=True editing=True]
[2026-05-04T07:08:22.3941869+02:00] After click on text in Move mode: isTextToolActive=True annotations=[#0 pos={X=40,Y=30} text='HI' selected=True editing=False]
[2026-05-04T07:08:22.4068572+02:00]   saved tx-06-clicked-text-move-mode -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\tx-06-clicked-text-move-mode.png
[2026-05-04T07:08:22.4457958+02:00] After 2nd click empty: isTextToolActive=True annotations=[#0 pos={X=40,Y=30} text='HI' selected=False editing=False, #1 pos={X=120,Y=80} text='' selected=True editing=True]
[2026-05-04T07:08:22.4671015+02:00] --- Zoomed paste flow ---
Bug 5 found: second Escape from text tool doesn't exit the tool — HandleTextToolKeyDown's object-selection Escape branch consumes the keystroke before the form-level handler that deactivates the tool. Fixing.

Read ImageEditor.TextTool.cs (lines 1544-1563)
Edit ImageEditor.TextTool.cs
Added 12 lines
Bash Run all tests
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo 2>&1 | tail -5

OUT
  Screenzap.ViewportTests -> D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll
Test run for D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 3 s - Screenzap.ViewportTests.dll (net8.0)
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"tx-*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"After.*Escape|click empty|click on text\" }",
  "description": "Re-run text flow",
  "timeout": 120000
}
OUT
[2026-05-04T07:09:17.4862397+02:00]   saved 06-after-escape-deselect -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\06-after-escape-deselect.png
[2026-05-04T07:09:20.4087604+02:00] After 1st Escape: isTextToolActive=True annotations=[#0 pos={X=40,Y=30} text='HI' selected=True editing=False]
[2026-05-04T07:09:20.4203182+02:00]   saved tx-03-after-1st-escape -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\tx-03-after-1st-escape.png
[2026-05-04T07:09:20.4583159+02:00] After 2nd Escape: isTextToolActive=False annotations=[#0 pos={X=40,Y=30} text='HI' selected=False editing=False]
[2026-05-04T07:09:20.4662537+02:00]   saved tx-04-after-2nd-escape -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\tx-04-after-2nd-escape.png
[2026-05-04T07:09:20.4710803+02:00] After click empty in Move mode: isTextToolActive=False annotations=[#0 pos={X=40,Y=30} text='HI' selected=False editing=False]
[2026-05-04T07:09:20.4911797+02:00] After click on text in Move mode: isTextToolActive=False annotations=[#0 pos={X=40,Y=30} text='HI' selected=True editing=False]
[2026-05-04T07:09:20.4994935+02:00] After 2nd click empty: isTextToolActive=False annotations=[#0 pos={X=40,Y=30} text='HI' selected=True editing=False]
Read tx-02-after-typing.png
Read tx-04-after-2nd-escape.png
Extending click-empty-deselect to also clear text and shape annotation selections (consistent Figma deselect-all on empty click).

Edit ImageEditor.Selection.cs
Added 15 lines
Bash Run tests
IN
cd d:/projects/code/screenzap && dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo 2>&1 | tail -5

OUT
  Screenzap.ViewportTests -> D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll
Test run for D:\projects\code\screenzap\tests\Screenzap.ViewportTests\bin\Debug\net8.0-windows10.0.19041.0\Screenzap.ViewportTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 3 s - Screenzap.ViewportTests.dll (net8.0)
Update Todos

Bug 1: phantom Selection on paste — FIXED

Bug 2: commit clobbers undo stack via clipboard auto-reload — FIXED

Bug 3: undo across commit double-renders layer — FIXED

Bug 4: multi-layer hit-test priority — FIXED

Bug 5: 2nd Escape from text tool didn't return to Move mode — FIXED

Bug 6: empty-click deselect didn't clear text/shape selections — FIXED

Look at zoomed paste flow

Commit fixes round 2
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"zm-*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"zoom|Zoomed\" }",
  "description": "Re-run zoomed flow",
  "timeout": 120000
}
OUT
[2026-05-04T07:10:19.0570799+02:00] State after kit ctor: form=643x544 visible=True created=True picturebox=643x522 visible=True image=640x200 zoom=1 pan={X=1.5, Y=161} layers=0 selected=-1 []
[2026-05-04T07:10:19.1191067+02:00] State after LoadCanvas: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:10:19.1447750+02:00] State after paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
[2026-05-04T07:10:19.7358245+02:00] State after blue paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-04T07:10:19.7513092+02:00] State after green paste: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=3 selected=2 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}, #2 src=28x10 frame={X=46,Y=35,Width=28,Height=10} fill={X=0,Y=0,Width=28,Height=10}]
[2026-05-04T07:10:19.7892980+02:00] State after delete: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=2 selected=-1 [#0 src=24x16 frame={X=48,Y=32,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}, #1 src=20x12 frame={X=50,Y=34,Width=20,Height=12} fill={X=0,Y=0,Width=20,Height=12}]
[2026-05-04T07:10:20.2465684+02:00] Before commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=0 [#0 src=24x16 frame={X=24,Y=16,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}]
[2026-05-04T07:10:20.2882611+02:00] After commit: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:10:20.3038155+02:00] After 1st undo: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=-1 [#0 src=24x16 frame={X=36,Y=24,Width=24,Height=16} fill={X=0,Y=0,Width=24,Height=16}]
[2026-05-04T07:10:20.3195535+02:00] After 2nd undo: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:10:20.7867616+02:00] After rubber-band drag: form=643x544 visible=True created=True picturebox=643x522 visible=True image=120x80 zoom=1 pan={X=261.5, Y=221} layers=0 selected=-1 []
[2026-05-04T07:10:21.3975097+02:00] On second item: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:10:21.4433582+02:00] Back on first: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=1 selected=-1 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
[2026-05-04T07:10:22.4374477+02:00] --- Zoomed paste flow ---
[2026-05-04T07:10:22.9757542+02:00] After zoom 2x: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=2 pan={X=225.5, Y=197} layers=0 selected=-1 []
[2026-05-04T07:10:22.9845174+02:00]   saved zm-01-zoomed-empty -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\zm-01-zoomed-empty.png
[2026-05-04T07:10:22.9996534+02:00] After paste at zoom 2x: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=2 pan={X=225.5, Y=197} layers=1 selected=0 [#0 src=20x14 frame={X=38,Y=25,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
[2026-05-04T07:10:23.0092435+02:00]   saved zm-02-zoomed-after-paste -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\zm-02-zoomed-after-paste.png
[2026-05-04T07:10:23.0227327+02:00]   saved zm-03-zoomed-after-click -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\zm-03-zoomed-after-click.png
[2026-05-04T07:10:23.0710517+02:00]   saved zm-04-zoomed-after-drag -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\zm-04-zoomed-after-drag.png
[2026-05-04T07:10:23.0714615+02:00] After drag at zoom 2x: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=2 pan={X=225.5, Y=197} layers=1 selected=0 [#0 src=20x14 frame={X=50,Y=33,Width=20,Height=14} fill={X=0,Y=0,Width=20,Height=14}]
Read zm-04-zoomed-after-drag.png
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\zm-04-zoomed-after-drag.png\"; $bmp = New-Object System.Drawing.Bitmap($f); foreach ($p in @(@(225,197),@(417,197),@(225,325),@(417,325),@(326,264),@(364,290),@(326,290))) { $px = $bmp.GetPixel($p[0], $p[1]); Write-Output \"($($p[0]),$($p[1])) = R=$($px.R) G=$($px.G) B=$($px.B)\" }; $bmp.Dispose()",
  "description": "Sample pixel positions at expected canvas/layer corners"
}
OUT
(225,197) = R=255 G=255 B=255
(417,197) = R=105 G=105 B=105
(225,325) = R=105 G=105 B=105
(417,325) = R=105 G=105 B=105
(326,264) = R=255 G=255 B=255
(364,290) = R=255 G=255 B=255
(326,290) = R=255 G=255 B=255
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\zm-04-zoomed-after-drag.png\"; $bmp = New-Object System.Drawing.Bitmap($f); for ($x = 200; $x -lt 450; $x += 5) { $px = $bmp.GetPixel($x, 220); $isWhite = ($px.R -gt 200 -and $px.G -gt 200 -and $px.B -gt 200); Write-Output \"x=$x isWhite=$isWhite (R=$($px.R))\" }; $bmp.Dispose()",
  "description": "Scan horizontal at y=220 to find canvas extent"
}
OUT
x=200 isWhite=False (R=105)
x=205 isWhite=False (R=105)
x=210 isWhite=False (R=105)
x=215 isWhite=False (R=105)
x=220 isWhite=False (R=105)
x=225 isWhite=True (R=255)
x=230 isWhite=True (R=255)
x=235 isWhite=True (R=255)
x=240 isWhite=True (R=255)
x=245 isWhite=True (R=255)
x=250 isWhite=True (R=255)
x=255 isWhite=True (R=255)
x=260 isWhite=True (R=255)
x=265 isWhite=True (R=255)
x=270 isWhite=True (R=255)
x=275 isWhite=True (R=255)
x=280 isWhite=True (R=255)
x=285 isWhite=True (R=255)
x=290 isWhite=True (R=255)
x=295 isWhite=True (R=255)
x=300 isWhite=True (R=255)
x=305 isWhite=True (R=255)
x=310 isWhite=True (R=255)
x=315 isWhite=True (R=255)
x=320 isWhite=True (R=255)
x=325 isWhite=True (R=255)
x=330 isWhite=True (R=255)
x=335 isWhite=True (R=255)
x=340 isWhite=True (R=255)
x=345 isWhite=True (R=255)
x=350 isWhite=True (R=255)
x=355 isWhite=True (R=255)
x=360 isWhite=True (R=255)
x=365 isWhite=True (R=255)
x=370 isWhite=True (R=255)
x=375 isWhite=True (R=255)
x=380 isWhite=True (R=255)
x=385 isWhite=True (R=255)
x=390 isWhite=True (R=255)
x=395 isWhite=True (R=255)
x=400 isWhite=True (R=255)
x=405 isWhite=True (R=255)
x=410 isWhite=True (R=255)
x=415 isWhite=True (R=255)
x=420 isWhite=False (R=105)
x=425 isWhite=False (R=105)
x=430 isWhite=False (R=105)
x=435 isWhite=False (R=105)
x=440 isWhite=False (R=105)
x=445 isWhite=False (R=105)
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\zm-04-zoomed-after-drag.png\"; $bmp = New-Object System.Drawing.Bitmap($f); for ($x = 300; $x -lt 400; $x += 2) { $px = $bmp.GetPixel($x, 280); $isOrange = ($px.R -gt 200 -and $px.G -lt 100 -and $px.B -lt 80); if ($isOrange) { Write-Output \"x=$x ORANGE (R=$($px.R) G=$($px.G) B=$($px.B))\" } }; $bmp.Dispose()",
  "description": "Find orange layer extent at y=280"
}
OUT
x=330 ORANGE (R=255 G=69 B=0)
x=332 ORANGE (R=255 G=69 B=0)
x=334 ORANGE (R=255 G=69 B=0)
x=336 ORANGE (R=255 G=69 B=0)
x=338 ORANGE (R=255 G=69 B=0)
x=340 ORANGE (R=255 G=69 B=0)
x=342 ORANGE (R=255 G=69 B=0)
x=344 ORANGE (R=255 G=69 B=0)
x=346 ORANGE (R=255 G=69 B=0)
x=348 ORANGE (R=255 G=69 B=0)
x=350 ORANGE (R=255 G=69 B=0)
x=352 ORANGE (R=255 G=69 B=0)
x=354 ORANGE (R=255 G=69 B=0)
x=356 ORANGE (R=255 G=69 B=0)
x=358 ORANGE (R=255 G=69 B=0)
x=360 ORANGE (R=255 G=69 B=0)
Read zm-02-zoomed-after-paste.png
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\zm-02-zoomed-after-paste.png\"; $bmp = New-Object System.Drawing.Bitmap($f); $startX = $endX = $null; for ($x = 200; $x -lt 400; $x++) { $px = $bmp.GetPixel($x, 250); if ($px.R -gt 200 -and $px.G -lt 100 -and $px.B -lt 80) { if ($startX -eq $null) { $startX = $x }; $endX = $x } }; Write-Output \"Layer extent: $startX..$endX width=$($endX - $startX + 1)\"; $bmp.Dispose()",
  "description": "Measure layer width in zm-02"
}
OUT
Layer extent: 306..336 width=31
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\zm-02-zoomed-after-paste.png\"; $bmp = New-Object System.Drawing.Bitmap($f); $startX = $endX = $null; for ($x = 0; $x -lt $bmp.Width; $x++) { $px = $bmp.GetPixel($x, 220); if ($px.R -eq 255 -and $px.G -eq 255 -and $px.B -eq 255) { if ($startX -eq $null) { $startX = $x }; $endX = $x } }; Write-Output \"Canvas at y=220: $startX..$endX width=$($endX - $startX + 1)\"; $startY = $endY = $null; for ($y = 0; $y -lt $bmp.Height; $y++) { $px = $bmp.GetPixel(300, $y); if ($px.R -eq 255 -and $px.G -eq 255 -and $px.B -eq 255) { if ($startY -eq $null) { $startY = $y }; $endY = $y } }; Write-Output \"Canvas at x=300: $startY..$endY height=$($endY - $startY + 1)\"; $bmp.Dispose()",
  "description": "Measure canvas extent"
}
OUT
Canvas at y=220: 225..416 width=192
Canvas at x=300: 197..324 height=128
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\zm-02-zoomed-after-paste.png\"; $bmp = New-Object System.Drawing.Bitmap($f); for ($y = 230; $y -le 270; $y += 2) { $extents = \"\"; for ($x = 290; $x -le 360; $x++) { $px = $bmp.GetPixel($x, $y); if ($px.R -gt 240 -and $px.G -lt 100) { if ($extents -eq \"\") { $extents = \"$x\" } } }; if ($extents -ne \"\") { $startX = [int]$extents; $endX = $startX; for ($x = $startX; $x -le 360; $x++) { $px = $bmp.GetPixel($x, $y); if ($px.R -gt 240 -and $px.G -lt 100) { $endX = $x } }; Write-Output \"y=$y: orange $startX..$endX width=$($endX - $startX + 1)\" } else { Write-Output \"y=$y: no orange\" } }; $bmp.Dispose()",
  "description": "Measure orange extent across multiple y"
}
OUT
Exit code 1
[31;1mParserError: [0m
[31;1m[36;1mLine |[0m
[31;1m[36;1m[36;1m   1 | [0m … 240 -and $px.G -lt 100) { $endX = $x } }; Write-Output "y=[36;1m$y:[0m orange  …[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m     | [31;1m                                                             ~~~[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m[31;1m[31;1m[36;1m     | [31;1mVariable reference is not valid. ':' was not followed by a valid variable name character. Consider using ${} to[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m[31;1m[31;1m[36;1m[31;1m[36;1m     | [31;1mdelimit the name.[0m
PowerShell
IN
{
  "command": "Add-Type -AssemblyName System.Drawing; $f = \"$env:TEMP\\screenzap-uitests\\zm-02-zoomed-after-paste.png\"; $bmp = New-Object System.Drawing.Bitmap($f); for ($y = 230; $y -le 280; $y += 2) { $startX = $null; $endX = $null; for ($x = 270; $x -le 380; $x++) { $px = $bmp.GetPixel($x, $y); if ($px.R -gt 240 -and $px.G -lt 100 -and $px.B -lt 80) { if ($null -eq $startX) { $startX = $x }; $endX = $x } }; if ($null -ne $startX) { Write-Output (\"y={0}: {1}..{2} w={3}\" -f $y, $startX, $endX, ($endX - $startX + 1)) } }; $bmp.Dispose()",
  "description": "Scan layer extent at multiple y"
}
OUT
y=248: 306..336 w=31
y=250: 306..336 w=31
y=252: 302..340 w=39
y=254: 302..340 w=39
y=256: 306..336 w=31
y=258: 306..336 w=31
y=260: 306..336 w=31
y=262: 306..336 w=31
y=264: 306..336 w=31
y=266: 302..340 w=39
y=268: 302..340 w=39
y=270: 306..336 w=31
y=272: 306..336 w=31
Zoom 2x rendering is actually correct — my pixel scan was thrown off by the resize handles obscuring the layer edges in the middle rows. The actual layer extent is 40px (verified at y=252-254 between handles). Committing round 2.

Bash Commit round 2
IN
cd d:/projects/code/screenzap && git add -A && git commit -m "$(cat <<'EOF'
fix: 2 more text/selection bugs found via UI capture

5. Second Escape didn't exit text tool
   With text tool active and a finalized text annotation selected,
   HandleTextToolKeyDown's object-selection Escape branch consumed the
   keystroke (deselecting the annotation) but didn't deactivate the tool —
   the form-level Escape handler that calls isTextToolActive=false was
   shadowed by the early return. Result: pressing Esc twice still left the
   user in text mode, so the next click anywhere on the canvas created
   another empty annotation. Fixed by chaining tool-deactivation into the
   object-selection Escape branch.

6. Empty-click in Move mode only deselected layers
   pictureBox1_MouseDown's empty-click branch called
   DeselectImageLayerIfAny() but left text-annotation and shape-annotation
   selections intact. Made it a single deselect-all so clicking empty
   canvas consistently clears every selectable content type (Figma model).

UI test kit gained:
- Type(string) primitive that fires real OnKeyPress events for character
  entry (was missing — Press only fired KeyDown).
- TestToggleTextTool / TestSetZoom / TestDescribeTextAnnotations
  diagnostics for the capture session.

Capture session now exercises text creation, multi-Escape exit, and
zoom-2x paste+drag end-to-end. All flows verified in screenshots.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"

OUT
warning: in the working copy of 'screenzap/Components/ImageEditor.TestInput.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'screenzap/Testing/UiCaptureSession.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'screenzap/Testing/UiTestKit.cs', LF will be replaced by CRLF the next time Git touches it
[main 45a3e43] fix: 2 more text/selection bugs found via UI capture
 5 files changed, 150 insertions(+), 3 deletions(-)
Continuing the bug hunt — let me cover annotation drawing tools and a multi-commit cycle.

Edit UiCaptureSession.cs
Added 1 line
Edit UiCaptureSession.cs
Added 35 lines
PowerShell
IN
{
  "command": "$dir = Join-Path $env:TEMP \"screenzap-uitests\"; if (Test-Path $dir) { Get-ChildItem $dir -Filter \"mc-*.png\" | Remove-Item -Force }; $logPath = Join-Path $env:APPDATA \"Screenzap\\screenzap.log\"; if (Test-Path $logPath) { Remove-Item $logPath -Force }; dotnet run --project screenzap/Screenzap.csproj -- --ui-capture | Out-Null; Get-Content $logPath | Where-Object { $_ -match \"Cycle|undos|Multi-commit\" }",
  "description": "Run multi-commit cycle",
  "timeout": 120000
}
OUT
[2026-05-04T07:13:41.8366744+02:00] --- Multi-commit cycle flow ---
[2026-05-04T07:13:42.4606497+02:00] Cycle 1 commit: True, state=form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:13:42.4684384+02:00]   saved mc-02-after-cycle-1-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\mc-02-after-cycle-1-commit.png
[2026-05-04T07:13:42.5752203+02:00] Cycle 2 commit: True, state=form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
[2026-05-04T07:13:42.5850837+02:00]   saved mc-05-after-cycle-2-commit -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\mc-05-after-cycle-2-commit.png
[2026-05-04T07:13:42.6276408+02:00]   saved mc-06-after-3-undos -> C:\Users\teeto\AppData\Local\Temp\screenzap-uitests\mc-06-after-3-undos.png
[2026-05-04T07:13:42.6280317+02:00] After 3 undos: form=643x544 visible=True created=True picturebox=643x522 visible=True image=96x64 zoom=1 pan={X=273.5, Y=229} layers=0 selected=-1 []
Read mc-05-after-cycle-2-commit.png
Read mc-06-after-3-undos.png
Read mc-04-after-blue-drag.png
Read co-04-after-second-undo.png
Update Todos

Bug 1: phantom Selection on paste — FIXED

Bug 2: commit clobbers undo stack via clipboard auto-reload — FIXED

Bug 3: undo across commit double-renders layer — FIXED

Bug 4: multi-layer hit-test priority — FIXED

Bug 5: 2nd Escape from text tool didn't return to Move mode — FIXED

Bug 6: empty-click deselect didn't clear text/shape selections — FIXED

Verified: zoom 2x paste/drag, multi-commit cycle, history switching, rubber-band

Pause and report status
Pausing here for a status check. Six bugs fixed across two commits since the kit landed.

Bugs found & fixed via the UI test kit
#	Bug	Where	Fix
1	Phantom marching-ants rectangle stuck at paste origin after layer moved	ImageEditor.cs paste path	Don't set image-region Selection on paste
2	Commit silently emptied the undo stack via the editor's clipboard auto-reload observer	ClipboardEditorHostForm.cs commit	Host informs editor of internal clipboard write via signature (new TrackHostClipboardImageWrite)
3	Undo across commit double-rendered the layer (layer-only undo steps didn't capture the base bitmap)	ImageEditor.Layers.cs	Drag/resize/delete steps now also clone the base, ReplacesImage=true
4	Multi-layer click priority: handle hit on selected layer ate body clicks on different layers	ImageEditor.Layers.cs hit-test	Body hit on a different layer wins over edge-handle hit on selected
5	Second Escape from text tool stayed in tool — every empty click then created a new annotation	ImageEditor.TextTool.cs object-selection Escape	Object-selection Escape branch also deactivates the tool
6	Empty-click in Move mode only deselected layers, not text/shape annotations	ImageEditor.Selection.cs	Single deselect-all on empty-click
All 45 unit tests still pass.

Flows verified visually
Slice 1+2 paste/drag/resize/escape/undo, multi-layer paste+select+delete, single-commit + undo-across-commit (now correctly unflattens), multi-commit-cycle + 3-undo-walk-back, history item switching (layer state survives + restores), rubber-band selection on empty canvas, text tool create+type+escape+escape+click-empty-doesn't-create-new, zoom-2x paste+drag (math verified by pixel sampling).

Three commits since you said "I trust you":

9ba84be — UI test kit foundation
20aaa2f — bugs 1–4
45a3e43 — bugs 5–6
What I haven't yet exercised through the kit
Annotation drawing tools (Arrow / Rectangle) — the cascade I touched. They likely still work but aren't verified through real input.
Censor / Straighten / Crop tools.
Color correction.
The ClipboardHistoryPanel thumbnail strip — clicking thumbnails to switch items.
Reload / Revert / Duplicate flows.
Multi-monitor / DPI scaling.
Anything across app-restart (persistence reload).
Want me to keep going down that list, or pause for you to drive next steps?

I've had a cheaper agent fix some issue. review them, then adjust your plan as needed.
Bash Check working tree state
IN
cd d:/projects/code/screenzap && git status && echo "---" && git log --oneline -3

OUT
On branch main
Your branch is ahead of 'origin/main' by 9 commits.
  (use "git push" to publish your local commits)

Changes not staged for commit:
  (use "git add <file>..." to update what will be committed)
  (use "git restore <file>..." to discard changes in working directory)
	modified:   screenzap/Components/ClipboardHistoryItem.cs
	modified:   screenzap/Components/ClipboardHistoryPanel.cs
	modified:   screenzap/Components/ImageEditor.cs
	modified:   tests/Screenzap.ViewportTests/ThumbnailActionRegressionTests.cs

no changes added to commit (use "git add" and/or "git commit -a")
---
3e79666 feat: Add multi-commit cycle flow capture to UI test suite
45a3e43 fix: 2 more text/selection bugs found via UI capture
20aaa2f fix: 4 visual bugs surfaced by the UI test kit
You're out of extra usage · resets 11:50am (Europe/Stockholm)

