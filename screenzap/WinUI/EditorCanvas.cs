using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace screenzap.WinUI;

// A XAML input surface surrounds Win2D's sealed control. This keeps keyboard focus,
// pointer capture and the contextual cursor on one native element.
internal sealed class EditorCanvas : Microsoft.UI.Xaml.Controls.UserControl
{
    internal CanvasControl Renderer { get; } = new() { IsHitTestVisible = false };
    private readonly Dictionary<InputSystemCursorShape, InputSystemCursor> cursors = new();
    private InputSystemCursorShape? current;
    internal EditorCanvas()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var panel = new Microsoft.UI.Xaml.Controls.Grid { AllowDrop = true, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        panel.Children.Add(Renderer);
        Content = panel;
    }
    internal void Invalidate() => Renderer.Invalidate();
    internal void RemoveFromVisualTree() => Renderer.RemoveFromVisualTree();
    internal void SetCursor(InputSystemCursorShape shape)
    {
        if (current == shape) return;
        if (!cursors.TryGetValue(shape, out var cursor)) cursors.Add(shape, cursor = InputSystemCursor.Create(shape));
        ProtectedCursor = cursor; current = shape;
    }
    internal void DisposeCursors() { ProtectedCursor = null; foreach (var cursor in cursors.Values) cursor.Dispose(); cursors.Clear(); }
}
