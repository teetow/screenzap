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
        public void DirectCropButton_InvokesCropAndUndo_AndPreservesSelectionRequirements()
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                var operations = Strip(kit.Editor, "mainToolStrip");
                var crop = Assert.IsAssignableFrom<ToolStripButton>(operations.Items["cropToolStripButton"]);
                Assert.Equal(ToolStripItemPlacement.Main, crop.Placement);
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
        public void PerspectiveRailButton_HasDistinctIconAndTooltip_AndActivatesTool()
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                var rail = Strip(kit.Editor, "toolsToolStrip");
                var perspective = Assert.IsType<IconToolStripButton>(rail.Items["straightenToolStripButton"]);
                Assert.Equal("Perspective", perspective.Text);
                Assert.Equal(ToolStripItemDisplayStyle.Image, perspective.DisplayStyle);
                Assert.Contains("perspective", perspective.ToolTipText);
                Assert.Equal(40, rail.Width);
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
                Assert.DoesNotContain(historyBar.Items.Cast<ToolStripItem>(), i => i is ToolStripDropDownItem);
                var duplicate = historyBar.Items.Cast<ToolStripItem>().Single(i => i.Tag is EditorCommandId id && id == EditorCommandId.Duplicate);
                int countBefore = kit.Host.HistoryStore.Items.Count;
                duplicate.PerformClick();
                Assert.Equal(countBefore + 1, kit.Host.HistoryStore.Items.Count);
            });
        }

        [Theory]
        [InlineData(0.3)]
        [InlineData(1.125)]
        [InlineData(2.75)]
        public void DirectCommit_PreservesZoomPanAndTransparency_AfterIdle(double zoom)
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(1000, 700));
                EditorFixture.PinModifiers(kit.Editor);
                kit.LoadCanvas(1600, 1200, Color.White);
                using var layer = EditorFixture.Canvas(20, 10, Color.Red);
                kit.PasteImage(layer);
                kit.Editor.TestSetZoom((decimal)zoom);
                kit.Editor.TestPanViewportBy(new Size(137, -83));
                kit.Editor.TestFireKeyDown(Keys.M);
                kit.PumpUi();
                var before = kit.Editor.ViewportDiagnostics;
                bool alphaBefore = kit.Editor.TestAlphaViewEnabled;
                var hostBoundsBefore = kit.Host!.Bounds;
                var fileBar = Assert.Single(kit.Host.Controls.OfType<ToolStrip>().Where(s => s.AccessibleName == "File actions"));
                var commit = Assert.IsType<IconToolStripButton>(fileBar.Items["CommitEditsButton"]);
                Assert.Equal(ToolStripItemPlacement.Main, commit.Placement);
                Assert.True(commit.Enabled);
                commit.PerformClick();
                kit.PumpUi();
                var after = kit.Editor.ViewportDiagnostics;
                Assert.Equal(before.ZoomLevel, after.ZoomLevel);
                Assert.Equal(before.PanOffset, after.PanOffset);
                Assert.Equal(before.ImageClientRectangle, after.ImageClientRectangle);
                Assert.Equal(alphaBefore, kit.Editor.TestAlphaViewEnabled);
                Assert.Equal(hostBoundsBefore, kit.Host.Bounds);
                Assert.False(kit.Host.HistoryStore.ActiveItem!.IsDirty);
                Assert.False(commit.Enabled);
                Assert.Equal(1, kit.Editor.ImageLayerCountForTests);
                Assert.True(((IClipboardDocumentPresenter)kit.Editor).TryExecute(EditorCommandId.Undo));
                Assert.Equal(0, kit.Editor.ImageLayerCountForTests);
                Assert.True(kit.Host.HistoryStore.ActiveItem.IsDirty);
                Assert.True(((IClipboardDocumentPresenter)kit.Editor).TryExecute(EditorCommandId.Redo));
                Assert.Equal(1, kit.Editor.ImageLayerCountForTests);
                Assert.False(kit.Host.HistoryStore.ActiveItem.IsDirty);
            });
        }

        [Fact]
        public void CommonOperations_AreDirectButtons_AndHistoryControlsFitWithoutPopups()
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                kit.Drag(new Point(10, 10), new Point(50, 40));
                var operations = Strip(kit.Editor, "mainToolStrip");
                Assert.DoesNotContain(operations.Items.Cast<ToolStripItem>(), i => i is ToolStripDropDownItem);
                foreach (var name in new[] { "cropToolStripButton", "resizeImageToolStripButton", "replaceToolStripButton", "colorCorrectToolStripButton", "deJpegButton" })
                {
                    var button = Assert.IsAssignableFrom<ToolStripButton>(operations.Items[name]);
                    Assert.Equal(ToolStripItemPlacement.Main, button.Placement);
                    Assert.True(operations.DisplayRectangle.Contains(button.Bounds), name);
                }
                var historyBar = Strip(kit.Host!, "historyActionsToolStrip");
                historyBar.Parent!.Width = 72;
                kit.PumpUi();
                Assert.DoesNotContain(historyBar.Items.Cast<ToolStripItem>(), i => i is ToolStripDropDownItem);
                foreach (ToolStripItem item in historyBar.Items)
                    Assert.True(historyBar.DisplayRectangle.Contains(item.Bounds), item.Text);
            });
        }

        [Theory]
        [InlineData(72)]
        [InlineData(93)]
        [InlineData(140)]
        public void PopulatedHistory_HeaderDoesNotCoverThumbnails_AndListFitsPane(int paneWidth)
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                var header = Strip(kit.Host!, "historyActionsToolStrip");
                var panel = header.Parent!;
                panel.Width = paneWidth;
                using var thumbnail = EditorFixture.Canvas(80, 80, Color.Blue);
                for (int i = 0; i < 40; i++)
                    kit.Host!.HistoryStore.AddObservedImage(thumbnail);
                kit.PumpUi();
                var flow = Assert.Single(panel.Controls.OfType<FlowLayoutPanel>());
                Assert.True(flow.VerticalScroll.Visible);
                Assert.False(flow.HorizontalScroll.Visible);
                Assert.True(flow.Top >= header.Bottom);
                Assert.Equal(panel.ClientSize.Width, header.Width);
                foreach (ToolStripItem item in header.Items)
                    Assert.True(header.DisplayRectangle.Contains(item.Bounds), item.Text);
                foreach (Control button in flow.Controls)
                    Assert.True(button.Right + flow.Padding.Right <= flow.ClientSize.Width,
                        $"Thumbnail {button.Bounds}, list client {flow.ClientSize}, padding {flow.Padding}");
                foreach (var item in kit.Host!.HistoryStore.Items)
                {
                    if (item.Thumbnail == null) continue;
                    var button = flow.Controls.Cast<Control>().Single(c =>
                        ReferenceEquals(c.GetType().GetProperty("Item")!.GetValue(c), item));
                    Assert.True(item.Thumbnail.Width <= button.Width - 8);
                    Assert.True(item.Thumbnail.Height <= button.Height - 8);
                }
                flow.AutoScrollPosition = new Point(0, 500);
                kit.PumpUi();
                Assert.False(flow.HorizontalScroll.Visible);
                Assert.Equal(0, header.Top);
            });
        }

        [Fact]
        public void ActualCleanupPopup_FitsItsFullLabelAndWorkingArea()
        {
            StaTest.Run(() =>
            {
                using var kit = new UiTestKit(new Size(900, 600));
                kit.LoadCanvas(120, 80, Color.White);
                kit.Drag(new Point(10, 10), new Point(50, 40));
                var menu = kit.Host!.MainMenuStrip!;
                var tools = Assert.IsType<ToolStripMenuItem>(menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "&Tools"));
                tools.ShowDropDown();
                var cleanup = Assert.IsType<ToolStripMenuItem>(tools.DropDownItems.Cast<ToolStripItem>().Single(i => i.Text == "Cleanup"));
                cleanup.ShowDropDown();
                kit.PumpUi();
                var command = Assert.IsType<ToolStripMenuItem>(Assert.Single(cleanup.DropDownItems.Cast<ToolStripItem>()));
                Assert.Equal("Replace Background", command.Text);
                Assert.True(command.Enabled);
                Assert.True(cleanup.DropDown.ClientRectangle.Contains(command.Bounds), $"Popup {cleanup.DropDown.ClientRectangle}; item {command.Bounds}");
                int textWidth = TextRenderer.MeasureText(command.Text, command.Font, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                Assert.True(command.ContentRectangle.Width >= textWidth);
                Assert.True(Screen.FromControl(cleanup.DropDown).WorkingArea.Contains(cleanup.DropDown.Bounds));
                cleanup.HideDropDown();
                tools.HideDropDown();
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
