using screenzap.lib;
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
        static Mutex? mutex;
        private static bool applicationConfigured;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            bool smoke = Array.Exists(args, arg => arg == "--winui-smoke");
            mutex = new Mutex(false, mutexId);

            if (smoke || mutex.WaitOne(TimeSpan.Zero, true))
            {
                ConfigureApplication();
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Microsoft.UI.Xaml.Application.Start(parameters =>
                {
                    SynchronizationContext.SetSynchronizationContext(
                        new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
                    _ = new WinUI.ScreenzapApplication();
                });
            }
            else
            {
                // A second tray launch does not need another window or modal prompt.
                Logger.Log("Screenzap is already running.");
            }
        }

        private static void LogUnhandled(string source, Exception? exception)
        {
            if (exception == null)
            {
                Logger.Log($"Unhandled exception reported from {source} with null payload.");
                return;
            }

            Logger.Log($"Unhandled exception on {source}: {exception}");
        }

        private static void ConfigureApplication()
        {
            if (applicationConfigured)
            {
                return;
            }

            applicationConfigured = true;
            SetProcessDPIAware();
            // The XAML dispatcher replaces Application.Run; initialize OLE explicitly for
            // the shared clipboard service and native desktop drag/drop targets.
            _ = Application.OleRequired();
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogUnhandled("AppDomain", e.ExceptionObject as Exception);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Logger.Log("Process exiting");
        }
    }
}
