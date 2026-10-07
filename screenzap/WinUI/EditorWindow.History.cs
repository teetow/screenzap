using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using screenzap.Components;
using Button = Microsoft.UI.Xaml.Controls.Button;
using Image = Microsoft.UI.Xaml.Controls.Image;
using Orientation = Microsoft.UI.Xaml.Controls.Orientation;
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;

namespace screenzap.WinUI;

internal sealed partial class EditorWindow
{
    private sealed class HistoryTile
    {
        internal required ClipboardHistoryItem Item;
        internal required Button Button;
        internal required Image Image;
        internal required TextBlock Caption;
        internal bool NeedsThumbnail = true;
        internal double ThumbnailHeight;
        internal int Generation;
        internal bool? Selected;
    }

    private readonly Dictionary<Guid, HistoryTile> historyTiles = new();

    private void SynchronizeHistory()
    {
        var items = host.HistoryStore.Items.ToArray();
        var ids = items.Select(item => item.Id).ToHashSet();
        foreach (var id in historyTiles.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            filmstrip.Children.Remove(historyTiles[id].Button);
            historyTiles.Remove(id);
        }
        double height = Math.Clamp(historyRow.Height.Value - 82, 16, 80);
        for (int index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (!historyTiles.TryGetValue(item.Id, out var tile) || !ReferenceEquals(tile.Item, item))
            {
                if (tile != null) filmstrip.Children.Remove(tile.Button);
                tile = CreateHistoryTile(item);
                historyTiles[item.Id] = tile;
            }
            int position = filmstrip.Children.IndexOf(tile.Button);
            if (position != index)
            {
                if (position >= 0) filmstrip.Children.RemoveAt(position);
                filmstrip.Children.Insert(index, tile.Button);
            }
            tile.Caption.Text = item.CreatedUtc.ToLocalTime().ToString("HH:mm") + (item.IsDirty ? "  •" : "");
            if (tile.NeedsThumbnail || tile.ThumbnailHeight != height) UpdateHistoryThumbnail(tile, height);
        }
        RefreshHistorySelection();
    }

    private HistoryTile CreateHistoryTile(ClipboardHistoryItem item)
    {
        var image = new Image { Width = 96, Stretch = Stretch.Uniform };
        var caption = new TextBlock { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Opacity = .75 };
        var stack = new StackPanel { Spacing = 5, Width = 96 };
        stack.Children.Add(image); stack.Children.Add(caption);
        var button = new Button { Style = ChromeButtonStyle, Content = stack, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top, Padding = new Thickness(5), BorderThickness = new Thickness(1), Background = Brush(34, 36, 40) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"History image {item.CreatedUtc.ToLocalTime():HH:mm}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"HistoryItem-{item.Id}");
        ToolTipService.SetToolTip(button, item.CreatedUtc.ToLocalTime().ToString("g"));
        button.Click += (_, _) =>
        {
            host.ActivateHistoryItem(item);
            inspectorKey = "";
            ResizeCanvas(); RefreshState();
            canvas.Focus(FocusState.Programmatic);
        };
        AttachHistoryDrag(button, item);
        return new HistoryTile { Item = item, Button = button, Image = image, Caption = caption };
    }

    private async void UpdateHistoryThumbnail(HistoryTile tile, double height)
    {
        tile.NeedsThumbnail = false;
        int generation = ++tile.Generation;
        if (tile.ThumbnailHeight != height)
        {
            tile.ThumbnailHeight = height;
            tile.Image.Height = height;
            tile.Button.Height = height + 30;
            tile.Item.RebuildThumbnail(96, (int)height);
        }
        var thumbnail = tile.Item.Thumbnail;
        if (thumbnail == null) return;
        try
        {
            var source = await BitmapSource(thumbnail);
            if (!shuttingDown && generation == tile.Generation && historyTiles.TryGetValue(tile.Item.Id, out var current) && ReferenceEquals(tile, current))
                tile.Image.Source = source;
        }
        catch (Exception ex) { lib.Logger.Log($"History thumbnail failed: {ex.Message}"); }
    }

    private void RefreshHistorySelection()
    {
        foreach (var tile in historyTiles.Values)
        {
            bool selected = ReferenceEquals(tile.Item, host.HistoryStore.ActiveItem);
            if (tile.Selected == selected) continue;
            tile.Selected = selected;
            tile.Button.BorderBrush = selected ? Accent : HistoryBorder;
        }
    }
}
