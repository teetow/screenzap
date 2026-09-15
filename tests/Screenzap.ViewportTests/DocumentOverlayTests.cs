using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using screenzap;
using screenzap.Components;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// The overlay — shapes, texts and floating layers — is one object because it kept not
    /// being one: three parallel properties meant the serializer could be taught about two of
    /// them and not the third, which is exactly how a floating paste came to survive nothing.
    ///
    /// If you add a fourth kind of overlay, the round-trip test below is where it belongs.
    /// </summary>
    public class DocumentOverlayTests
    {
        private static Bitmap SolidBitmap(int width, int height, Color color)
        {
            var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(color);
            return bitmap;
        }

        private static DocumentOverlay PopulatedOverlay()
        {
            return new DocumentOverlay
            {
                Shapes =
                {
                    new AnnotationShape
                    {
                        Id = Guid.NewGuid(),
                        Type = AnnotationType.Arrow,
                        Start = new Point(3, 4),
                        End = new Point(30, 20),
                        LineThickness = 2.5f,
                        ArrowSize = 9f,
                    },
                },
                Texts =
                {
                    new TextAnnotation
                    {
                        Id = Guid.NewGuid(),
                        Position = new Point(7, 8),
                        Text = "hello",
                        FontFamily = "Segoe UI",
                        FontSize = 14f,
                        TextColor = Color.Red,
                        OutlineThickness = 1.5f,
                        OutlineColor = Color.Black,
                    },
                },
                Layers =
                {
                    new ImageLayer(SolidBitmap(8, 6, Color.Lime), new RectangleF(3f, 4f, 8f, 6f))
                    {
                        Name = "Paste 1",
                    },
                },
            };
        }

        private static void WithTempRoot(Action<string> body)
        {
            var root = Path.Combine(Path.GetTempPath(), "screenzap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                body(root);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch
                {
                    // Leftover temp directories must not fail the test.
                }
            }
        }

        [Fact]
        public void Overlay_RoundTripsEveryKindTogether()
        {
            WithTempRoot(root =>
            {
                ClipboardHistoryItem? item = null;
                try
                {
                    using (var canvas = SolidBitmap(60, 40, Color.White))
                    {
                        item = ClipboardHistoryItem.FromImage(canvas);
                    }

                    item.Overlay = PopulatedOverlay();
                    new ClipboardHistoryPersistence(root).Save(new[] { item }, item);

                    var restored = new ClipboardHistoryPersistence(root).Load();
                    try
                    {
                        var overlay = Assert.Single(restored.Items).Overlay;
                        Assert.NotNull(overlay);

                        // Every kind, or the serializer has been taught about a subset again.
                        var shape = Assert.Single(overlay!.Shapes);
                        Assert.Equal(AnnotationType.Arrow, shape.Type);
                        Assert.Equal(new Point(3, 4), shape.Start);
                        Assert.Equal(new Point(30, 20), shape.End);

                        var text = Assert.Single(overlay.Texts);
                        Assert.Equal("hello", text.Text);
                        Assert.Equal(new Point(7, 8), text.Position);
                        Assert.Equal(Color.Red.ToArgb(), text.TextColor.ToArgb());

                        var layer = Assert.Single(overlay.Layers);
                        Assert.Equal("Paste 1", layer.Name);
                        Assert.Equal(new RectangleF(3f, 4f, 8f, 6f), layer.Frame);
                        Assert.Equal(Color.Lime.ToArgb(), layer.Source.GetPixel(4, 3).ToArgb());
                    }
                    finally
                    {
                        foreach (var restoredItem in restored.Items)
                        {
                            restoredItem.Dispose();
                        }
                    }
                }
                finally
                {
                    item?.Dispose();
                }
            });
        }

        [Fact]
        public void Overlay_WithOnlyAnnotations_LoadsWithEmptyLayers()
        {
            WithTempRoot(root =>
            {
                // The shape a manifest written before layers existed has: annotations present,
                // no ImageLayers array at all. It must still load, with nothing floating.
                ClipboardHistoryItem? item = null;
                try
                {
                    using (var canvas = SolidBitmap(20, 20, Color.White))
                    {
                        item = ClipboardHistoryItem.FromImage(canvas);
                    }

                    var overlay = PopulatedOverlay();
                    foreach (var layer in overlay.Layers)
                    {
                        layer.Dispose();
                    }
                    overlay.Layers.Clear();
                    item.Overlay = overlay;

                    new ClipboardHistoryPersistence(root).Save(new[] { item }, item);

                    var restored = new ClipboardHistoryPersistence(root).Load();
                    try
                    {
                        var reloaded = Assert.Single(restored.Items).Overlay;
                        Assert.NotNull(reloaded);
                        Assert.Single(reloaded!.Shapes);
                        Assert.Single(reloaded.Texts);
                        Assert.Empty(reloaded.Layers);
                    }
                    finally
                    {
                        foreach (var restoredItem in restored.Items)
                        {
                            restoredItem.Dispose();
                        }
                    }
                }
                finally
                {
                    item?.Dispose();
                }
            });
        }

        [Fact]
        public void Clone_IsDeep_SoTheCopyCanBeEditedFreely()
        {
            using var original = PopulatedOverlay();
            using var copy = original.Clone();

            Assert.NotSame(original.Layers[0], copy.Layers[0]);
            Assert.NotSame(original.Layers[0].Source, copy.Layers[0].Source);
            Assert.NotSame(original.Shapes[0], copy.Shapes[0]);
            Assert.NotSame(original.Texts[0], copy.Texts[0]);

            copy.Layers[0].Frame = new RectangleF(99f, 99f, 1f, 1f);
            copy.Shapes[0].Start = new Point(99, 99);
            copy.Texts[0].Text = "changed";

            Assert.Equal(new RectangleF(3f, 4f, 8f, 6f), original.Layers[0].Frame);
            Assert.Equal(new Point(3, 4), original.Shapes[0].Start);
            Assert.Equal("hello", original.Texts[0].Text);
        }

        [Fact]
        public void IsEmpty_TracksEveryKind()
        {
            using var empty = new DocumentOverlay();
            Assert.True(empty.IsEmpty);

            using var populated = PopulatedOverlay();
            Assert.False(populated.IsEmpty);
        }
    }
}
