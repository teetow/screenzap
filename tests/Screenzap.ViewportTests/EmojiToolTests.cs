using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using screenzap;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests;

public class EmojiToolTests
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class RecentScope : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Screenzap-emoji-" + Guid.NewGuid());
        internal string Path => System.IO.Path.Combine(directory, "recent.json");
        internal EmojiRecentStore Store { get; }
        internal RecentScope() => Store = new EmojiRecentStore(Path);
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Recents_AreDistinctBoundedAndSurviveRestart()
    {
        using var scope = new RecentScope();
        foreach (var emoji in new[] { "😀", "😂", "😍", "👍🏽", "❤️", "🎉", "🔥", "👀", "🥳" }) scope.Store.Record(emoji);
        scope.Store.Record("👍🏽");
        var reloaded = new EmojiRecentStore(scope.Path);
        Assert.Equal("👍🏽", reloaded.Tiles[0]);
        Assert.Equal("🥳", reloaded.Tiles[1]);
        Assert.Equal(8, reloaded.Tiles.Length);
        Assert.Equal(8, reloaded.Tiles.Distinct().Count());
        Assert.DoesNotContain("😀", reloaded.Tiles);
    }

    [Theory]
    [InlineData("⭐", true)]
    [InlineData("©", true)]
    [InlineData("🀄", true)]
    [InlineData("👩🏽‍💻", true)]
    [InlineData("1️⃣", true)]
    [InlineData("hello", false)]
    [InlineData("\u200D", false)]
    [InlineData("\uFE0F", false)]
    [InlineData("\uD83D", false)]
    public void EmojiInput_RecognizesWholeEmojiAndRejectsIncompleteInput(string input, bool expected)
        => Assert.Equal(expected, EmojiRecentStore.IsEmoji(input));

    [Fact]
    public void Export_RetainsColorEmoji()
    {
        StaTest.Run(() =>
        {
            using var scope = new RecentScope();
            using var editor = EditorFixture.WithCanvas(300, 200, createControl: true);
            editor.TestSetEmojiRecentStore(scope.Store);
            Assert.True(editor.AddEmojiAtClientPoint("😀", editor.TestImagePixelToClient(new Point(150, 100))));
            using var composite = editor.BuildCompositeImageForTests();
            var annotation = Assert.Single(editor.TestTextAnnotations);
            using var graphics = Graphics.FromImage(composite);
            var bounds = Rectangle.Intersect(annotation.GetBounds(graphics), new Rectangle(Point.Empty, composite.Size));
            int colorfulPixels = 0;
            for (int y = bounds.Top; y < bounds.Bottom; y++)
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    var pixel = composite.GetPixel(x, y);
                    if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) > 60)
                        colorfulPixels++;
                }
            Assert.True(colorfulPixels > 100, $"Expected color emoji in export, found {colorfulPixels} colorful pixels.");
        });
    }

    [Fact]
    public void Recents_InvalidFileFallsBackToStarterEmoji()
    {
        using var scope = new RecentScope();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(scope.Path)!);
        File.WriteAllText(scope.Path, "{broken");
        Assert.Equal(8, new EmojiRecentStore(scope.Path).Tiles.Length);
    }

    [Fact]
    public void Drop_CreatesSelectedEditableTextAndSupportsUndoRedo()
    {
        StaTest.Run(() =>
        {
            using var scope = new RecentScope();
            using var editor = EditorFixture.WithCanvas(1200, 900, createControl: true);
            editor.TestSetEmojiRecentStore(scope.Store);
            editor.TestSetZoom(2m);
            editor.TestPanViewportBy(new Size(-180, -110));
            var viewport = (Control)typeof(ImageEditor).GetField("pictureBox1", Private)!.GetValue(editor)!;
            var point = new Point(160, 140);
            var data = new DataObject();
            data.SetData(EmojiFlyout.DragFormat, "👍🏽");
            var screenPoint = viewport.PointToScreen(point);
            var drag = new DragEventArgs(data, 0, screenPoint.X, screenPoint.Y, DragDropEffects.Copy, DragDropEffects.None);
            typeof(ImageEditor).GetMethod("EmojiDragEnter", Private)!.Invoke(editor, new object?[] { viewport, drag });
            Assert.Equal(DragDropEffects.Copy, drag.Effect);
            typeof(ImageEditor).GetMethod("EmojiDragDrop", Private)!.Invoke(editor, new object?[] { viewport, drag });
            var annotation = Assert.Single(editor.TestTextAnnotations);
            Assert.Equal("👍🏽", annotation.Text);
            Assert.Equal(72f, annotation.FontSize);
            Assert.Equal(0f, annotation.OutlineThickness);
            Assert.True(annotation.Selected);
            Assert.False(annotation.IsEditing);
            Assert.Equal(ActiveTool.None, editor.CurrentTool);
            AssertCentered(editor, annotation, point);
            var presenter = (IClipboardDocumentPresenter)editor;
            Assert.True(presenter.TryExecute(EditorCommandId.Undo));
            Assert.Empty(editor.TestTextAnnotations);
            Assert.True(presenter.TryExecute(EditorCommandId.Redo));
            Assert.Equal("👍🏽", Assert.Single(editor.TestTextAnnotations).Text);
            Assert.True(editor.TestHandleTextToolKeyDown(Keys.Enter));
            Assert.True(editor.TestHandleTextToolKeyPress('!'));
            Assert.Equal("👍🏽!", editor.TestTextAnnotations[0].Text);
        });
    }

    [Fact]
    public void Picker_CompoundEmojiIsOneObjectAtPannedViewportCenter()
    {
        StaTest.Run(() =>
        {
            using var scope = new RecentScope();
            using var editor = EditorFixture.WithCanvas(2400, 1600, createControl: true);
            editor.TestSetEmojiRecentStore(scope.Store);
            editor.TestSetSize(800, 600);
            editor.TestSetZoom(3m);
            editor.TestPanViewportBy(new Size(-350, 200));
            Assert.True(editor.TestBeginEmojiPickerCapture());
            editor.TestSetEmojiPickerInput("👩🏽‍💻");
            editor.CommitEmojiPickerInput();
            var annotation = Assert.Single(editor.TestTextAnnotations);
            Assert.Equal("👩🏽‍💻", annotation.Text);
            var viewport = (Control)typeof(ImageEditor).GetField("pictureBox1", Private)!.GetValue(editor)!;
            AssertCentered(editor, annotation, new Point(viewport.ClientSize.Width / 2, viewport.ClientSize.Height / 2));
            Assert.Equal("👩🏽‍💻", scope.Store.Tiles[0]);
            editor.CommitEmojiPickerInput();
            Assert.Single(editor.TestTextAnnotations);
        });
    }

    [Fact]
    public void CancelledPickerAndSearchText_DoNotChangeDocumentOrRecents()
    {
        StaTest.Run(() =>
        {
            using var scope = new RecentScope();
            using var editor = EditorFixture.WithCanvas(300, 200, createControl: true);
            editor.TestSetEmojiRecentStore(scope.Store);
            editor.TestAddTextAnnotation(new Point(10, 10), "Keep me");
            var before = editor.TestDescribeTextAnnotations();
            var undoBefore = editor.TestDescribeUndoStack();
            Assert.True(editor.TestBeginEmojiPickerCapture());
            editor.TestSetEmojiPickerInput("smile");
            editor.CommitEmojiPickerInput();
            editor.TestEndEmojiPickerCapture();
            Assert.Equal(before, editor.TestDescribeTextAnnotations());
            Assert.Equal(undoBefore, editor.TestDescribeUndoStack());
            Assert.False(File.Exists(scope.Path));
        });
    }

    [Fact]
    public void Flyout_HasNineLargeTilesAndStaysOpenAfterInsertion()
    {
        StaTest.Run(() =>
        {
            using var scope = new RecentScope();
            using var editor = EditorFixture.WithCanvas(400, 300, createControl: true);
            editor.TestSetEmojiRecentStore(scope.Store);
            using var owner = new Form();
            owner.Controls.Add(editor);
            owner.Show();
            editor.Show();
            var button = (ToolStripButton)typeof(ImageEditor).GetField("emojiToolStripButton", Private)!.GetValue(editor)!;
            Assert.Equal(ToolStripItemDisplayStyle.ImageAndText, button.DisplayStyle);
            Assert.Equal(new Size(124, 30), button.Size);
            Assert.Equal("textToolStripButton", button.Owner!.Items[button.Owner.Items.IndexOf(button) - 1].Name);
            button.PerformClick();
            var flyout = (Form)typeof(ImageEditor).GetField("emojiFlyout", Private)!.GetValue(editor)!;
            Assert.True(flyout.Visible);
            var grid = Assert.IsType<TableLayoutPanel>(flyout.Controls[0]);
            Assert.Equal(9, grid.Controls.Count);
            Assert.All(grid.Controls.Cast<Control>(), tile => Assert.True(tile.Width >= 64 && tile.Height >= 64));
            Assert.True(editor.AddEmojiAtClientPoint("🎉", new Point(200, 150)));
            Assert.True(flyout.Visible);
            button.PerformClick();
            Assert.False(flyout.Visible);
        });
    }

    [Fact]
    public void Placeholder_RejectsEmojiAndDisablesToolbar()
    {
        StaTest.Run(() =>
        {
            using var editor = new ImageEditor();
            Assert.False(editor.AddEmojiAtClientPoint("😀", Point.Empty));
            Assert.False(editor.TestBeginEmojiPickerCapture());
            var button = (ToolStripButton)typeof(ImageEditor).GetField("emojiToolStripButton", Private)!.GetValue(editor)!;
            Assert.False(button.Enabled);
        });
    }

    private static void AssertCentered(ImageEditor editor, TextAnnotation annotation, Point clientPoint)
    {
        var viewport = (Control)typeof(ImageEditor).GetField("pictureBox1", Private)!.GetValue(editor)!;
        using var graphics = viewport.CreateGraphics();
        var bounds = annotation.GetBounds(graphics);
        var metrics = editor.ViewportDiagnostics;
        var zoom = (double)((screenzap.Components.Shared.ImageViewportControl)viewport).ZoomLevel;
        var x = (bounds.Left + bounds.Width / 2) * zoom + metrics.PanOffset.X;
        var y = (bounds.Top + bounds.Height / 2) * zoom + metrics.PanOffset.Y;
        Assert.InRange(x, clientPoint.X - zoom, clientPoint.X + zoom);
        Assert.InRange(y, clientPoint.Y - zoom, clientPoint.Y + zoom);
    }
}
