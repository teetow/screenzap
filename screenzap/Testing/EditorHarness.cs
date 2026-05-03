using screenzap.Components;
using screenzap.Components.Shared;
using screenzap.lib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ImagePresenter = screenzap.ImageEditor;
using TextPresenter = screenzap.TextEditor;

namespace screenzap.Testing
{
    internal static class EditorHarness
    {
        private static readonly Size HarnessImageSize = new Size(96, 64);
        private static readonly Rectangle HarnessImageBounds = new Rectangle(Point.Empty, HarnessImageSize);

        public static int Run()
        {
            Logger.Log("Editor harness starting...");
            using var imagePresenter = new ImagePresenter();
            using var textPresenter = new TextPresenter();
            using var host = new ClipboardEditorHostForm(imagePresenter, textPresenter)
            {
                SuppressActivation = true,
                ShowInTaskbar = false
            };

            host.CreateControl();

            var failures = new List<string>();
            ValidateTextFlow(host, failures);
            ValidateImageFlow(host, imagePresenter, failures);
            ValidateHostServices(failures);

            if (failures.Count == 0)
            {
                Logger.Log("Editor harness completed successfully.");
                return 0;
            }

            foreach (var failure in failures)
            {
                Logger.Log($"Editor harness failure: {failure}");
            }

            return 1;
        }

        private static void ValidateTextFlow(ClipboardEditorHostForm host, List<string> failures)
        {
            var data = CreateTextData();
            if (!host.TryShowClipboardData(data))
            {
                failures.Add("Text presenter rejected Unicode text payload.");
                return;
            }

            if (host.ActivePresenter is not TextPresenter)
            {
                failures.Add("Text presenter was not activated after loading text data.");
                return;
            }

            ValidateCommand(host, EditorCommandId.Find, failures, "Text presenter");
            ValidateCommand(host, EditorCommandId.Copy, failures, "Text presenter");
        }

        private static void ValidateImageFlow(ClipboardEditorHostForm host, ImagePresenter imagePresenter, List<string> failures)
        {
            var data = CreateImageData();
            if (!host.TryShowClipboardData(data))
            {
                failures.Add("Image presenter rejected bitmap payload.");
                return;
            }

            ProcessPendingUi();

            if (host.ActivePresenter is not ImagePresenter)
            {
                failures.Add("Image presenter was not activated after loading bitmap data.");
                return;
            }

            ValidateCommand(host, EditorCommandId.Copy, failures, "Image presenter");

            imagePresenter.ResetZoom();
            imagePresenter.HandleResize();
            ProcessPendingUi();

            ValidateViewportDiagnostics(imagePresenter, failures);
            ValidateSelectionDiagnostics(imagePresenter, failures);
            ValidateImageLayerPasteFlow(host, imagePresenter, failures);
        }

        private static Bitmap CreateHarnessImage()
        {
            var bmp = new Bitmap(HarnessImageSize.Width, HarnessImageSize.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
            }
            return bmp;
        }

