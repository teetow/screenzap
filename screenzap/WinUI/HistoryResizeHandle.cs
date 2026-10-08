using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace screenzap.WinUI;

internal sealed class HistoryResizeHandle : Microsoft.UI.Xaml.Controls.UserControl, IDisposable
{
    private readonly InputSystemCursor cursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    private uint? pointer;
    private double lastY;
    internal event Action<double>? Dragged;

    internal HistoryResizeHandle()
    {
        IsTabStop = true;
        ProtectedCursor = cursor;
        var grip = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Gray), Width = 40, Height = 2,
            CornerRadius = new CornerRadius(1), HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
        };
        var area = new Microsoft.UI.Xaml.Controls.Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 47, 51)) };
        area.Children.Add(grip);
        Content = area;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "Resize history drawer");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, "HistoryResizeHandle");
        ToolTipService.SetToolTip(this, "Drag to resize history");
        PointerPressed += (_, e) =>
        {
            if (pointer != null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (!CapturePointer(e.Pointer)) return;
            pointer = e.Pointer.PointerId;
            lastY = e.GetCurrentPoint(null).Position.Y;
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (pointer != e.Pointer.PointerId) return;
            double y = e.GetCurrentPoint(null).Position.Y;
            Dragged?.Invoke(y - lastY);
            lastY = y;
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (pointer != e.Pointer.PointerId) return;
            Dragged?.Invoke(e.GetCurrentPoint(null).Position.Y - lastY);
            pointer = null;
            ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) => pointer = null;
        KeyDown += (_, e) =>
        {
            if (e.Key is not (VirtualKey.Up or VirtualKey.Down)) return;
            Dragged?.Invoke(e.Key == VirtualKey.Up ? -16 : 16);
            e.Handled = true;
        };
    }
    public void Dispose() { ReleasePointerCaptures(); ProtectedCursor = null; cursor.Dispose(); }
}
