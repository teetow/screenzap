using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
using Windows.System;
using Windows.UI.Core;
using Color = Windows.UI.Color;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using TextBox = Microsoft.UI.Xaml.Controls.TextBox;
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;

namespace screenzap.WinUI;

// A Blender-style number field: the label and value sit inside a bar filled from the neutral
// value. Hovering and turning the wheel adjusts it with no focus, so the canvas keeps the
// keyboard; dragging sideways scrubs; a click without a drag types; right-click resets.
// Shift makes fine adjustments and Ctrl coarse ones, as in the other numeric inputs.
internal sealed class ScrubField : Microsoft.UI.Xaml.Controls.UserControl
{
    private const double DragThreshold = 3, PixelsPerStep = 3;
    private static readonly Color TrackColor = Color.FromArgb(255, 46, 49, 54), HoverColor = Color.FromArgb(255, 56, 59, 65), FillColor = Color.FromArgb(255, 92, 74, 44);
    private readonly Grid track = new() { Height = 30, CornerRadius = new CornerRadius(4) };
    private readonly Border fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4) };
    private readonly TextBlock valueText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBox input = new() { Visibility = Visibility.Collapsed, MinHeight = 30, FontSize = 12, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly SolidColorBrush trackBrush = new(TrackColor);
    private readonly Func<double, string> format;
    private double value, dragValue, lastX;
    private bool pressed, dragging, editing;

    internal ScrubField(string label, double value, double minimum, double maximum, double step, double neutral, Func<double, string> format)
    {
        (Minimum, Maximum, Step, Neutral, this.format) = (minimum, maximum, step, neutral, format);
        this.value = Math.Clamp(value, minimum, maximum);
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        track.Background = trackBrush;
        fill.Background = new SolidColorBrush(FillColor);
        track.Children.Add(fill);
        var text = new Grid { Padding = new Thickness(10, 0, 10, 0), IsHitTestVisible = false };
        text.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Opacity = .85 });
        text.Children.Add(valueText);
        track.Children.Add(text);
        track.Children.Add(input);
        Content = track;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, label);
        ToolTipService.SetToolTip(this, "Drag or scroll to adjust (Shift fine, Ctrl coarse). Click to type, right-click to reset.");
        track.SizeChanged += (_, _) => UpdateVisuals();
        PointerEntered += (_, _) => trackBrush.Color = HoverColor;
        PointerExited += (_, _) => { if (!pressed) trackBrush.Color = TrackColor; };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => { pressed = dragging = false; trackBrush.Color = TrackColor; };
        PointerWheelChanged += OnWheel;
        KeyDown += OnKey;
        input.KeyDown += OnInputKey;
        input.LostFocus += (_, _) => EndEdit(true);
        UpdateVisuals();
    }

    internal double Minimum { get; }
    internal double Maximum { get; }
    internal double Step { get; }
    internal double Neutral { get; }
    /// <summary>Raised for user changes only; assigning <see cref="Value"/> is silent.</summary>
    internal event Action<double>? ValueChanged;
    /// <summary>Raised when typing ends, so the host can hand the keyboard back to the canvas.</summary>
    internal event Action? EditFinished;

    internal double Value
    {
        get => value;
        set { this.value = Math.Clamp(value, Minimum, Maximum); UpdateVisuals(); }
    }

    private void Change(double next)
    {
        next = Math.Round(Math.Clamp(next, Minimum, Maximum), 4);
        if (next == value) return;
        value = next;
        UpdateVisuals();
        ValueChanged?.Invoke(value);
    }

    private static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    private double Increment => Step * (Down(VirtualKey.Shift) ? .1 : Down(VirtualKey.Control) ? 10 : 1);
    private static double Quantize(double value, double increment) => Math.Round(value / increment) * increment;

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true; // Never let the inspector scroll from under the pointer.
        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (editing || delta == 0) return;
        int notches = Math.Sign(delta) * Math.Max(1, Math.Abs(delta) / 120);
        double increment = Increment;
        Change(Quantize(value, increment) + notches * increment);
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (editing) return;
        var point = e.GetCurrentPoint(this);
        e.Handled = true;
        if (point.Properties.IsRightButtonPressed) { Change(Neutral); return; }
        if (!point.Properties.IsLeftButtonPressed) return;
        pressed = true; dragging = false;
        lastX = point.Position.X; dragValue = value;
        CapturePointer(e.Pointer);
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!pressed) return;
        double x = e.GetCurrentPoint(this).Position.X;
        if (!dragging && Math.Abs(x - lastX) < DragThreshold) return;
        dragging = true;
        // Accumulate per move rather than from the press point so pressing or releasing
        // Shift/Ctrl mid-drag changes the rate from here on instead of jumping the value.
        double increment = Increment;
        dragValue = Math.Clamp(dragValue + (x - lastX) / PixelsPerStep * increment, Minimum, Maximum);
        lastX = x;
        Change(Quantize(dragValue, increment));
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!pressed) return;
        bool clicked = !dragging;
        pressed = dragging = false;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
        if (clicked) BeginEdit();
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Right or VirtualKey.Up: Change(value + Increment); break;
            case VirtualKey.Left or VirtualKey.Down: Change(value - Increment); break;
            case VirtualKey.Enter: BeginEdit(); break;
            case VirtualKey.Back or VirtualKey.Delete: Change(Neutral); break;
            default: return;
        }
        e.Handled = true;
    }

    private void BeginEdit()
    {
        editing = true;
        input.Text = value.ToString("0.####", CultureInfo.CurrentCulture);
        input.Visibility = Visibility.Visible;
        input.Focus(FocusState.Programmatic);
        input.SelectAll();
    }

    private void OnInputKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Escape)) return;
        e.Handled = true;
        EndEdit(e.Key == VirtualKey.Enter);
    }

    private void EndEdit(bool commit)
    {
        if (!editing) return;
        editing = false;
        input.Visibility = Visibility.Collapsed;
        if (commit && (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double typed) || double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out typed)) && double.IsFinite(typed))
            Change(typed);
        EditFinished?.Invoke();
    }

    private void UpdateVisuals()
    {
        valueText.Text = format(value);
        double width = track.ActualWidth;
        if (width <= 0 || Maximum <= Minimum) return;
        double X(double v) => (v - Minimum) / (Maximum - Minimum) * width;
        double origin = X(Math.Clamp(Neutral, Minimum, Maximum)), current = X(value);
        fill.Margin = new Thickness(Math.Min(origin, current), 0, 0, 0);
        fill.Width = Math.Abs(current - origin);
    }
}
