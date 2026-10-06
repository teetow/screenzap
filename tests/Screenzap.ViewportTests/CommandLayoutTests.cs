using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FontAwesome.Sharp;
using screenzap;
using screenzap.Components.Shared;
using screenzap.Testing;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class CommandLayoutTests
    {
        private static ToolStrip Strip(Control parent, string name)
            => Assert.IsAssignableFrom<ToolStrip>(Assert.Single(parent.Controls.Find(name, true)));

        [Fact]
        public void GeometryFlyout_InvokesCropAndUndo_AndPreservesSelectionRequirements()
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                var operations = Strip(kit.Editor, "mainToolStrip");
                var geometry = Assert.IsType<ToolStripDropDownButton>(operations.Items["geometryCommands"]);
                var crop = Assert.IsAssignableFrom<ToolStripButton>(geometry.DropDownItems["cropToolStripButton"]);
                Assert.False(crop.Enabled);
                kit.Drag(new Point(10, 10), new Point(50, 40));
                Assert.True(crop.Enabled);
                crop.PerformClick();
                Assert.Equal(new Size(40, 30), kit.Editor.ViewportDiagnostics.ImagePixelSize);
                Assert.True(((IClipboardDocumentPresenter)kit.Editor).TryExecute(EditorCommandId.Undo));
                Assert.Equal(new Size(120, 80), kit.Editor.ViewportDiagnostics.ImagePixelSize);
            });
        }

        [Fact]
        public void PerspectiveRailButton_HasVisibleLabelDistinctIcon_AndActivatesTool()
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                var rail = Strip(kit.Editor, "toolsToolStrip");
                var perspective = Assert.IsType<IconToolStripButton>(rail.Items["straightenToolStripButton"]);
                Assert.Equal("Perspective", perspective.Text);
                Assert.Equal(ToolStripItemDisplayStyle.ImageAndText, perspective.DisplayStyle);
                Assert.Equal(IconChar.DrawPolygon, perspective.IconChar);
                Assert.Equal(ToolStripItemPlacement.Main, perspective.Placement);
                perspective.PerformClick();
                Assert.True(kit.Editor.TestIsStraightenToolActive);
                Assert.True(perspective.Checked);
            });
        }

        [Fact]
        public void ShortToolRail_OverflowKeepsPerspectiveReachable()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(120, 80);
                using var owner = new Form
                {
                    ClientSize = new Size(500, 260), ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000)
                };
                owner.Controls.Add(editor);
                owner.Show();
                editor.Show();
                Application.DoEvents();
                var rail = Strip(editor, "toolsToolStrip");
                var perspective = Assert.IsType<IconToolStripButton>(rail.Items["straightenToolStripButton"]);
                Assert.True(rail.OverflowButton.Visible);
                Assert.Equal(ToolStripItemPlacement.Overflow, perspective.Placement);
                rail.OverflowButton.ShowDropDown();
                perspective.PerformClick();
                Assert.True(editor.TestIsStraightenToolActive);
                rail.OverflowButton.HideDropDown();
            });
        }

        [Fact]
        public void HostedFileBarAndImageOperations_AreSeparate_AndHistoryActionsHaveTheirOwnHome()
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                var fileBar = Assert.Single(kit.Host!.Controls.OfType<ToolStrip>().Where(s => s.AccessibleName == "File actions"));
                Assert.Contains(fileBar.Items.Cast<ToolStripItem>(), i => i.Name == "copySvgCommands");
                Assert.DoesNotContain(fileBar.Items.Cast<ToolStripItem>(), i => i.Tag is EditorCommandId id
                    && (id == EditorCommandId.Delete || id == EditorCommandId.Duplicate || id == EditorCommandId.ApplyFloatingPaste));
                Assert.False(Strip(kit.Editor, "documentToolStrip").Visible);
                var operations = Strip(kit.Editor, "mainToolStrip");
                Assert.True(operations.Visible);
                Assert.DoesNotContain(operations.Items.Cast<ToolStripItem>(), i => i.Name == "traceToolStripDropDown");

                var historyBar = Strip(kit.Host, "historyActionsToolStrip");
                var history = Assert.IsType<ToolStripDropDownButton>(historyBar.Items["historyCommands"]);
                var duplicate = history.DropDownItems.Cast<ToolStripItem>().Single(i => i.Text == "Duplicate");
                int countBefore = kit.Host.HistoryStore.Items.Count;
                duplicate.PerformClick();
                Assert.Equal(countBefore + 1, kit.Host.HistoryStore.Items.Count);
            });
        }

        [Fact]
        public void BottomViewControls_ChangeZoomAndTransparency_WithoutEditingPixels()
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                var view = Strip(kit.Editor, "statusStrip");
                view.Items.Cast<ToolStripItem>().Single(i => i.Text == "+").PerformClick();
                Assert.True(kit.Editor.ViewportDiagnostics.ZoomLevel > 1m);
                view.Items.Cast<ToolStripItem>().Single(i => i.Text == "Fit").PerformClick();
                Assert.Equal(1m, kit.Editor.ViewportDiagnostics.ZoomLevel);
                var transparency = Assert.IsType<ToolStripButton>(view.Items.Cast<ToolStripItem>().Single(i => i.Text == "Transparency"));
                Assert.True(transparency.Checked);
                transparency.PerformClick();
                Assert.False(kit.Editor.TestAlphaViewEnabled);
                Assert.False(transparency.Checked);
                Assert.Contains("canUndo=False", kit.Editor.TestDescribeUndoStack());
                Assert.Equal(new Size(120, 80), kit.Editor.ViewportDiagnostics.ImagePixelSize);
            });
        }

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
                using var empty = new ImageEditor();
                Assert.False(((IClipboardDocumentPresenter)empty).CanExecute(EditorCommandId.CopySvgPoster));
            });
        }

        [Fact]
        public void ContextualMergeButton_CommitsLayer_AndUndoRestoresIt()
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                using var layer = EditorFixture.Canvas(20, 10, Color.Red);
                kit.PasteImage(layer);
                var options = Strip(kit.Editor, "layerOptionsToolStrip");
                Assert.True(options.Visible);
                Assert.IsType<ToolStripButton>(options.Items["mergeLayerButton"]).PerformClick();
                Assert.Equal(0, kit.Editor.ImageLayerCountForTests);
                Assert.True(((IClipboardDocumentPresenter)kit.Editor).TryExecute(EditorCommandId.Undo));
                Assert.Equal(1, kit.Editor.ImageLayerCountForTests);
            });
        }
    }
}
