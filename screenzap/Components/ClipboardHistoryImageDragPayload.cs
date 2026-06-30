using System;
using System.Drawing;
using System.Windows.Forms;

namespace screenzap.Components
{
    /// <summary>
    /// In-process drag payload for an image history row. The payload owns a full-resolution
    /// snapshot so the source item can change (or be disposed) without invalidating the drag.
    /// </summary>
    internal sealed class ClipboardHistoryImageDragPayload : IDisposable
    {
        internal const string DataFormat = "Screenzap.ClipboardHistory.Image";

        private ClipboardHistoryImageDragPayload(Bitmap image)
        {
            Image = image;
        }

        internal Bitmap Image { get; }

        internal static ClipboardHistoryImageDragPayload? Create(ClipboardHistoryItem? item)
        {
            if (item?.Kind != ClipboardItemKind.Image)
            {
                return null;
            }

            var source = item.PreviewComposite ?? item.CurrentImage;
            return source == null
                ? null
                : new ClipboardHistoryImageDragPayload(new Bitmap(source));
        }

        internal DataObject CreateDataObject()
        {
            var data = new DataObject();
            data.SetData(DataFormat, false, this);
            return data;
        }

        internal static bool TryGetImage(IDataObject? data, out Image? image)
        {
            image = null;
            if (data?.GetDataPresent(DataFormat, false) != true)
            {
                return false;
            }

            if (data.GetData(DataFormat, false) is not ClipboardHistoryImageDragPayload payload)
            {
                return false;
            }

            image = payload.Image;
            return true;
        }

        public void Dispose()
        {
            Image.Dispose();
        }
    }
}
