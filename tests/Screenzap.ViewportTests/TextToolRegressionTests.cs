using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SkiaSharp;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class TextToolRegressionTests
    {
        [Fact]
        public void EditorConstruction_DefersInstalledFontEnumeration()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageDocumentEditor();

                Assert.False(editor.FontChoicesLoadedForDiagnostics);

                editor.LoadFontChoicesForDiagnostics();

                Assert.True(editor.FontChoicesLoadedForDiagnostics);
                Assert.NotEmpty(editor.SurfaceFontChoices);
            });
        }

        [Fact]
        public void SelectingExistingTextAnnotation_RecallsToolbarSettings()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(120, 80);

                string distinctFont = editor.SurfaceFontChoices.FirstOrDefault(name => name != editor.SurfaceFont) ?? "Segoe UI";

                var annotation = new screenzap.TextAnnotation
                {
                    Position = new Point(12, 10),
                    Text = "Toolbar recall",
                    FontFamily = distinctFont,
                    FontSize = 28f,
                    FontStyle = FontStyle.Bold | FontStyle.Italic | FontStyle.Underline,
                    TextColor = Color.MediumVioletRed,
                    OutlineThickness = 4f,
                    OutlineColor = Color.DarkGreen
                };

                editor.TestAddTextAnnotation(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                var handled = editor.TestHandleTextToolMouseDown(pixelPoint);

                Assert.True(handled);
                Assert.Equal(distinctFont, editor.SurfaceFont);
                Assert.Equal(annotation.FontSize, editor.SurfaceFontSize);
                Assert.Equal(annotation.FontStyle, editor.SurfaceFontStyle);
                Assert.Equal(annotation.TextColor.ToArgb(), editor.SurfaceColor.ToArgb());
                Assert.Equal(annotation.OutlineColor.ToArgb(), editor.SurfaceOutlineColor.ToArgb());
                Assert.Equal(4f, editor.SurfaceOutline);
            });
        }

        [Fact]
        public void SelectionMode_DoesNotAutoInsertTypedCharacters()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(120, 80);

                var annotation = new screenzap.TextAnnotation
                {
                    Position = new Point(12, 10),
                    Text = "Hello",
                    FontFamily = "Segoe UI",
                    FontSize = 16f,
                    FontStyle = FontStyle.Regular,
                    TextColor = Color.Red,
                    OutlineThickness = 0f,
                    OutlineColor = Color.Black
                };

                editor.TestAddTextAnnotation(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                Assert.True(editor.TestHandleTextToolMouseDown(pixelPoint));
                Assert.False(annotation.IsEditing);

                var handled = editor.TestHandleTextToolKeyPress('Z');

                Assert.False(handled);
                Assert.False(annotation.IsEditing);
                Assert.Equal("Hello", annotation.Text);
            });
        }

        [Fact]
        public void SelectionMode_EnterStartsExplicitTextEditing()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(120, 80);

                var annotation = new screenzap.TextAnnotation
                {
                    Position = new Point(12, 10),
                    Text = "Hello",
                    FontFamily = "Segoe UI",
                    FontSize = 16f,
                    FontStyle = FontStyle.Regular,
                    TextColor = Color.Red,
                    OutlineThickness = 0f,
                    OutlineColor = Color.Black
                };

                editor.TestAddTextAnnotation(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                Assert.True(editor.TestHandleTextToolMouseDown(pixelPoint));

                var keyDown = new KeyEventArgs(Keys.Enter);
                Assert.True(editor.TestHandleTextToolKeyDown(keyDown.KeyData));
                Assert.True(annotation.IsEditing);

                var keyPress = new KeyPressEventArgs('!');
                Assert.True(editor.TestHandleTextToolKeyPress(keyPress.KeyChar));
                Assert.Equal("Hello!", annotation.Text);
            });
        }

        [Fact]
        public void ToolbarCommit_CanReturnSelectedAnnotationToEditingMode()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(120, 80);

                var annotation = new screenzap.TextAnnotation
                {
                    Position = new Point(12, 10),
                    Text = "Hello",
                    FontFamily = "Segoe UI",
                    FontSize = 16f,
                    FontStyle = FontStyle.Regular,
                    TextColor = Color.Red,
                    OutlineThickness = 0f,
                    OutlineColor = Color.Black
                };

                editor.TestAddTextAnnotation(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                Assert.True(editor.TestHandleTextToolMouseDown(pixelPoint));

                var keyDown = new KeyEventArgs(Keys.Enter);
                Assert.True(editor.TestHandleTextToolKeyDown(keyDown.KeyData));
                Assert.True(annotation.IsEditing);

                editor.TestSuspendTextEditingForUiFocus();
                Assert.False(annotation.IsEditing);

                editor.TestResumeSelectedTextEditing();
                Assert.True(annotation.IsEditing);
            });
        }

        [Fact]
        public void CanvasClick_AfterToolbarFocus_ResumesSelectedTextEditing()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(200, 120);

                var annotation = new screenzap.TextAnnotation
                {
                    Position = new Point(12, 10),
                    Text = "Hello",
                    FontFamily = "Segoe UI",
                    FontSize = 16f,
                    FontStyle = FontStyle.Regular,
                    TextColor = Color.Red,
                    OutlineThickness = 0f,
                    OutlineColor = Color.Black
                };

                editor.TestAddTextAnnotation(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                Assert.True(editor.TestHandleTextToolMouseDown(pixelPoint));
                Assert.True(editor.TestHandleTextToolKeyDown(Keys.Enter));
                Assert.True(annotation.IsEditing);

                editor.TestSuspendTextEditingForUiFocus();
                Assert.False(annotation.IsEditing);

                int beforeCount = editor.TestTextAnnotationCount;
                var emptyPixel = new Point(120, 70);
                Assert.True(editor.TestHandleTextToolMouseDown(emptyPixel));

                Assert.Equal(beforeCount, editor.TestTextAnnotationCount);
                Assert.True(annotation.IsEditing);
            });
        }

        [Fact]
        public void HeavyWeightFontVariants_DoNotCollapseToRegularTypeface()
        {
            var installedFonts = new System.Drawing.Text.InstalledFontCollection();
            string? heavyVariant = installedFonts.Families
                .Select(f => f.Name)
                .FirstOrDefault(name => name.Contains("Black", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("ExtraBold", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Extra Bold", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Heavy", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(heavyVariant))
            {
                return;
            }

            string baseFamily = TrimVariantSuffix(heavyVariant);
            var regularTypeface = CreateTypeface(baseFamily, FontStyle.Regular);
            var heavyTypeface = CreateTypeface(heavyVariant, FontStyle.Regular);

            Assert.NotNull(regularTypeface);
            Assert.NotNull(heavyTypeface);

            int regularWeight = GetTypefaceWeight(regularTypeface!);
            int heavyWeight = GetTypefaceWeight(heavyTypeface!);

            Assert.True(
                heavyWeight > regularWeight ||
                !string.Equals(heavyTypeface!.FamilyName, regularTypeface!.FamilyName, StringComparison.OrdinalIgnoreCase),
                $"Expected '{heavyVariant}' to resolve heavier than '{baseFamily}', but got weights {regularWeight} and {heavyWeight}.");
        }

        private static string TrimVariantSuffix(string fontName)
        {
            string[] suffixes =
            {
                " Extra Bold", " ExtraBold", " Black", " Heavy", " Ultra Bold", " UltraBold"
            };

            foreach (var suffix in suffixes)
            {
                if (fontName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return fontName.Substring(0, fontName.Length - suffix.Length).TrimEnd();
                }
            }

            return fontName;
        }

        private static SKTypeface? CreateTypeface(string familyName, FontStyle style)
        {
            var rendererType = typeof(screenzap.ImageDocumentEditor).Assembly.GetType("screenzap.EmojiTextRenderer");
            Assert.NotNull(rendererType);

            var method = rendererType!.GetMethod("CreateSkTypeface", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            return (SKTypeface?)method!.Invoke(null, new object[] { familyName, style });
        }

        private static int GetTypefaceWeight(SKTypeface typeface)
        {
            var fontStyle = typeface.FontStyle;
            var weightProperty = fontStyle.GetType().GetProperty("Weight");
            Assert.NotNull(weightProperty);
            return Convert.ToInt32(weightProperty!.GetValue(fontStyle));
        }

        private static T GetPrivateField<T>(object target, string fieldName) where T : class
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var value = field!.GetValue(target) as T;
            Assert.NotNull(value);
            return value!;
        }

    }
}
