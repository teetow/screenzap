using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using screenzap;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageEditorViewportRegressionTests
    {
        [Fact]
        public void Rotate90_FullImage_KeepsViewportImageInClientBounds()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var editor = new ImageEditor();
                    using var source = new Bitmap(640, 200);
                    using (var g = Graphics.FromImage(source))
                    {
                        g.Clear(Color.DarkSlateBlue);
                    }

                    var loadImage = typeof(ImageEditor).GetMethod(
                        "LoadImage",
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        binder: null,
                        types: new[] { typeof(Image), typeof(bool) },
                        modifiers: null);
                    Assert.NotNull(loadImage);
                    loadImage!.Invoke(editor, new object[] { source, false });

                    var rotate = typeof(ImageEditor).GetMethod("ExecuteRotate90Cw", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(rotate);
                    var ok = rotate!.Invoke(editor, null);
                    Assert.True(ok is bool b && b, "Expected full-image rotate command to execute successfully.");

                    var metrics = editor.ViewportDiagnostics;
                    Assert.True(metrics.HasImage, "Expected viewport diagnostics to report an active image.");
                    Assert.True(metrics.ImageClientRectangle.Left >= -0.5f, "Image should not overflow left edge after rotate.");
                    Assert.True(metrics.ImageClientRectangle.Top >= -0.5f, "Image should not overflow top edge after rotate.");
                    Assert.True(metrics.ImageClientRectangle.Right <= metrics.ClientSize.Width + 0.5f, "Image should stay within right client bound after rotate.");
                    Assert.True(metrics.ImageClientRectangle.Bottom <= metrics.ClientSize.Height + 0.5f, "Image should stay within bottom client bound after rotate.");
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            if (failure != null)
            {
                throw new TargetInvocationException(failure);
            }
        }

        private static void RunInSta(ThreadStart action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw failure;
            }
        }
    }
}
