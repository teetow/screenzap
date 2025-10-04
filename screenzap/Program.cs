using System;
using System.Threading;
using System.Windows.Forms;

namespace screenzap
{
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        private static string mutexId = "ScreenZapBackgroundProcess";

        static Mutex mutex = new Mutex(true, mutexId);

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            if (mutex.WaitOne(TimeSpan.Zero, true))
            {
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Screenzap());
            }
            else
            {
                MessageBox.Show("ScreenZap was launched, but is already running. Don't cross the streams.");
            }
        }
    }
}
