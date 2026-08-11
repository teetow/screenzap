using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// The editor form runs with KeyPreview, so ImageEditor_KeyDown sees every keystroke before
    /// the thing being typed into does. Any shortcut that answers a character-producing key and
    /// sets SuppressKeyPress kills the WM_CHAR behind it, and the character can never be typed —
    /// which is exactly how the bare-M transparency-grid toggle made "m" untypable in text
    /// annotations. These tests drive the real KeyDown → KeyPress sequence Windows produces.
    /// </summary>
    public class TextTypingShortcutConflictTests
    {
        private static screenzap.ImageEditor PrepareEditorInTextEditMode()
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(160, 120);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.White);
            }
            editor.LoadImage(canvas);
            canvas.Dispose();

            editor.TestToggleTextTool();
            editor.TestFireMouseDownAtImagePixel(new Point(30, 30), MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(new Point(30, 30), MouseButtons.Left);

            Assert.Equal(1, editor.TestTextAnnotationCount);
            Assert.Contains("editing=True", editor.TestDescribeTextAnnotations());
            return editor;
        }

        /// <summary>
        /// M is the transparency-grid shortcut. While a text annotation is being edited it is a
        /// letter: it must reach KeyPress unsuppressed, and must not toggle the grid.
        /// </summary>
        [Fact]
        public void TypingM_IntoTextAnnotation_InsertsTheLetter_AndLeavesTheGridAlone()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorInTextEditMode();
                bool gridBefore = editor.TestAlphaViewEnabled;

                Assert.False(editor.TestFireKeyDownSuppressed(Keys.M));
                Assert.True(editor.TestFireKeyPress('m'));

                Assert.Contains("text='m'", editor.TestDescribeTextAnnotations());
                Assert.Equal(gridBefore, editor.TestAlphaViewEnabled);
            });
        }

        /// <summary>
        /// Every letter has to behave the same way — the point is that no character-producing key
        /// reaches the document shortcuts while a text annotation is being edited.
        /// </summary>
        [Theory]
        [InlineData(Keys.A, 'a')]
        [InlineData(Keys.B, 'b')]
        [InlineData(Keys.E, 'e')]
        [InlineData(Keys.M, 'm')]
        [InlineData(Keys.S, 's')]
        [InlineData(Keys.Z, 'z')]
        public void TypingAnyLetter_IntoTextAnnotation_IsNeverSwallowedByAShortcut(Keys key, char expected)
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorInTextEditMode();

                Assert.False(editor.TestFireKeyDownSuppressed(key));
                Assert.True(editor.TestFireKeyPress(expected));

                Assert.Contains($"text='{expected}'", editor.TestDescribeTextAnnotations());
            });
        }

        /// <summary>
        /// AltGr reaches WinForms as Ctrl+Alt and still produces characters on international
        /// layouts, so it counts as typing rather than as a Ctrl shortcut. The motivating
        /// collision is Swedish AltGr+E (€) against Ctrl+E, but this test fires Ctrl+Alt+A
        /// instead: a regression on Ctrl+E would launch the censor tool's OCR pass and its
        /// MessageBox, blocking the test run rather than failing it. Ctrl+A (select all) fails
        /// visibly and synchronously.
        /// </summary>
        [Fact]
        public void AltGrCombo_IsTreatedAsTyping_NotAsACtrlShortcut()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorInTextEditMode();
                Assert.True(editor.SelectionDiagnostics.Selection.IsEmpty);

                Assert.False(editor.TestFireKeyDownSuppressed(Keys.Control | Keys.Alt | Keys.A));

                // Ctrl+A did not run: the marquee is untouched and the character lands in the text.
                Assert.True(editor.SelectionDiagnostics.Selection.IsEmpty);
                Assert.True(editor.TestFireKeyPress('€'));
                Assert.Contains("text='€'", editor.TestDescribeTextAnnotations());
            });
        }

        /// <summary>
        /// The flip side: a plain Ctrl combo produces no character, so it keeps working as an
        /// editor shortcut even while a text annotation is being edited.
        /// </summary>
        [Fact]
        public void PlainCtrlShortcut_StillReachesTheEditor_WhileTypingText()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorInTextEditMode();
                Assert.False(editor.TestIsStraightenToolActive);

                Assert.True(editor.TestFireKeyDownSuppressed(Keys.Control | Keys.L));
                Assert.True(editor.TestIsStraightenToolActive);
            });
        }

        /// <summary>
        /// The guard is scoped to typing: with no text being edited, M still toggles the grid.
        /// </summary>
        [Fact]
        public void M_StillTogglesTheGrid_WhenNoTextIsBeingEdited()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(160, 120);
                editor.LoadImage(canvas);

                bool before = editor.TestAlphaViewEnabled;
                Assert.True(editor.TestFireKeyDownSuppressed(Keys.M));
                Assert.NotEqual(before, editor.TestAlphaViewEnabled);
            });
        }
    }
}