        private static void ValidateImageLayerPasteFlow(ClipboardEditorHostForm host, ImagePresenter editor, List<string> failures)
        {
            const string label = "Image layer paste";

            // Seed a history item and activate it so commit has a target. TryShowClipboardData
            // (used earlier) loads into the presenter but doesn't add to the history store.
            using var seed = CreateHarnessImage();
            var seededItem = host.HistoryStore.AddObservedImage(seed);
            if (!host.ActivateHistoryItem(seededItem))
            {
                failures.Add($"{label}: failed to activate seeded history item.");
                return;
            }
            ProcessPendingUi();

            if (editor.ImageLayerCountForTests != 0)
            {
                failures.Add($"{label}: expected 0 layers before paste, got {editor.ImageLayerCountForTests}.");
                return;
            }

            using var pasted = new Bitmap(20, 14);
            using (var g = Graphics.FromImage(pasted))
            {
                g.Clear(Color.Magenta);
            }
            editor.SetInternalClipboardImageForDiagnostics(pasted);

            if (!editor.PasteFromClipboardForDiagnostics())
            {
                failures.Add($"{label}: paste returned false.");
                return;
            }
            ProcessPendingUi();

            if (editor.ImageLayerCountForTests != 1)
            {
                failures.Add($"{label}: expected 1 layer after paste, got {editor.ImageLayerCountForTests}.");
                return;
            }

            var frame = editor.GetImageLayerFrameForTests(0);
            // Harness canvas is 96x64; 20x14 layer should center near (38,25).
            if (Math.Abs(frame.X - 38f) > 0.5f || Math.Abs(frame.Y - 25f) > 0.5f)
            {
                failures.Add($"{label}: unexpected layer frame {frame}.");
            }

            using (var composite = editor.BuildCompositeImageForTests())
            {
                var center = composite.GetPixel(48, 32);
                if (center.ToArgb() != Color.Magenta.ToArgb())
                {
                    failures.Add($"{label}: composite pixel at canvas center was {center}, expected Magenta — layer not baked.");
                }
                var corner = composite.GetPixel(0, 0);
                if (corner.ToArgb() == Color.Magenta.ToArgb())
                {
                    failures.Add($"{label}: composite corner is Magenta — layer leaked outside its frame.");
                }
            }

            // Commit through the real host pipeline (stash → flatten → MarkClean → reload).
            if (!host.ExecuteCommandForDiagnostics(EditorCommandId.CommitEdits))
            {
                failures.Add($"{label}: CommitEdits returned false.");
                return;
            }
            ProcessPendingUi();

            if (editor.ImageLayerCountForTests != 0)
            {
                failures.Add($"{label}: expected 0 layers after commit, got {editor.ImageLayerCountForTests}.");
            }

            using (var afterCommit = editor.CloneBaseBitmapForTests())
            {
                if (afterCommit == null)
                {
                    failures.Add($"{label}: no base bitmap after commit.");
                    return;
                }
                var center = afterCommit.GetPixel(48, 32);
                if (center.ToArgb() != Color.Magenta.ToArgb())
                {
                    failures.Add($"{label}: post-commit base center was {center}, expected Magenta — flatten did not bake layer into baseline.");
                }
            }

            // Undo across the commit boundary should unflatten: layer disappears, base reverts to pre-paste.
            var presenter = (IClipboardDocumentPresenter)editor;
            if (!presenter.CanExecute(EditorCommandId.Undo))
            {
                failures.Add($"{label}: undo not available after commit — UndoSnapshot lost.");
                return;
            }
            if (!presenter.TryExecute(EditorCommandId.Undo))
            {
                failures.Add($"{label}: undo execution failed after commit.");
                return;
            }
            ProcessPendingUi();

            using (var afterUndo = editor.CloneBaseBitmapForTests())
            {
                if (afterUndo == null)
                {
                    failures.Add($"{label}: no base bitmap after undo.");
                    return;
                }
                var center = afterUndo.GetPixel(48, 32);
                if (center.ToArgb() == Color.Magenta.ToArgb())
                {
                    failures.Add($"{label}: post-undo base still Magenta at center — undo across commit failed to unflatten.");
                }
            }

            Logger.Log($"{label} flow validated through host commit pipeline.");

            // Validate the screen-surface paint at non-default zoom: the layer must shift with pan/zoom.
            ValidateImageLayerScreenRender(editor, failures);

            // Validate Move-tool selection / drag / resize / delete on a fresh layer.
            ValidateImageLayerMoveTool(editor, failures);
        }

