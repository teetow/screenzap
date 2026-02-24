using System;
using System.IO;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class TextEditorWorkflowTests
    {
        [Fact]
        public void TextEditor_AutomatesLoadFindReplaceReloadSaveAndCopyBack()
        {
            using var editor = new screenzap.TextEditor();
            editor.LoadText("alpha beta alpha");

            editor.ConfigureSearchForDiagnostics("alpha", "omega", regex: false, matchCase: false, wholeWord: false);
            Assert.True(editor.TryFindForDiagnostics(backwards: false, out var start, out var length));
            Assert.Equal(0, start);
            Assert.Equal(5, length);

            editor.ReplaceAllForDiagnostics();
            Assert.Equal("omega beta omega", editor.CurrentTextForDiagnostics);
            Assert.True(editor.IsDirtyForDiagnostics);

            string tempRoot = Path.Combine(Path.GetTempPath(), "screenzap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                string savePath = Path.Combine(tempRoot, "note.txt");
                Assert.True(editor.SaveDocumentForDiagnostics(savePath));
                Assert.True(File.Exists(savePath));
                Assert.Equal("omega beta omega", File.ReadAllText(savePath));
                Assert.False(editor.IsDirtyForDiagnostics);
                Assert.Equal(savePath, editor.CurrentSavePathForDiagnostics);

                string? copied = null;
                editor.ClipboardTextWriterForDiagnostics = text =>
                {
                    copied = text;
                    return true;
                };
                editor.CopyToClipboardForDiagnostics();
                Assert.Equal("omega beta omega", copied);
                Assert.Equal("omega beta omega", editor.LastCopiedTextForDiagnostics);

                editor.SetTextForDiagnostics("local edit");
                editor.SetPendingClipboardTextForDiagnostics("reloaded from clipboard", hasPendingReload: true);
                editor.ConfirmReloadWhenDirtyOverrideForDiagnostics = () => false;
                editor.ReloadFromClipboardForDiagnostics();
                Assert.Equal("local edit", editor.CurrentTextForDiagnostics);
                Assert.True(editor.ClipboardHasPendingReloadForDiagnostics);

                editor.ConfirmReloadWhenDirtyOverrideForDiagnostics = () => true;
                editor.ReloadFromClipboardForDiagnostics();
                Assert.Equal("reloaded from clipboard", editor.CurrentTextForDiagnostics);
                Assert.False(editor.IsDirtyForDiagnostics);
                Assert.False(editor.ClipboardHasPendingReloadForDiagnostics);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
