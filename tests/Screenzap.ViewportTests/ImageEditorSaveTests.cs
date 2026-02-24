using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageEditorSaveTests
    {
        [Fact]
        public void Save_CreatesDefaultPngPath_AndSubsequentSaveReusesSameFile()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "screenzap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string previousCaptureFolder = screenzap.Properties.Settings.Default.captureFolder;

            try
            {
                screenzap.Properties.Settings.Default.captureFolder = tempRoot;

                using var editor = new screenzap.ImageEditor();
                using var source = new Bitmap(32, 24);
                editor.LoadImage(source);

                Assert.True(editor.ExecuteSaveForDiagnostics());

                var filesAfterFirstSave = Directory.GetFiles(tempRoot, "*.png");
                Assert.Single(filesAfterFirstSave);

                string firstFile = filesAfterFirstSave[0];
                string fileName = Path.GetFileName(firstFile);
                Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}(?:_\d+)?\.png$"), fileName);

                DateTime firstWriteUtc = File.GetLastWriteTimeUtc(firstFile);

                Assert.True(editor.ExecuteSaveForDiagnostics());

                var filesAfterSecondSave = Directory.GetFiles(tempRoot, "*.png");
                Assert.Single(filesAfterSecondSave);
                Assert.Equal(firstFile, filesAfterSecondSave.Single());
                Assert.True(File.GetLastWriteTimeUtc(firstFile) >= firstWriteUtc);
            }
            finally
            {
                screenzap.Properties.Settings.Default.captureFolder = previousCaptureFolder;
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

        [Fact]
        public void SaveAs_UsesProvidedPath_AndSubsequentSaveWritesSamePath()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "screenzap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                using var editor = new screenzap.ImageEditor();
                using var source = new Bitmap(20, 10);
                editor.LoadImage(source);

                string saveAsPath = Path.Combine(tempRoot, "custom-name.png");
                Assert.True(editor.ExecuteSaveAsForDiagnostics(saveAsPath));
                Assert.True(File.Exists(saveAsPath));

                DateTime afterSaveAsUtc = File.GetLastWriteTimeUtc(saveAsPath);

                Assert.True(editor.ExecuteSaveForDiagnostics());
                Assert.True(File.Exists(saveAsPath));
                Assert.True(File.GetLastWriteTimeUtc(saveAsPath) >= afterSaveAsUtc);
                Assert.Single(Directory.GetFiles(tempRoot, "*.png"));
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
