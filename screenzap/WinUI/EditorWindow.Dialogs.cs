using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using screenzap.Components.Shared;
using screenzap.lib;
using System.Drawing.Imaging;
using Button = Microsoft.UI.Xaml.Controls.Button;
using ComboBox = Microsoft.UI.Xaml.Controls.ComboBox;
using CheckBox = Microsoft.UI.Xaml.Controls.CheckBox;
using ProgressBar = Microsoft.UI.Xaml.Controls.ProgressBar;
using FontFamily = Microsoft.UI.Xaml.Media.FontFamily;
using Orientation = Microsoft.UI.Xaml.Controls.Orientation;

namespace screenzap.WinUI;

internal sealed partial class EditorWindow
{
    private async void ExecuteNativeCommand(EditorCommandId command)
    {
        try
        {
            if (command is EditorCommandId.Save or EditorCommandId.SaveAs or EditorCommandId.Copy) editor.SurfaceFinalizeText();
            // Exports show what the canvas shows, so an open color adjustment is applied first.
            if (command is EditorCommandId.Save or EditorCommandId.SaveAs or EditorCommandId.Copy or EditorCommandId.CommitEdits or EditorCommandId.CopySvgPoster or EditorCommandId.CopySvgPhoto or EditorCommandId.CopySvgBlackAndWhite) editor.SurfaceEndColorAdjustment(true);
            switch (command)
            {
                case EditorCommandId.Reload:
                    if (!editor.DocumentIsDirty || await Dialog("Reload image?", "Discard the current edits and reload from the clipboard?", "Reload").ShowAsync() == ContentDialogResult.Primary)
                        editor.SurfaceReloadConfirmed();
                    break;
                case EditorCommandId.SaveAs: await SaveAsDialog(); break;
                case EditorCommandId.ResizeImage: await ResizeDialog(); break;
                case EditorCommandId.EmojiTool: ShowEmojiFlyout(); break;
                case EditorCommandId.DeJpeg: await DeJpegDialog(); break;
                default: host.ExecuteHostCommand(command); break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Editor command {command}: {ex}");
            await new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = "Couldn't complete the operation", Content = ex.Message, CloseButtonText = "Close" }.ShowAsync();
        }
        finally { if (command != EditorCommandId.CommitEdits) inspectorKey = ""; RefreshState(); canvas.Invalidate(); }
    }
    private ContentDialog Dialog(string title, object content, string action = "Apply") => new() { XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = title, Content = content, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
    private async Task ResizeDialog()
    {
        var size = editor.SurfaceImageSize;
        long revision = editor.SurfaceRevision;
        var width = NumericInput(size.Width, 1, 32768, header: "Width (px)");
        var height = NumericInput(size.Height, 1, 32768, header: "Height (px)");
        var aspect = new CheckBox { Content = "Keep aspect ratio", IsChecked = true };
        bool updating = false;
        width.ValueChanged += (_, e) => { if (updating || aspect.IsChecked != true || double.IsNaN(e.NewValue)) return; updating = true; height.Value = Math.Round(e.NewValue * size.Height / size.Width); updating = false; };
        height.ValueChanged += (_, e) => { if (updating || aspect.IsChecked != true || double.IsNaN(e.NewValue)) return; updating = true; width.Value = Math.Round(e.NewValue * size.Width / size.Height); updating = false; };
        var interpolation = new ComboBox { Header = "Sampling", SelectedIndex = 0 };
        interpolation.Items.Add("Smooth (bicubic)"); interpolation.Items.Add("Pixel art (nearest neighbor)");
        var content = new StackPanel { Spacing = 14 }; content.Children.Add(width); content.Children.Add(height); content.Children.Add(aspect); content.Children.Add(interpolation);
        if (await Dialog("Resize image", content).ShowAsync() == ContentDialogResult.Primary && editor.SurfaceRevision == revision && double.IsFinite(width.Value) && double.IsFinite(height.Value)) editor.SurfaceResizeImage(new System.Drawing.Size((int)width.Value, (int)height.Value), interpolation.SelectedIndex == 1);
    }
    private async Task DeJpegDialog()
    {
        using var image = editor.SurfaceCopyBase();
        if (image == null) return;
        long revision = editor.SurfaceRevision;
        using var input = new DeJpegBitmap(image);
        var progressText = new TextBlock { Text = "Preparing JPEG cleanup…", TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 16 }; content.Children.Add(progressText); content.Children.Add(new ProgressBar { IsIndeterminate = true });
        var dialog = Dialog("De-JPEG", content); dialog.PrimaryButtonText = "";
        using var cancellation = new CancellationTokenSource();
        var show = dialog.ShowAsync();
        dialog.Closing += (_, _) => cancellation.Cancel();
        try
        {
            var bytes = await new OnnxDeJpegFilter().CleanAsync(input.Png, new Progress<string>(value => progressText.Text = value), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            using var result = input.Restore(bytes);
            if (!editor.ApplyDeJpeg(result, revision)) progressText.Text = "The image changed during cleanup. Please try again.";
        }
        catch (OperationCanceledException) { }
        finally { dialog.Hide(); await show; }
    }
    private void BuildColorInspector()
    {
        // Non-modal: the canvas itself is the preview, and the fields answer the wheel on hover
        // so the canvas keeps the keyboard (Enter applies, Escape cancels).
        colorScopeText = new TextBlock { FontSize = 12, Opacity = .65, TextWrapping = TextWrapping.Wrap };
        inspector.Children.Add(colorScopeText);
        var current = editor.SurfaceColorAdjustment;
        var fields = new StackPanel { Spacing = 4 };
        ScrubField Add(string label, double value, double min, double max, double step, double neutral, Func<double, string> format)
        {
            var field = new ScrubField(label, value, min, max, step, neutral, format);
            field.EditFinished += () => canvas.Focus(FocusState.Programmatic);
            fields.Children.Add(field);
            return field;
        }
        var exposure = Add("Exposure", current.Exposure, -3, 3, .05, 0, v => $"{v:+0.00;-0.00;0.00} EV");
        var contrast = Add("Contrast", current.Contrast, -100, 100, 1, 0, v => $"{v:+0;-0;0}%");
        var saturation = Add("Saturation", current.Saturation, 0, 200, 1, 100, v => $"{v:0}%");
        var gamma = Add("Gamma", current.Gamma, .1, 3, .05, 1, v => $"{v:0.00}");
        var all = new[] { exposure, contrast, saturation, gamma };
        void Push() => editor.SurfaceSetColorAdjustment(new((float)exposure.Value, (float)contrast.Value, (float)saturation.Value, (float)gamma.Value));
        foreach (var field in all) field.ValueChanged += _ => Push();
        inspector.Children.Add(fields);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var apply = new Button { Content = "Apply", Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] };
        apply.Click += (_, _) => EndColorAdjustment(true);
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => EndColorAdjustment(false);
        var reset = new Button { Content = "Reset" };
        reset.Click += (_, _) => { foreach (var field in all) field.Value = field.Neutral; Push(); };
        row.Children.Add(apply); row.Children.Add(cancel); row.Children.Add(reset);
        inspector.Children.Add(row);
    }
    private void EndColorAdjustment(bool apply)
    {
        editor.SurfaceEndColorAdjustment(apply);
        inspectorKey = "";
        RefreshState();
        canvas.Focus(FocusState.Programmatic);
    }
    private void BuildCensorInspector(Action<string, UIElement> field, Action<string, double, double, double, Action<float>> number)
    {
        var directions = new ComboBox { SelectedIndex = editor.SurfaceCensorDirection };
        foreach (var direction in new[] { "Horizontal", "Vertical", "Both" }) directions.Items.Add(direction);
        directions.SelectionChanged += (_, _) => editor.SurfaceCensorSettings(directions.SelectedIndex, null, null);
        field("Direction", directions);
        number("Iterations", editor.SurfaceCensorIterations, 1, 100, value => editor.SurfaceCensorSettings(null, (int)value, null));
        number("Smear (%)", editor.SurfaceCensorSmear, 0, 100, value => editor.SurfaceCensorSettings(null, null, (int)value));
        var select = new Button { Content = "Select all regions" }; select.Click += (_, _) => editor.SurfaceSelectCensor(true); inspector.Children.Add(select);
        var clear = new Button { Content = "Deselect all" }; clear.Click += (_, _) => editor.SurfaceSelectCensor(false); inspector.Children.Add(clear);
    }
    private void BuildLayerInspector(Action<string, UIElement> field, Action<string, double, double, double, Action<float>> number)
    {
        var aspect = new CheckBox { Content = "Lock aspect ratio", IsChecked = editor.SurfaceLayerAspectLock };
        aspect.Checked += (_, _) => editor.SurfaceLayerAspectLock = true; aspect.Unchecked += (_, _) => editor.SurfaceLayerAspectLock = false;
        inspector.Children.Add(aspect);
        var layer = editor.SurfaceSelectedLayer;
        if (layer == null) return;
        number("X", layer.Frame.X, -100000, 100000, value => editor.SurfaceLayerProperty("X", value));
        number("Y", layer.Frame.Y, -100000, 100000, value => editor.SurfaceLayerProperty("Y", value));
        number("Width", layer.Frame.Width, 1, 32768, value => editor.SurfaceLayerProperty("Width", value));
        number("Height", layer.Frame.Height, 1, 32768, value => editor.SurfaceLayerProperty("Height", value));
        number("Rotation (°)", layer.RotationDeg, -180, 180, value => editor.SurfaceLayerProperty("Angle", value));
        inspector.Children.Add(CommandButton(EditorCommandId.ApplyFloatingPaste, "\uE73E", "Merge layer"));
    }
    private void BuildLayerList()
    {
        if (editor.SurfaceLayerCount == 0) return;
        inspector.Children.Add(new TextBlock { Text = "Layers", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        for (int i = editor.SurfaceLayerCount - 1; i >= 0; i--)
        {
            int index = i;
            var layer = editor.SurfaceLayerAt(i);
            var row = new StackPanel { Spacing = 4 };
            var button = new Button { Content = layer.Name, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
            button.Click += (_, _) => { editor.SurfaceSelectLayer(index); inspectorKey = ""; RefreshState(); }; row.Children.Add(button);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            foreach (var action in new[] { ("Show", layer.IsVisible ? "\uE890" : "\uE7B3", "Show / hide layer"), ("Merge", "\uE73E", "Merge layer"), ("Delete", "\uE74D", "Delete layer"), ("Up", "\uE74A", "Move layer up"), ("Down", "\uE74B", "Move layer down") })
            {
                string name = action.Item1;
                actions.Children.Add(PlainButton(action.Item2, action.Item3, () => { editor.SurfaceLayerAction(index, name); inspectorKey = ""; RefreshState(); }));
            }
            row.Children.Add(actions); inspector.Children.Add(row);
        }
        inspector.Children.Add(new TextBlock { Text = "Background", FontSize = 12, Opacity = .65 });
    }
    private async Task SaveAsDialog()
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "Screenzap image", DefaultFileExtension = ".png" };
        picker.FileTypeChoices.Add("PNG image", new[] { ".png" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file != null) editor.SurfaceSaveAs(file.Path);
    }
}
