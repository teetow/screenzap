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
            using var editor = EditorFixture.WithCanvas(300, 200);
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
            using var editor = EditorFixture.WithCanvas(1200, 900);
            editor.TestSetEmojiRecentStore(scope.Store);
            editor.TestSetZoom(2m);
            editor.TestPanViewportBy(new Size(-180, -110));
            var point = new Point(160, 140);
            Assert.True(editor.SurfaceDropEmoji("👍🏽", point));
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
            using var editor = EditorFixture.WithCanvas(2400, 1600);
            editor.TestSetEmojiRecentStore(scope.Store);
            editor.TestSetSize(800, 600);
            editor.TestSetZoom(3m);
            editor.TestPanViewportBy(new Size(-350, 200));
            editor.SurfaceAddEmoji("👩🏽‍💻");
            var annotation = Assert.Single(editor.TestTextAnnotations);
            Assert.Equal("👩🏽‍💻", annotation.Text);
            var viewport = (screenzap.Components.Shared.ImageViewport)typeof(ImageDocumentEditor).GetField("viewport", Private)!.GetValue(editor)!;
            AssertCentered(editor, annotation, new Point(viewport.ClientSize.Width / 2, viewport.ClientSize.Height / 2));
            Assert.Equal("👩🏽‍💻", scope.Store.Tiles[0]);

            Assert.Single(editor.TestTextAnnotations);
        });
    }





    [Fact]
    public void Placeholder_RejectsEmoji() {
        using var editor = new ImageDocumentEditor();
        Assert.False(editor.AddEmojiAtClientPoint("😀", Point.Empty));
        Assert.False(((IClipboardDocumentPresenter)editor).CanExecute(EditorCommandId.EmojiTool));
    }

    private static void AssertCentered(ImageDocumentEditor editor, TextAnnotation annotation, Point clientPoint)
    {
        var viewport = (screenzap.Components.Shared.ImageViewport)typeof(ImageDocumentEditor).GetField("viewport", Private)!.GetValue(editor)!;
        using var graphics = viewport.CreateGraphics();
        var bounds = annotation.GetBounds(graphics);
        var metrics = editor.ViewportDiagnostics;
        var zoom = (double)((screenzap.Components.Shared.ImageViewport)viewport).ZoomLevel;
        var x = (bounds.Left + bounds.Width / 2) * zoom + metrics.PanOffset.X;
        var y = (bounds.Top + bounds.Height / 2) * zoom + metrics.PanOffset.Y;
        Assert.InRange(x, clientPoint.X - zoom, clientPoint.X + zoom);
        Assert.InRange(y, clientPoint.Y - zoom, clientPoint.Y + zoom);
    }
}