        private static void ValidateImageLayerMoveTool(ImagePresenter editor, List<string> failures)
        {
            const string label = "Image layer move-tool";

            // Start clean: drop any leftover layers from prior validations by undoing back to baseline.
            var presenter = (IClipboardDocumentPresenter)editor;
            while (presenter.CanExecute(EditorCommandId.Undo))
            {
                if (!presenter.TryExecute(EditorCommandId.Undo)) break;
            }
            if (editor.ImageLayerCountForTests != 0)
            {
                failures.Add($"{label}: failed to clear pre-existing layers via undo (got {editor.ImageLayerCountForTests}).");
                return;
            }

            using var pasted = new Bitmap(20, 14);
            using (var g = Graphics.FromImage(pasted))
            {
                g.Clear(Color.Orange);
            }
            editor.SetInternalClipboardImageForDiagnostics(pasted);
            if (!editor.PasteFromClipboardForDiagnostics())
            {
                failures.Add($"{label}: paste returned false.");
                return;
            }

            var initialFrame = editor.GetImageLayerFrameForTests(0);

            // Hit-test: click body should select.
            var bodyPoint = new Point((int)(initialFrame.X + initialFrame.Width / 2), (int)(initialFrame.Y + initialFrame.Height / 2));
            if (!editor.BeginLayerInteractionForTests(bodyPoint))
            {
                failures.Add($"{label}: body hit-test failed at {bodyPoint}.");
                return;
            }
            if (editor.SelectedLayerIndexForTests != 0)
            {
                failures.Add($"{label}: expected selectedLayerIndex=0 after body hit, got {editor.SelectedLayerIndexForTests}.");
                editor.EndLayerInteractionForTests();
                return;
            }

            // Drag-translate.
            editor.UpdateLayerInteractionForTests(new Point(bodyPoint.X + 7, bodyPoint.Y + 5));
            var movedFrame = editor.GetImageLayerFrameForTests(0);
            if (System.Math.Abs(movedFrame.X - (initialFrame.X + 7)) > 0.001f
                || System.Math.Abs(movedFrame.Y - (initialFrame.Y + 5)) > 0.001f)
            {
                failures.Add($"{label}: drag translation incorrect (expected dx=7,dy=5 from {initialFrame}, got {movedFrame}).");
                editor.EndLayerInteractionForTests();
                return;
            }
            editor.EndLayerInteractionForTests();

            // Undo restores frame.
            if (!presenter.TryExecute(EditorCommandId.Undo))
            {
                failures.Add($"{label}: undo of drag returned false.");
                return;
            }
            var afterUndo = editor.GetImageLayerFrameForTests(0);
            if (System.Math.Abs(afterUndo.X - initialFrame.X) > 0.001f || System.Math.Abs(afterUndo.Y - initialFrame.Y) > 0.001f)
            {
                failures.Add($"{label}: undo did not restore translation (got {afterUndo}, expected {initialFrame}).");
                return;
            }

            // Re-select then drag the bottom-right corner handle to resize.
            if (!editor.BeginLayerInteractionForTests(bodyPoint))
            {
                failures.Add($"{label}: re-select body hit failed.");
                return;
            }
            editor.EndLayerInteractionForTests();

            var corner = new Point((int)initialFrame.Right, (int)initialFrame.Bottom);
            if (!editor.BeginLayerInteractionForTests(corner))
            {
                failures.Add($"{label}: bottom-right handle hit failed at {corner}.");
                return;
            }
            editor.UpdateLayerInteractionForTests(new Point(corner.X + 6, corner.Y + 4));
            var resized = editor.GetImageLayerFrameForTests(0);
            if (System.Math.Abs(resized.Width - (initialFrame.Width + 6)) > 0.001f
                || System.Math.Abs(resized.Height - (initialFrame.Height + 4)) > 0.001f)
            {
                failures.Add($"{label}: resize incorrect (got W={resized.Width} H={resized.Height}, expected +6/+4 from {initialFrame.Width}x{initialFrame.Height}).");
                editor.EndLayerInteractionForTests();
                return;
            }
            editor.EndLayerInteractionForTests();

            // Undo restores size.
            if (!presenter.TryExecute(EditorCommandId.Undo))
            {
                failures.Add($"{label}: undo of resize returned false.");
                return;
            }
            var afterResizeUndo = editor.GetImageLayerFrameForTests(0);
            if (System.Math.Abs(afterResizeUndo.Width - initialFrame.Width) > 0.001f
                || System.Math.Abs(afterResizeUndo.Height - initialFrame.Height) > 0.001f)
            {
                failures.Add($"{label}: undo did not restore resize.");
                return;
            }

            Logger.Log($"{label}: select / translate / resize / undo flow OK.");
        }

