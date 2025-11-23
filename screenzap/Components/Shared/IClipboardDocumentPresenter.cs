using System;
using System.Windows.Forms;

namespace screenzap.Components.Shared
{
    internal interface IClipboardDocumentPresenter : IDisposable
    {
        Control View { get; }
        string DisplayName { get; }
        void AttachHostServices(EditorHostServices services);
        bool CanHandleClipboard(IDataObject dataObject);
        void LoadFromClipboard(IDataObject dataObject);
        bool CanExecute(EditorCommandId commandId);
        bool TryExecute(EditorCommandId commandId);
        void OnActivated();
        void OnDeactivated();
    }
}
