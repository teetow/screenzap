using System;
using System.Drawing;
using System.Reflection;
using screenzap;
using screenzap.Components.Shared;
using screenzap.lib;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class OptimizeTextTests
    {
        [Fact]
        public void SparseFaintInk_IsDarkenedWithoutLosingSoftEdges()
        {
            var pixels = GrayPixels(10000, 250);
            SetGray(pixels, 0, 200);
            SetGray(pixels, 1, 220);
            SetGray(pixels, 2, 235);

            TextToneNormalizer.Normalize(pixels);

            Assert.InRange(pixels[0], 0, 64);
            Assert.InRange(pixels[4], pixels[0] + 1, 230);
            Assert.InRange(pixels[8], pixels[4] + 1, 254);
            Assert.Equal(255, pixels[12]);
        }

        [Fact]
        public void Midtones_AreDarkenedBeyondLinearLevels_WhenInkHasMixedStrength()
        {
            var pixels = GrayPixels(1000, 250);
            for (int i = 0; i < 10; i++) SetGray(pixels, i, 40);
            for (int i = 10; i < 100; i++) SetGray(pixels, i, 150);

            TextToneNormalizer.Normalize(pixels);

            Assert.InRange(pixels[0], 1, 20);
            Assert.InRange(pixels[40], 55, 75);
            Assert.Equal(255, pixels[400]);
        }

        [Fact]
        public void DarkStrokeDetail_RemainsDistinct_WhilePaperStillClipsWhite()
        {
            var pixels = GrayPixels(1000, 250);
            for (int i = 0; i < 100; i++) SetGray(pixels, i, 150);
            // These tones all fell at or below the old fifth-percentile black point.
            SetGray(pixels, 0, 40);
            SetGray(pixels, 1, 50);
            SetGray(pixels, 2, 60);
            SetGray(pixels, 100, 252);
            SetGray(pixels, 101, 255);

            TextToneNormalizer.Normalize(pixels);

            Assert.InRange(pixels[0], 1, 20);
            Assert.True(pixels[4] > pixels[0]);
            Assert.True(pixels[8] > pixels[4]);
            Assert.InRange(pixels[12], 55, 75);
            Assert.Equal(255, pixels[400]);
            Assert.Equal(255, pixels[404]);
            Assert.Equal(255, pixels[408]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(128)]
        [InlineData(240)]
        [InlineData(255)]
        public void UniformImages_AreUnchanged(int tone)
        {
            var pixels = GrayPixels(32, (byte)tone);
            var original = (byte[])pixels.Clone();
            TextToneNormalizer.Normalize(pixels);
            Assert.Equal(original, pixels);
        }

        [Fact]
        public void LowContrastPaperGrain_IsNotStretched()
        {
            var pixels = GrayPixels(1000, 250);
            for (int i = 0; i < 500; i++) SetGray(pixels, i, (byte)(238 + i % 12));
            var original = (byte[])pixels.Clone();
            TextToneNormalizer.Normalize(pixels);
            Assert.Equal(original, pixels);
        }

        [Fact]
        public void TransparentPixels_DoNotAffectLevels_AndAlphaIsPreserved()
        {
            var opaque = GrayPixels(100, 250);
            for (int i = 0; i < 10; i++) SetGray(opaque, i, 170);
            var padded = new byte[opaque.Length + 4000];
            Array.Copy(opaque, padded, opaque.Length);
            for (int i = opaque.Length; i < padded.Length; i += 4)
            {
                padded[i] = 255;
                padded[i + 3] = (byte)((i / 4) % 128);
            }
            var before = (byte[])padded.Clone();

            TextToneNormalizer.Normalize(opaque);
            TextToneNormalizer.Normalize(padded);

            Assert.Equal(opaque, padded.AsSpan(0, opaque.Length).ToArray());
            for (int i = 0; i < padded.Length; i += 4)
            {
                Assert.Equal(before[i + 3], padded[i + 3]);
                if (before[i + 3] == 0)
                    Assert.Equal(before.AsSpan(i, 4).ToArray(), padded.AsSpan(i, 4).ToArray());
            }
        }

        [Fact]
        public void ShadedTextImage_HasWhitePaperDarkInkAndGrayEdges()
        {
            using var source = new Bitmap(192, 96);
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                int paper = 160 + x * 60 / source.Width;
                int tone = paper + (x + y) % 3 - 1;
                if (y >= 24 && y < 72 && x % 32 == 16) tone = paper / 2;
                if (y >= 24 && y < 72 && x % 32 == 17) tone = paper * 3 / 4;
                source.SetPixel(x, y, Color.FromArgb(tone, tone, tone));
            }

            var optimize = typeof(ImageDocumentEditor).GetMethod("CreateOptimizedForTextCopy",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            using var result = (Bitmap)optimize.Invoke(null, new object[] { source })!;

            foreach (int x in new[] { 16, 80, 144 })
            {
                Assert.InRange(result.GetPixel(x, 12).R, 245, 255);
                Assert.InRange(result.GetPixel(x, 48).R, 1, 40);
                Assert.InRange(result.GetPixel(x + 1, 48).R, 30, 230);
            }
        }

        [Fact]
        public void Command_ChangesOnlySelection_AndSupportsUndoRedo()
        {
            StaTest.Run(() =>
            {
                using var source = new Bitmap(128, 64);
                using (var graphics = Graphics.FromImage(source))
                {
                    graphics.Clear(Color.FromArgb(200, 200, 200));
                    using var brush = new SolidBrush(Color.FromArgb(120, 120, 120));
                    graphics.FillRectangle(brush, 40, 24, 2, 16);
                }
                using var editor = new ImageDocumentEditor();
                editor.LoadImage(source);
                var selection = new Rectangle(16, 8, 96, 48);
                editor.SetSelectionForDiagnostics(selection);
                var presenter = (IClipboardDocumentPresenter)editor;

                Assert.True(presenter.TryExecute(EditorCommandId.OptimizeText));
                using var optimized = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(source.GetPixel(0, 0), optimized.GetPixel(0, 0));
                Assert.InRange(optimized.GetPixel(40, 30).R, 1, 40);
                Assert.InRange(optimized.GetPixel(60, 30).R, 245, 255);
                Assert.Equal(selection, editor.SelectionDiagnostics.Selection);

                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                using var undone = editor.CloneBaseBitmapForTests()!;
                AssertImagesEqual(source, undone);
                Assert.True(presenter.TryExecute(EditorCommandId.Redo));
                using var redone = editor.CloneBaseBitmapForTests()!;
                AssertImagesEqual(optimized, redone);
            });
        }

        private static byte[] GrayPixels(int count, byte tone)
        {
            var pixels = new byte[count * 4];
            for (int i = 0; i < count; i++)
            {
                SetGray(pixels, i, tone);
                pixels[i * 4 + 3] = 255;
            }
            return pixels;
        }

        private static void SetGray(byte[] pixels, int pixel, byte tone)
        {
            pixels[pixel * 4] = tone;
            pixels[pixel * 4 + 1] = tone;
            pixels[pixel * 4 + 2] = tone;
        }

        private static void AssertImagesEqual(Bitmap expected, Bitmap actual)
        {
            Assert.Equal(expected.Size, actual.Size);
            for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.Equal(expected.GetPixel(x, y), actual.GetPixel(x, y));
        }
    }
}