        private static void ValidateImageLayerScreenRender(ImagePresenter editor, List<string> failures)
        {
            const string label = "Image layer screen render";

            // Re-paste to get a layer back on the canvas.
            using var pasted = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(pasted))
            {
                g.Clear(Color.Cyan);
            }
            editor.SetInternalClipboardImageForDiagnostics(pasted);
            if (!editor.PasteFromClipboardForDiagnostics())
            {
                failures.Add($"{label}: paste returned false.");
                return;
            }
            ProcessPendingUi();

            // Render the screen-surface paint pass into an offscreen bitmap at 2x zoom.
            var metrics = editor.ViewportDiagnostics;
            using var screenBuffer = new Bitmap((int)metrics.ClientSize.Width, (int)metrics.ClientSize.Height);
            using (var g = Graphics.FromImage(screenBuffer))
            {
                g.Clear(Color.Black);
                editor.RenderScreenLayersForTests(g);
            }

            // Layer is centered on canvas (96x64), so its center sits at canvas (48,32).
            // At zoom 1.0, screen position = pan + (48,32) * 1.
            var sample = new Point(
                (int)(metrics.PanOffset.X + 48),
                (int)(metrics.PanOffset.Y + 32));
            if (sample.X < 0 || sample.Y < 0 || sample.X >= screenBuffer.Width || sample.Y >= screenBuffer.Height)
            {
                failures.Add($"{label}: sample point {sample} fell outside screen buffer.");
                return;
            }
            var pixel = screenBuffer.GetPixel(sample.X, sample.Y);
            if (pixel.ToArgb() != Color.Cyan.ToArgb())
            {
                failures.Add($"{label}: expected Cyan at screen-space sample {sample}, got {pixel}.");
            }

            Logger.Log($"{label}: layer rendered correctly in screen surface at pan={metrics.PanOffset}.");
        }

        private static void ValidateViewportDiagnostics(ImagePresenter imagePresenter, List<string> failures)
        {
            var metrics = imagePresenter.ViewportDiagnostics;
            Logger.Log($"Viewport metrics => zoom {metrics.ZoomLevel}, pan {metrics.PanOffset}, client {metrics.ClientSize}, image {metrics.ImageClientRectangle}.");
            if (!metrics.HasImage)
            {
                failures.Add("Viewport diagnostics did not report an image after load.");
                return;
            }

            if (metrics.ImagePixelSize != HarnessImageSize)
            {
                failures.Add($"Viewport reported unexpected image size {metrics.ImagePixelSize}.");
            }

            if (metrics.ZoomLevel != 1m)
            {
                failures.Add($"Viewport zoom expected 1 but was {metrics.ZoomLevel}.");
            }

            if (metrics.ClientSize.Width <= 0 || metrics.ClientSize.Height <= 0)
            {
                failures.Add("Viewport client area was not initialized.");
            }

            var expectedScaledWidth = metrics.ImagePixelSize.Width * (float)metrics.ZoomLevel;
            var expectedScaledHeight = metrics.ImagePixelSize.Height * (float)metrics.ZoomLevel;

            if (!IsNearlyEqual(metrics.ScaledImageSize.Width, expectedScaledWidth) ||
                !IsNearlyEqual(metrics.ScaledImageSize.Height, expectedScaledHeight))
            {
                failures.Add("Scaled image size no longer matches zoom level.");
            }

            if (!IsNearlyEqual(metrics.ImageClientRectangle.Width, metrics.ScaledImageSize.Width) ||
                !IsNearlyEqual(metrics.ImageClientRectangle.Height, metrics.ScaledImageSize.Height))
            {
                failures.Add("Image client rectangle drifted from scaled bounds.");
            }

            if (metrics.ImageClientRectangle.Left < -0.5f || metrics.ImageClientRectangle.Top < -0.5f)
            {
                failures.Add("Image viewport rendered outside the client area (negative offset).");
            }

            if (metrics.ImageClientRectangle.Right > metrics.ClientSize.Width + 0.5f ||
                metrics.ImageClientRectangle.Bottom > metrics.ClientSize.Height + 0.5f)
            {
                failures.Add("Image viewport overflowed the client bounds.");
            }
        }

