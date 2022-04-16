using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace screenzap
{
    public sealed class ClipboardMonitor : IDisposable
    {
        /// <summary>
        /// Places the given window in the system-maintained clipboard format listener list.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        /// <summary>
        /// Removes the given window from the system-maintained clipboard format listener list.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        /// <summary>
        /// Sent when the contents of the clipboard have changed.
        /// </summary>
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        private class Window : NativeWindow, IDisposable
        {
            public Window()
            {
                // create the handle for the window.
                this.CreateHandle(new CreateParams());
                AddClipboardFormatListener(this.Handle);
            }

            /// <summary>
            /// Overridden to get the notifications.
            /// </summary>
            /// <param name="m"></param>
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                if (m.Msg == WM_CLIPBOARDUPDATE)
                {
                    IDataObject iData = Clipboard.GetDataObject();      // Clipboard's data.

                    /* Depending on the clipboard's current data format we can process the data differently. 
                     * Feel free to add more checks if you want to process more formats. */
                    if (iData.GetDataPresent(DataFormats.Text))
                    {
                        string text = (string)iData.GetData(DataFormats.Text);
                        OnUpdateText(this, text);
                    }
                    else if (iData.GetDataPresent(DataFormats.Bitmap))
                    {
                        Bitmap image = (Bitmap)iData.GetData(DataFormats.Bitmap);
                        OnUpdateImage(this, image);
                    }
                }
            }
            public event EventHandler<string> OnUpdateText;
            public event EventHandler<Bitmap> OnUpdateImage;
                public void Dispose()
            {
                RemoveClipboardFormatListener(this.Handle);
                this.DestroyHandle();
            }
        }

        private Window _window;
        public ClipboardMonitor()
        {
            _window = new Window();
            _window.OnUpdateImage += _window_OnUpdateImage;
            _window.OnUpdateText += _window_OnUpdateText;
        }

        public bool isListening = false;

        private void _window_OnUpdateText(object sender, string e)
        {
            if (isListening)
                OnUpdateText?.Invoke(sender, e);
        }

        private void _window_OnUpdateImage(object sender, Bitmap e)
        {
            if (isListening)
                OnUpdateImage?.Invoke(sender, e);
        }

        public event EventHandler<string> OnUpdateText;
        public event EventHandler<Bitmap> OnUpdateImage;
        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                    _window.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}