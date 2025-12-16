using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    public class Logger
    {
        private static string DEBUG_LEVEL = "DEBUG";
         private static string INFO_LEVEL = "INFO";
        private readonly string _logFilePath;
        private readonly object _lock = new object();

        private string loggerLevel = DEBUG_LEVEL; // Default log level

        public Logger(IConfigurationRoot configurationBuilder)
        {
            loggerLevel = configurationBuilder["Logger:Level"] ?? DEBUG_LEVEL;
            string logFileName = configurationBuilder["Logger:LogFileName"] ?? "sync-log.txt";

            // Create logs directory in the application folder
            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);

            // Create log file with timestamp
            string fileName = $"{DateTime.Now:yyyy-MM-dd}_{logFileName}";
            _logFilePath = Path.Combine(logDirectory, fileName);
        }

        public void LogError(string message, Exception ex = null)
        {
            WriteLog("ERROR", message, ex);
        }

        public void LogWarning(string message)
        {
            WriteLog("WARNING", message);
        }

        public void LogInfo(string message)
        {
            if (loggerLevel == INFO_LEVEL || loggerLevel == DEBUG_LEVEL)
            {
                WriteLog(INFO_LEVEL, message);
            }
        }

        public void LogDebug(string message)
        {
            if (loggerLevel == DEBUG_LEVEL)
            {
                WriteLog(DEBUG_LEVEL, message);
            }
        }

        private void WriteLog(string level, string message, Exception ex = null)
        {
            lock (_lock)
            {
                try
                {
                    using (var writer = new StreamWriter(_logFilePath, append: true))
                    {
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        writer.WriteLine($"[{timestamp}] [{level}] {message}");

                        if (ex != null)
                        {
                            writer.WriteLine($"[{timestamp}] [ERROR] Exception: {ex.Message}");
                            writer.WriteLine($"[{timestamp}] [ERROR] StackTrace: {ex.StackTrace}");
                        }

                        writer.WriteLine(); // Empty line for readability
                    }
                }
                catch (Exception logEx)
                {
                    // If logging fails, write to console as fallback
                    Console.WriteLine($"Failed to write to log file: {logEx.Message}");
                    Console.WriteLine($"Original message: [{level}] {message}");
                }
            }
        }

        public async Task WriteLogAsync(string level, string message, Exception ex = null)
        {
            await Task.Run(() => WriteLog(level, message, ex));
        }
    }
}