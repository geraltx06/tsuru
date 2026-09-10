using System;
using System.IO;

namespace Tsuru
{
    /// <summary>
    /// Minimal append-only diagnostic log. Records failures, plus the one-time
    /// migration notices that mutate a user's configuration, so the file stays
    /// quiet during normal operation.
    /// </summary>
    internal static class Log
    {
        private static readonly object Sync = new object();

        public static string Path
        {
            get { return System.IO.Path.Combine(Settings.ConfigDirectory, "log.txt"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Settings.ConfigDirectory);
                    // Keep the log from growing without bound across sessions.
                    if (File.Exists(Path) && new FileInfo(Path).Length > 256 * 1024)
                        File.Delete(Path);
                    File.AppendAllText(Path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
