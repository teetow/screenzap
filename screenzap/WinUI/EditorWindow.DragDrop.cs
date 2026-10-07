using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using screenzap.Components;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Button = Microsoft.UI.Xaml.Controls.Button;
using ClipboardHistoryItem = screenzap.Components.ClipboardHistoryItem;
using Orientation = Microsoft.UI.Xaml.Controls.Orientation;

namespace screenzap.WinUI;

internal sealed partial class EditorWindow
{
    private void InitializeNativeDrop()
    {
        canvas.AllowDrop = true;
        canvas.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.Bitmap) || e.DataView.Contains(StandardDataFormats.Text)) e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
        };
        canvas.Drop += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                var point = CanvasPoint(e.GetPosition(canvas));
                if (e.DataView.Contains(StandardDataFormats.Bitmap))
                {
                    var reference = await e.DataView.GetBitmapAsync();
                    using var random = await reference.OpenReadAsync();
                    using var stream = random.AsStreamForRead();
                    using var image = new System.Drawing.Bitmap(stream);
                    if (editor.SurfaceDropImage(image, point)) e.AcceptedOperation = DataPackageOperation.Copy;
                }
                else if (e.DataView.Contains(StandardDataFormats.Text))
                {
                    var emoji = await e.DataView.GetTextAsync();
                    if (editor.SurfaceDropEmoji(emoji, point)) e.AcceptedOperation = DataPackageOperation.Copy;
                }
                inspectorKey = ""; RefreshState(); canvas.Focus(FocusState.Programmatic);
            }
            catch (Exception ex) { lib.Logger.Log($"Image drop failed: {ex}"); }
            finally { e.Handled = true; deferral.Complete(); }
        };
    }
    private void AttachHistoryDrag(Button button, ClipboardHistoryItem item)
    {
        AttachButtonDrag(button);
        InMemoryRandomAccessStream? payload = null;
        button.DragStarting += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                if (ReferenceEquals(item, host.HistoryStore.ActiveItem)) host.ActivePresenter?.CaptureLiveStateInto(item);
                using var snapshot = ClipboardHistoryImageDragPayload.Create(item);
                if (snapshot == null) { e.Cancel = true; return; }
                using var encoded = new MemoryStream(); snapshot.Image.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
                payload?.Dispose(); payload = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(payload)) { writer.WriteBytes(encoded.ToArray()); await writer.StoreAsync(); writer.DetachStream(); }
                payload.Seek(0);
                e.Data.SetBitmap(RandomAccessStreamReference.CreateFromStream(payload));
                e.Data.RequestedOperation = DataPackageOperation.Copy;
            }
            catch (Exception ex) { lib.Logger.Log($"History drag failed: {ex}"); e.Cancel = true; payload?.Dispose(); payload = null; }
            finally { deferral.Complete(); }
        };
        button.DropCompleted += (_, _) => { payload?.Dispose(); payload = null; };
    }
    private bool nativeDragInProgress;
    private void AttachButtonDrag(Button button)
    {
        button.CanDrag = true;
        // Button consumes presses for Click. Observe handled pointer events and ask Windows
        // to start a data drag after a threshold; ordinary clicks and keyboard access still work.
        // Keep the source alive while Windows owns the drag, even when history changes.
        button.DragStarting += (_, _) => nativeDragInProgress = true;
        button.DropCompleted += (_, _) => { nativeDragInProgress = false; RefreshState(); };
        Windows.Foundation.Point? pressed = null;
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(button);
            if (point.Properties.IsLeftButtonPressed) pressed = point.Position;
        }), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => pressed = null), true);
        button.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => pressed = null), true);
        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (_, e) =>
        {
            if (pressed is not { } start || nativeDragInProgress) return;
            var point = e.GetCurrentPoint(button);
            if (!point.Properties.IsLeftButtonPressed) { pressed = null; return; }
            if (Math.Abs(point.Position.X - start.X) < 6 && Math.Abs(point.Position.Y - start.Y) < 6) return;
            pressed = null;
            nativeDragInProgress = true;
            try { await button.StartDragAsync(point); }
            catch (Exception ex) { lib.Logger.Log($"Native drag failed: {ex}"); }
            finally { nativeDragInProgress = false; RefreshState(); }
        }), true);
    }
    private void ShowEmojiFlyout()
    {
        var content = new StackPanel { Spacing = 12, Width = 232 };
        content.Children.Add(new TextBlock { Text = "Emoji", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var flyout = new Flyout { Content = content };
        var tiles = editor.SurfaceEmojiTiles;
        for (int row = 0; row < 2; row++)
        {
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var emoji in tiles.Skip(row * 4).Take(4))
            {
                var button = new Button { Content = emoji, FontSize = 26, Width = 54, Height = 54, CanDrag = true, Padding = new Thickness(6) };
                AttachButtonDrag(button);
                button.Click += (_, _) => { editor.SurfaceAddEmoji(emoji); flyout.Hide(); inspectorKey = ""; RefreshState(); };
                button.DragStarting += (_, e) => { e.Data.SetText(emoji); e.Data.RequestedOperation = DataPackageOperation.Copy; };
                strip.Children.Add(button);
            }
            content.Children.Add(strip);
        }
        var text = new Microsoft.UI.Xaml.Controls.TextBox { PlaceholderText = "Emoji", FontSize = 22 };
        content.Children.Add(text);
        var add = new Button { Content = "Add emoji" };
        add.Click += (_, _) => { if (editor.SurfaceDropEmoji(text.Text, new System.Drawing.Point((int)(canvas.ActualWidth * scale / 2), (int)(canvas.ActualHeight * scale / 2)))) { flyout.Hide(); inspectorKey = ""; RefreshState(); } };
        content.Children.Add(add);
        flyout.ShowAt(commandButtons[Components.Shared.EditorCommandId.EmojiTool]);
    }
}
