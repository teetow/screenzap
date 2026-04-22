using System;
using System.Collections;
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
        public void SelectingExistingTextAnnotation_RecallsToolbarSettings()
        {
            RunInSta(() =>
            {
                using var editor = new screenzap.ImageEditor();
                editor.CreateControl();

                using var image = new Bitmap(120, 80);
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.White);
                }

                editor.LoadImage(image);

                var fontCombo = GetPrivateField<ToolStripComboBox>(editor, "fontComboBox");
                var sizeCombo = GetPrivateField<ToolStripComboBox>(editor, "fontSizeComboBox");
                var boldButton = GetPrivateField<ToolStripButton>(editor, "boldButton");
                var italicButton = GetPrivateField<ToolStripButton>(editor, "italicButton");
                var underlineButton = GetPrivateField<ToolStripButton>(editor, "underlineButton");
                var textColorButton = GetPrivateField<ToolStripButton>(editor, "textColorButton");
                var outlineColorButton = GetPrivateField<ToolStripButton>(editor, "outlineColorButton");
                var outlineThicknessCombo = GetPrivateField<ToolStripComboBox>(editor, "outlineThicknessComboBox");

                string distinctFont = fontCombo.Items.Cast<object>()
                    .Select(item => item?.ToString())
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, fontCombo.Text, StringComparison.OrdinalIgnoreCase))
                    ?? "Segoe UI";

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

                var annotations = GetPrivateField<IList>(editor, "textAnnotations");
                annotations.Add(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                var formPoint = (Point)InvokePrivate(editor, "PixelToFormCoord", pixelPoint)!;
                var handled = (bool)InvokePrivate(editor, "HandleTextToolMouseDown", pixelPoint, formPoint)!;

                Assert.True(handled);
                Assert.Equal(distinctFont, fontCombo.Text);
                Assert.Equal(annotation.FontSize, float.Parse(sizeCombo.Text));
                Assert.True(boldButton.Checked);
                Assert.True(italicButton.Checked);
                Assert.True(underlineButton.Checked);
                Assert.Equal(annotation.TextColor.ToArgb(), textColorButton.BackColor.ToArgb());
                Assert.Equal(annotation.OutlineColor.ToArgb(), outlineColorButton.BackColor.ToArgb());
                Assert.Equal("4", outlineThicknessCombo.Text);
            });
        }

        [Fact]
        public void SelectionMode_DoesNotAutoInsertTypedCharacters()
        {
            RunInSta(() =>
            {
                using var editor = new screenzap.ImageEditor();
                editor.CreateControl();

                using var image = new Bitmap(120, 80);
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.White);
                }

                editor.LoadImage(image);

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

                var annotations = GetPrivateField<IList>(editor, "textAnnotations");
                annotations.Add(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                var formPoint = (Point)InvokePrivate(editor, "PixelToFormCoord", pixelPoint)!;
                Assert.True((bool)InvokePrivate(editor, "HandleTextToolMouseDown", pixelPoint, formPoint)!);
                Assert.False(annotation.IsEditing);

                var handled = (bool)InvokePrivate(editor, "HandleTextToolKeyPress", new KeyPressEventArgs('Z'))!;

                Assert.False(handled);
                Assert.False(annotation.IsEditing);
                Assert.Equal("Hello", annotation.Text);
            });
        }

        [Fact]
        public void SelectionMode_EnterStartsExplicitTextEditing()
        {
            RunInSta(() =>
            {
                using var editor = new screenzap.ImageEditor();
                editor.CreateControl();

                using var image = new Bitmap(120, 80);
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.White);
                }

                editor.LoadImage(image);

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

                var annotations = GetPrivateField<IList>(editor, "textAnnotations");
                annotations.Add(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                var formPoint = (Point)InvokePrivate(editor, "PixelToFormCoord", pixelPoint)!;
                Assert.True((bool)InvokePrivate(editor, "HandleTextToolMouseDown", pixelPoint, formPoint)!);

                var keyDown = new KeyEventArgs(Keys.Enter);
                Assert.True((bool)InvokePrivate(editor, "HandleTextToolKeyDown", keyDown)!);
                Assert.True(annotation.IsEditing);

                var keyPress = new KeyPressEventArgs('!');
                Assert.True((bool)InvokePrivate(editor, "HandleTextToolKeyPress", keyPress)!);
                Assert.Equal("Hello!", annotation.Text);
            });
        }

        [Fact]
        public void ToolbarCommit_CanReturnSelectedAnnotationToEditingMode()
        {
            RunInSta(() =>
            {
                using var editor = new screenzap.ImageEditor();
                editor.CreateControl();

                using var image = new Bitmap(120, 80);
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.White);
                }

                editor.LoadImage(image);

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

                var annotations = GetPrivateField<IList>(editor, "textAnnotations");
                annotations.Add(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                var formPoint = (Point)InvokePrivate(editor, "PixelToFormCoord", pixelPoint)!;
                Assert.True((bool)InvokePrivate(editor, "HandleTextToolMouseDown", pixelPoint, formPoint)!);

                var keyDown = new KeyEventArgs(Keys.Enter);
                Assert.True((bool)InvokePrivate(editor, "HandleTextToolKeyDown", keyDown)!);
                Assert.True(annotation.IsEditing);

                InvokePrivate(editor, "SuspendTextEditingForUiFocus");
                Assert.False(annotation.IsEditing);

                InvokePrivate(editor, "ResumeSelectedTextEditing");
                Assert.True(annotation.IsEditing);
            });
        }

        [Fact]
        public void CanvasClick_AfterToolbarFocus_ResumesSelectedTextEditing()
        {
            RunInSta(() =>
            {
                using var editor = new screenzap.ImageEditor();
                editor.CreateControl();

                using var image = new Bitmap(200, 120);
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.White);
                }

                editor.LoadImage(image);

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

                var annotations = GetPrivateField<IList>(editor, "textAnnotations");
                annotations.Add(annotation);

                var pixelPoint = new Point(annotation.Position.X + 2, annotation.Position.Y + 2);
                var formPoint = (Point)InvokePrivate(editor, "PixelToFormCoord", pixelPoint)!;
                Assert.True((bool)InvokePrivate(editor, "HandleTextToolMouseDown", pixelPoint, formPoint)!);
                Assert.True((bool)InvokePrivate(editor, "HandleTextToolKeyDown", new KeyEventArgs(Keys.Enter))!);
                Assert.True(annotation.IsEditing);

                var dummyInput = new TextBox();
                editor.Controls.Add(dummyInput);
                dummyInput.Focus();
                InvokePrivate(editor, "SuspendTextEditingForUiFocus");
                Assert.False(annotation.IsEditing);

                int beforeCount = annotations.Count;
                var emptyPixel = new Point(120, 70);
                var emptyForm = (Point)InvokePrivate(editor, "PixelToFormCoord", emptyPixel)!;
                Assert.True((bool)InvokePrivate(editor, "HandleTextToolMouseDown", emptyPixel, emptyForm)!);

                Assert.Equal(beforeCount, annotations.Count);
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
            var rendererType = typeof(screenzap.ImageEditor).Assembly.GetType("screenzap.EmojiTextRenderer");
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

        private static void RunInSta(Action action)
        {
            Exception? captured = null;
            using var completed = new System.Threading.ManualResetEventSlim(false);

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    completed.Set();
                }
            });

            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            completed.Wait();

            if (captured != null)
            {
                throw new TargetInvocationException(captured);
            }
        }

        private static T GetPrivateField<T>(object target, string fieldName) where T : class
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var value = field!.GetValue(target) as T;
            Assert.NotNull(value);
            return value!;
        }

        private static object? InvokePrivate(object target, string methodName, params object[] args)
        {
            var argTypes = args.Select(arg => arg.GetType()).ToArray();
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, binder: null, types: argTypes, modifiers: null);
            Assert.NotNull(method);
            return method!.Invoke(target, args);
        }
    }
}
