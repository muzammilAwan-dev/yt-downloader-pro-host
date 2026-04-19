using System;
using System.IO;

namespace YTDLPHost.Services
{
    public static class AppLogger
    {
        private static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YT Downloader Pro");
        private static readonly string LogFile = Path.Combine(LogDir, "debug.log");
        private static readonly object _lock = new object();

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(LogDir))
                    {
                        Directory.CreateDirectory(LogDir);
                    }
                    
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n";
                    File.AppendAllText(LogFile, logEntry);
                }
            }
            catch 
            { 
                // Fail silently. Logging should never be the reason the app crashes.
            }
        }
    }
}
