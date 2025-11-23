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
            ValidateImageFlow(host, failures);
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

        private static void ValidateImageFlow(ClipboardEditorHostForm host, List<string> failures)
        {
            var data = CreateImageData();
            if (!host.TryShowClipboardData(data))
            {
                failures.Add("Image presenter rejected bitmap payload.");
                return;
            }

            if (host.ActivePresenter is not ImagePresenter)
            {
                failures.Add("Image presenter was not activated after loading bitmap data.");
                return;
            }

            ValidateCommand(host, EditorCommandId.Copy, failures, "Image presenter");
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
            var bmp = new Bitmap(96, 64);
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
