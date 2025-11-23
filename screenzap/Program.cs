using screenzap.lib;
using screenzap.Testing;
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
            if (IsEditorHarnessRequested(args))
            {
                ConfigureApplication();
                var exitCode = EditorHarness.Run();
                Environment.Exit(exitCode);
                return;
            }

            mutex = new Mutex(true, mutexId);

            if (mutex.WaitOne(TimeSpan.Zero, true))
            {
                ConfigureApplication();
                Application.Run(new Screenzap());
            }
            else
            {
                MessageBox.Show("ScreenZap was launched, but is already running. Don't cross the streams.");
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

        private static bool IsEditorHarnessRequested(string[] args)
        {
            if (args != null)
            {
                foreach (var arg in args)
                {
                    if (string.Equals(arg, "--editor-harness", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            var env = Environment.GetEnvironmentVariable("SCREENZAP_EDITOR_HARNESS");
            return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static void ConfigureApplication()
        {
            if (applicationConfigured)
            {
                return;
            }

            applicationConfigured = true;
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => LogUnhandled("UI thread", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogUnhandled("AppDomain", e.ExceptionObject as Exception);
            AppDomain.CurrentDomain.FirstChanceException += (_, e) => Logger.Log($"First chance exception: {e.Exception}");
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Logger.Log("Process exiting");
        }
    }
}
