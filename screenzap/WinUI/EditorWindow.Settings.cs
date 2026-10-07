using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Color = Windows.UI.Color;

namespace screenzap.WinUI;

internal sealed partial class EditorWindow
{
    private async Task CaptureFolderDialog()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null) { Properties.Settings.Default.captureFolder = folder.Path; Properties.Settings.Default.Save(); }
    }
    private async Task CaptureShortcutDialog()
    {
        var keys = host.GetCaptureShortcut?.Invoke() ?? (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.D4);
        var converter = new System.Windows.Forms.KeysConverter();
        var input = new Microsoft.UI.Xaml.Controls.TextBox { Header = "Press the shortcut", Text = converter.ConvertToString(keys), IsReadOnly = true };
        input.KeyDown += (_, e) =>
        {
            var pressed = KeyData(e.Key);
            var code = pressed & System.Windows.Forms.Keys.KeyCode;
            if (code is not (System.Windows.Forms.Keys.ShiftKey or System.Windows.Forms.Keys.ControlKey or System.Windows.Forms.Keys.Menu or System.Windows.Forms.Keys.LWin or System.Windows.Forms.Keys.RWin)) { keys = pressed; input.Text = converter.ConvertToString(keys); }
            e.Handled = true;
        };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed) };
        var content = new StackPanel { Spacing = 14 }; content.Children.Add(input); content.Children.Add(error);
        var dialog = Dialog("Capture shortcut", content, "Save");
        dialog.PrimaryButtonClick += (_, e) => { if (host.TrySetCaptureShortcut?.Invoke(keys) != true) { error.Text = "That shortcut is unavailable. Please choose another."; e.Cancel = true; } };
        dialog.Opened += (_, _) => input.Focus(FocusState.Programmatic);
        await dialog.ShowAsync();
    }
    private async Task CheckerboardDialog()
    {
        Color Read(int argb) { var c = System.Drawing.Color.FromArgb(argb); return Color.FromArgb(c.A, c.R, c.G, c.B); }
        var light = new ColorPicker { Color = Read(Properties.Settings.Default.checkerboardLightColorArgb), IsAlphaEnabled = false, IsColorSliderVisible = false, IsColorChannelTextInputVisible = false, IsHexInputVisible = true };
        var dark = new ColorPicker { Color = Read(Properties.Settings.Default.checkerboardDarkColorArgb), IsAlphaEnabled = false, IsColorSliderVisible = false, IsColorChannelTextInputVisible = false, IsHexInputVisible = true };
        var tabs = new Pivot(); tabs.Items.Add(new PivotItem { Header = "Light", Content = light }); tabs.Items.Add(new PivotItem { Header = "Dark", Content = dark });
        if (await Dialog("Transparency colors", tabs, "Save").ShowAsync() == ContentDialogResult.Primary)
        {
            int Argb(Color c) => System.Drawing.Color.FromArgb(255, c.R, c.G, c.B).ToArgb();
            Properties.Settings.Default.checkerboardLightColorArgb = Argb(light.Color);
            Properties.Settings.Default.checkerboardDarkColorArgb = Argb(dark.Color);
            Properties.Settings.Default.Save(); editor.ApplyCheckerboardColorsFromSettings(); canvas.Invalidate();
        }
    }
}
