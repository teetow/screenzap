using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using screenzap.Components.Shared;
using System.Drawing.Imaging;
using Windows.Storage.Streams;
using Button = Microsoft.UI.Xaml.Controls.Button;
using Color = Windows.UI.Color;
using ComboBox = Microsoft.UI.Xaml.Controls.ComboBox;
using CheckBox = Microsoft.UI.Xaml.Controls.CheckBox;
using ProgressBar = Microsoft.UI.Xaml.Controls.ProgressBar;
using FontFamily = Microsoft.UI.Xaml.Media.FontFamily;
using Orientation = Microsoft.UI.Xaml.Controls.Orientation;
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using Image = Microsoft.UI.Xaml.Controls.Image;
using MenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;

namespace screenzap.WinUI;

internal sealed partial class EditorWindow
{
    private MenuBar BuildMenus()
    {
        var menu = new MenuBar { VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
        void Group(string name, params EditorCommandId[] commands)
        {
            var group = new MenuBarItem { Title = name };
            foreach (var command in commands)
            {
                var descriptor = EditorCommandCatalog.All[command];
                var item = new MenuFlyoutItem { Text = descriptor.Label.Replace("&&", "&"), KeyboardAcceleratorTextOverride = descriptor.Shortcut.HasValue ? EditorCommandCatalog.FormatShortcut(descriptor.Shortcut.Value) : string.Empty };
                menuCommands.Add((command, item));
                item.Click += (_, _) => Execute(command);
                group.Items.Add(item);
            }
            menu.Items.Add(group);
        }
        Group("File", EditorCommandId.Save, EditorCommandId.SaveAs, EditorCommandId.Copy, EditorCommandId.CommitEdits, EditorCommandId.Reload);
        menu.Items[0].Items.Add(new MenuFlyoutSeparator());
        var svg = new MenuFlyoutSubItem { Text = "Copy as SVG" };
        foreach (var command in new[] { EditorCommandId.CopySvgPoster, EditorCommandId.CopySvgPhoto, EditorCommandId.CopySvgBlackAndWhite })
        {
            var item = new MenuFlyoutItem { Text = EditorCommandCatalog.All[command].Label.Replace("&&", "&") };
            menuCommands.Add((command, item));
            item.Click += (_, _) => Execute(command);
            svg.Items.Add(item);
        }
        menu.Items[0].Items.Add(svg);
        var saveClipboard = new MenuFlyoutItem { Text = "Save original clipboard image" };
        saveClipboard.Click += (_, _) => host.SaveClipboardImageRequested?.Invoke(); menu.Items[0].Items.Add(saveClipboard);
        var close = new MenuFlyoutItem { Text = "Close" };
        close.Click += (_, _) => { AppWindow.Hide(); stateTimer.Stop(); }; menu.Items[0].Items.Add(close);
        Group("Edit", EditorCommandId.Undo, EditorCommandId.Redo, EditorCommandId.Duplicate, EditorCommandId.Revert, EditorCommandId.Delete, EditorCommandId.ApplyFloatingPaste);
        Group("View", EditorCommandId.FitImageToView, EditorCommandId.ToggleTransparencyGrid);
        Group("Image", EditorCommandId.CropTool, EditorCommandId.ResizeImage, EditorCommandId.ExpandCanvas, EditorCommandId.RotateRight, EditorCommandId.FlipHorizontal, EditorCommandId.FlipVertical, EditorCommandId.ReplaceBackground, EditorCommandId.ColorCorrect, EditorCommandId.OptimizeText, EditorCommandId.DeJpeg);
        var settings = new MenuBarItem { Title = "Settings" };
        void Toggle(string title, Func<bool>? get, Action<bool>? set)
        {
            var item = new ToggleMenuFlyoutItem { Text = title, IsChecked = get?.Invoke() ?? false };
            item.Click += (_, _) => set?.Invoke(item.IsChecked); settings.Items.Add(item);
        }
        Toggle("Start on login", () => host.GetStartOnLogin?.Invoke() ?? false, value => host.SetStartOnLogin?.Invoke(value));
        Toggle("Show startup notification", () => host.GetStartupNotificationEnabled?.Invoke() ?? false, value => host.SetStartupNotificationEnabled?.Invoke(value));
        void ActionItem(string title, Action action) { var item = new MenuFlyoutItem { Text = title }; item.Click += (_, _) => action(); settings.Items.Add(item); }
        ActionItem("Capture folder…", async () => await CaptureFolderDialog());
        ActionItem("Capture shortcut…", async () => await CaptureShortcutDialog());
        ActionItem("Transparency colors…", async () => await CheckerboardDialog());
        menu.Items.Add(settings);
        return menu;
    }

    private void BuildInspector(string kind)
    {
        inspector.Children.Clear();
        toolApplyButton = null;
        inspectorNumbers.Clear();
        string title = kind switch { "None" => "Selection", "Straighten" => "Perspective", "FreeRotate" => "Free rotate", _ => kind };
        inspector.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        void Field(string label, UIElement control)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, label);
            var stack = new StackPanel { Spacing = 7 };
            stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = .8 }); stack.Children.Add(control); inspector.Children.Add(stack);
        }
        void Number(string label, double value, double min, double max, Action<float> change)
        {
            var box = NumericInput(value, min, max, label.Contains("scale") ? .1 : 1);
            box.GotFocus += (_, _) => editor.SurfaceSuspendText();
            box.ValueChanged += (_, args) => { if (!synchronizingInspector && double.IsFinite(args.NewValue)) change((float)args.NewValue); };
            inspectorNumbers[label] = box;
            Field(label, box);
        }
        void ColorField(string label, System.Drawing.Color color, string property)
        {
            var picker = new ColorPicker { Color = Color.FromArgb(color.A, color.R, color.G, color.B), IsAlphaEnabled = true, IsColorSliderVisible = true, IsColorChannelTextInputVisible = true, IsHexInputVisible = true, MinWidth = 200 };
            var flyout = new Flyout { Content = picker };
            var button = new Button { Style = ChromeButtonStyle, Content = $"#{color.R:X2}{color.G:X2}{color.B:X2}", HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B)), Flyout = flyout };
            button.Foreground = new SolidColorBrush(color.GetBrightness() > .6 ? Colors.Black : Colors.White);
            flyout.Opening += (_, _) => editor.SurfaceSuspendText();
            flyout.Closed += (_, _) => { var c = picker.Color; editor.SurfaceStyle(property, System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B)); inspectorKey = ""; RefreshState(); };
            Field(label, button);
        }
        switch (kind)
        {
            case "Arrow":
            case "Rectangle":
            case "Highlighter":
                ColorField("Color", editor.SurfaceColor, "Color");
                Number("Width", editor.SurfaceWidth, 1, 200, value => editor.SurfaceStyle("Width", value));
                if (kind == "Arrow")
                {
                    var arrowScale = NumericInput((double)editor.SurfaceArrowSize, 0, 5, .1);
                    arrowScale.GotFocus += (_, _) => editor.SurfaceSuspendText();
                    arrowScale.ValueChanged += (_, args) => { if (!synchronizingInspector && double.IsFinite(args.NewValue)) editor.SurfaceStyle("Arrow", (decimal)args.NewValue); };
                    Field("Arrowhead scale", arrowScale);
                }
                if (kind == "Highlighter") Number("Opacity (%)", editor.SurfaceOpacity * 100, 0, 100, value => editor.SurfaceStyle("Opacity", value / 100));
                break;
            case "Text":
                var choices = editor.SurfaceFontChoices;
                var family = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, ItemsSource = choices, Text = editor.SurfaceFont, HorizontalAlignment = HorizontalAlignment.Stretch };
                family.GotFocus += (_, _) => editor.SurfaceSuspendText();
                family.SelectionChanged += (_, _) => { if (family.SelectedItem is string font) editor.SurfaceStyle("Font", font); };
                family.LostFocus += (_, _) => { var font = choices.FirstOrDefault(choice => choice.Equals(family.Text, StringComparison.OrdinalIgnoreCase)); if (font != null) editor.SurfaceStyle("Font", font); else family.Text = editor.SurfaceFont; };
                Field("Font", family);
                Number("Size", editor.SurfaceFontSize, 1, 200, value => editor.SurfaceStyle("Size", value));
                var style = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var name in new[] { "Regular", "Bold", "Italic", "Bold italic" }) style.Items.Add(name);
                style.SelectedIndex = (int)editor.SurfaceFontStyle & 3;
                style.SelectionChanged += (_, _) => editor.SurfaceStyle("Style", (System.Drawing.FontStyle)style.SelectedIndex);
                Field("Style", style);
                ColorField("Text color", editor.SurfaceColor, "Color");
                Number("Outline width", editor.SurfaceOutline, 0, 20, value => editor.SurfaceStyle("Outline", value));
                ColorField("Outline color", editor.SurfaceOutlineColor, "OutlineColor");
                break;
            case "Straighten":
                ToolApplyButtons(); break;
            case "FreeRotate":
                Number("Angle (°)", editor.SurfaceAngleValue, -180, 180, editor.SurfaceAngle);
                ToolApplyButtons(); break;
            case "Censor":
                BuildCensorInspector(Field, Number);
                ToolApplyButtons(); break;
            case "Layer":
                BuildLayerInspector(Field, Number); break;
            default:
                break;
        }
        BuildLayerList();
    }
    private void ToolApplyButtons()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var apply = new Button { Content = "Apply", IsEnabled = editor.SurfaceCanApply };
        toolApplyButton = apply;
        apply.Click += (_, _) => { editor.SurfaceApplyTool(true); inspectorKey = ""; RefreshState(); };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => { editor.SurfaceApplyTool(false); inspectorKey = ""; RefreshState(); };
        row.Children.Add(apply); row.Children.Add(cancel); inspector.Children.Add(row);
    }
    private static async Task<BitmapImage> BitmapSource(System.Drawing.Bitmap bitmap)
    {
        using var encoded = new MemoryStream(); bitmap.Save(encoded, ImageFormat.Png);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream)) { writer.WriteBytes(encoded.ToArray()); await writer.StoreAsync(); writer.DetachStream(); }
        stream.Seek(0); var image = new BitmapImage(); await image.SetSourceAsync(stream); return image;
    }
}
