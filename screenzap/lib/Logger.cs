using System;
using System.IO;
using System.Text;

namespace screenzap.lib
{
    internal static class Logger
    {
        private static readonly object Sync = new object();
        private static readonly string LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screenzap");
        private static readonly string LogPath = Path.Combine(LogDirectory, "screenzap.log");
        private const long MaxBytes = 1_000_000; // ~1 MB cap

        internal static void Log(string message)
        {
            try
            {
                var payload = $"[{DateTime.Now:O}] {message}{Environment.NewLine}";
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfNeeded();
                    File.AppendAllText(LogPath, payload, Encoding.UTF8);
                }
            }
            catch
            {
                // Swallow logging failures to avoid crashing the app.
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(LogPath))
                {
                    return;
                }

                var info = new FileInfo(LogPath);
                if (info.Length < MaxBytes)
                {
                    return;
                }

                var archivePath = Path.Combine(LogDirectory, $"screenzap_{DateTime.Now:yyyyMMddHHmmss}.log");
                File.Move(LogPath, archivePath, overwrite: false);
            }
            catch
            {
                // Ignore rotation failures; a full log is better than crashing.
            }
        }
    }
}
