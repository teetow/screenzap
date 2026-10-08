using System.Drawing;
using screenzap.Components;
using Xunit;

namespace Screenzap.ViewportTests;

public class CaptureSelectionTests
{
    [Theory]
    [InlineData(10, 20, 70, 80, 10, 20, 60, 60)]
    [InlineData(70, 80, 10, 20, 10, 20, 60, 60)]
    [InlineData(-20, -10, 120, 150, 0, 0, 100, 100)]
    [InlineData(150, 150, 200, 200, 0, 0, 0, 0)]
    public void SelectionClipsToFrozenMonitor(int sx, int sy, int ex, int ey, int x, int y, int width, int height)
    {
        var selection = new CaptureSelection(new Size(100, 100));
        selection.Begin(new Point(sx, sy));
        selection.Move(new Point(ex, ey), false, false, false, false);
        Assert.Equal(new Rectangle(x, y, width, height), selection.Finish());
        Assert.False(selection.IsDragging);
    }

    [Theory]
    [InlineData(false, false, 30, 40, 40, 40)]
    [InlineData(true, false, 30, 40, 32, 32)]
    [InlineData(false, true, 0, 20, 70, 40)]
    public void ModifiersSupportSquareGridAndCenter(bool snap, bool center, int x, int y, int width, int height)
    {
        var selection = new CaptureSelection(new Size(100, 100));
        selection.Begin(new Point(30, 40));
        selection.Move(new Point(70, 60), false, center, !center, snap);
        Assert.Equal(new Rectangle(x, y, width, height), selection.Finish());
    }

    [Fact]
    public void SpaceMovesBothEndpoints_WithoutResizing()
    {
        var selection = new CaptureSelection(new Size(300, 200));
        selection.Begin(new Point(20, 30));
        selection.Move(new Point(80, 70), false, false, false, false);
        selection.Move(new Point(100, 100), true, false, false, false);
        Assert.Equal(new Rectangle(40, 60, 60, 40), selection.Finish());
    }

    [Fact]
    public void CancelOrClickProducesNoCapture_AndMovesWithoutDragDoNothing()
    {
        var selection = new CaptureSelection(new Size(100, 100));
        selection.Begin(new Point(10, 10));
        Assert.Equal(Size.Empty, selection.Finish().Size);
        selection.Begin(new Point(10, 10));
        selection.Move(new Point(70, 60), false, false, false, false);
        selection.Cancel();
        selection.Move(new Point(90, 90), false, false, false, false);
        Assert.Equal(Rectangle.Empty, selection.Area);
        Assert.False(selection.IsDragging);
    }
}
