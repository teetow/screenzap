using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Globalization.NumberFormatting;
using Windows.System;
using Windows.UI.Core;

namespace screenzap.WinUI;

internal sealed partial class EditorWindow
{
    private static NumberBox NumericInput(double value, double min, double max, double step = 1, string? header = null)
    {
        var formatter = new DecimalFormatter { FractionDigits = 0, NumberRounder = new IncrementNumberRounder { Increment = .0001 } };
        var box = new NumberBox { NumberFormatter = formatter, Value = value, Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 10, Header = header, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        box.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.Handled || !box.IsEnabled) return;
            int delta = e.GetCurrentPoint(box).Properties.MouseWheelDelta;
            if (delta == 0) return;
            box.Focus(FocusState.Pointer);
            bool shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
            bool control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
            decimal increment = (decimal)box.SmallChange * (shift ? .1m : control ? 10m : 1m);
            decimal current = (decimal)(double.IsFinite(box.Value) ? box.Value : box.Minimum);
            int notches = Math.Sign(delta) * Math.Max(1, Math.Abs(delta) / 120);
            box.Value = (double)Math.Round(Math.Clamp(current + notches * increment, (decimal)box.Minimum, (decimal)box.Maximum), 4);
            e.Handled = true;
        }), true);
        return box;
    }
}