        private static void ValidateSelectionDiagnostics(ImagePresenter imagePresenter, List<string> failures)
        {
            var inBoundsSelection = new Rectangle(10, 8, 24, 18);
            imagePresenter.SetSelectionForDiagnostics(inBoundsSelection);
            ProcessPendingUi();
            var selection = imagePresenter.SelectionDiagnostics;
            Logger.Log($"Selection diagnostics (in-bounds) => selection {selection.Selection}, clamped {selection.ClampedSelection}.");

            if (!selection.HasSelection)
            {
                failures.Add("Selection diagnostics lost in-bounds selection.");
            }

            if (!selection.IsWithinBounds)
            {
                failures.Add("Selection diagnostics marked in-bounds selection as clamped.");
            }

            if (selection.ClampedSelection != inBoundsSelection)
            {
                failures.Add("Selection metrics for in-bounds selection were altered unexpectedly.");
            }

            var outOfBoundsSelection = new Rectangle(-12, -6, 50, 48);
            imagePresenter.SetSelectionForDiagnostics(outOfBoundsSelection);
            ProcessPendingUi();
            selection = imagePresenter.SelectionDiagnostics;
            Logger.Log($"Selection diagnostics (out-of-bounds) => selection {selection.Selection}, clamped {selection.ClampedSelection}.");

            if (selection.ImageBounds != HarnessImageBounds)
            {
                failures.Add("Selection diagnostics reported unexpected image bounds.");
            }

            if (selection.IsWithinBounds)
            {
                failures.Add("Selection diagnostics failed to flag out-of-bounds selection.");
            }

            var expectedClamp = Rectangle.Intersect(outOfBoundsSelection, HarnessImageBounds);
            if (selection.ClampedSelection != expectedClamp)
            {
                failures.Add($"Selection diagnostics did not clamp selection (expected {expectedClamp}, got {selection.ClampedSelection}).");
            }

            imagePresenter.SetSelectionForDiagnostics(Rectangle.Empty);
        }

        private static bool IsNearlyEqual(float a, float b, float tolerance = 0.1f)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        private static void ValidateHostServices(List<string> failures)
        {
            using var stub = new StubPresenter();
            using var host = new ClipboardEditorHostForm(stub)
            {
                SuppressActivation = true,
                ShowInTaskbar = false
            };

            host.CreateControl();
            stub.SetCommandEnabled(EditorCommandId.Reload, true);

            var data = new DataObject();
            data.SetData(StubPresenter.DataFormat, true, "stub payload");

            if (!host.TryShowClipboardData(data))
            {
                failures.Add("Stub presenter was not selected for its custom payload.");
                return;
            }

            if (!Equals(stub.LastLoadedPayload, "stub payload"))
            {
                failures.Add("Stub presenter did not receive expected payload.");
            }

            stub.TriggerReloadIndicator(true);
            ProcessPendingUi();
            if (!host.HasPendingReloadIndicator)
            {
                failures.Add("Host did not surface reload indicator when presenter requested it.");
            }

            stub.TriggerReloadIndicator(false);
            ProcessPendingUi();
            if (host.HasPendingReloadIndicator)
            {
                failures.Add("Host failed to clear reload indicator when presenter reset it.");
            }

            stub.UpdateStatus("Harness ready");
            ProcessPendingUi();
            if (!string.Equals(host.CurrentStatusText, "Harness ready", StringComparison.Ordinal))
            {
                failures.Add("Host status text did not reflect presenter update.");
            }

            stub.UpdateStatus(null);
            ProcessPendingUi();
            if (!string.IsNullOrEmpty(host.CurrentStatusText))
            {
                failures.Add("Host status text was not cleared when presenter sent null status.");
            }

            if (!stub.RequestHostReload())
            {
                failures.Add("Presenter reload request did not result in host command execution.");
            }
            else if (stub.ReloadCommandCount != 1)
            {
                failures.Add("Presenter reload request executed an unexpected number of times.");
            }

            stub.RequestHostFocus();
        }

