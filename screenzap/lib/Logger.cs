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
        private static bool sessionInitialized;

        internal static void Log(string message)
        {
            try
            {
                var payload = $"[{DateTime.Now:O}] {message}{Environment.NewLine}";
                lock (Sync)
                {
                    EnsureSessionHeader();
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

        internal static void StartNewSession(bool clearExisting)
        {
            lock (Sync)
            {
                if (clearExisting)
                {
                    TryResetLogFile();
                }

                sessionInitialized = false;
                EnsureSessionHeader();
            }
        }

        private static void EnsureSessionHeader()
        {
            if (sessionInitialized)
            {
                return;
            }

            Directory.CreateDirectory(LogDirectory);
            var header = $"[{DateTime.Now:O}] === Screenzap session start ==={Environment.NewLine}";
            File.AppendAllText(LogPath, header, Encoding.UTF8);
            sessionInitialized = true;
        }

        private static void TryResetLogFile()
        {
            if (!File.Exists(LogPath))
            {
                return;
            }

            try
            {
                var archivePath = Path.Combine(LogDirectory, $"screenzap_{DateTime.Now:yyyyMMddHHmmss}.prev.log");
                File.Move(LogPath, archivePath, overwrite: false);
            }
            catch
            {
                try
                {
                    File.Delete(LogPath);
                }
                catch
                {
                    // Last resort: leave the old log in place.
                }
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
