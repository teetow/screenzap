using System.Drawing;
using screenzap;
using screenzap.Components.Shared;
using Xunit;
namespace Screenzap.ViewportTests;
public class SvgExportTests {

        [Fact]
        public void SvgExport_FindsBundledTracerAndProducesSvg_WithoutWritingClipboard()
        {
            StaTest.Run(() =>
            {
                using var source = EditorFixture.Canvas(20, 10, Color.Red);
                Assert.True(screenzap.lib.ImageTracer.IsAvailable());
                var svg = screenzap.lib.ImageTracer.TraceToSvgAsync(source,
                    screenzap.lib.ImageTracer.TracingPreset.Poster).GetAwaiter().GetResult();
                Assert.NotNull(svg);
                Assert.Contains("<svg", svg);
                using var editor = EditorFixture.WithCanvas(120, 80);
                Assert.True(((IClipboardDocumentPresenter)editor).CanExecute(EditorCommandId.CopySvgPoster));
                using var empty = new ImageDocumentEditor();
                Assert.False(((IClipboardDocumentPresenter)empty).CanExecute(EditorCommandId.CopySvgPoster));
            });
        }

}