        private static void ValidateCommand(ClipboardEditorHostForm host, EditorCommandId commandId, List<string> failures, string context)
        {
            if (!host.CanExecuteHostCommand(commandId))
            {
                failures.Add($"{context}: command {commandId} should be enabled but is not.");
                return;
            }

            if (!host.ExecuteHostCommand(commandId))
            {
                failures.Add($"{context}: executing {commandId} returned false.");
            }
        }

        private static IDataObject CreateTextData()
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, true, "Harness sample text\nSecond line");
            return data;
        }

        private static IDataObject CreateImageData()
        {
            var bmp = new Bitmap(HarnessImageSize.Width, HarnessImageSize.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.DarkSlateBlue);
                g.FillRectangle(Brushes.Gold, 10, 10, 30, 30);
                g.DrawString("SZ", SystemFonts.DefaultFont, Brushes.White, new PointF(45, 20));
            }

            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, true, bmp);
            return data;
        }

        private sealed class StubPresenter : IClipboardDocumentPresenter
        {
            internal const string DataFormat = "screenzap-harness-stub";

            private readonly Panel view = new Panel { Size = new Size(50, 50) };
            private readonly HashSet<EditorCommandId> enabledCommands = new();
            private EditorHostServices? hostServices;

            public object? LastLoadedPayload { get; private set; }
            public int ReloadCommandCount { get; private set; }

            public Control View => view;
            public string DisplayName => "Stub";

            public void AttachHostServices(EditorHostServices services)
            {
                hostServices = services;
            }

            public bool CanHandleClipboard(IDataObject dataObject)
            {
                return dataObject?.GetDataPresent(DataFormat, true) == true;
            }

            public void LoadFromClipboard(IDataObject dataObject)
            {
                LastLoadedPayload = dataObject?.GetData(DataFormat, true);
            }

            public bool CanExecute(EditorCommandId commandId)
            {
                return enabledCommands.Contains(commandId);
            }

            public bool TryExecute(EditorCommandId commandId)
            {
                if (!enabledCommands.Contains(commandId))
                {
                    return false;
                }

                if (commandId == EditorCommandId.Reload)
                {
                    ReloadCommandCount++;
                }

                return true;
            }

            public void OnActivated()
            {
            }

            public void OnDeactivated()
            {
            }

            public bool CanPresent(screenzap.Components.ClipboardHistoryItem item) => false;
            public void LoadHistoryItem(screenzap.Components.ClipboardHistoryItem item) { }
            public void StashHistoryItemState(screenzap.Components.ClipboardHistoryItem item) { }
            public object? GetCurrentContent() => null;

            public void Dispose()
            {
                view.Dispose();
            }

            internal void SetCommandEnabled(EditorCommandId commandId, bool enabled)
            {
                if (enabled)
                {
                    enabledCommands.Add(commandId);
                }
                else
                {
                    enabledCommands.Remove(commandId);
                }
            }

            internal void TriggerReloadIndicator(bool hasPending)
            {
                hostServices?.SetReloadIndicator?.Invoke(hasPending);
            }

            internal void UpdateStatus(string? text)
            {
                hostServices?.UpdateStatusText?.Invoke(text);
            }

            internal bool RequestHostReload()
            {
                return hostServices?.RequestClipboardReload?.Invoke() == true;
            }

            internal void RequestHostFocus()
            {
                hostServices?.FocusHost?.Invoke();
            }
        }

        private static void ProcessPendingUi()
        {
            Application.DoEvents();
        }
    }
}
