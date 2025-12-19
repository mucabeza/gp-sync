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
        private readonly string _logDirectory;
        private readonly object _lock = new object();
        private readonly int _retentionDays;

        private string loggerLevel = DEBUG_LEVEL; // Default log level

        public Logger(IConfigurationRoot configurationBuilder)
        {
            loggerLevel = configurationBuilder["Logger:Level"] ?? DEBUG_LEVEL;
            string logFileName = configurationBuilder["Logger:LogFileName"] ?? "sync-log.txt";
            
            // Parse retention days from config (default to 7 days if not specified)
            if (!int.TryParse(configurationBuilder["Logger:RetentionDays"], out _retentionDays))
            {
                _retentionDays = 7;
            }

            // Create logs directory in the application folder
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(_logDirectory);

            // Create log file with timestamp
            string fileName = $"{DateTime.Now:yyyy-MM-dd}_{logFileName}";
            _logFilePath = Path.Combine(_logDirectory, fileName);
            
            // Clean up old log files on initialization
            CleanupOldLogFiles();
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

        /// <summary>
        /// Deletes log files older than the specified retention period
        /// </summary>
        public void CleanupOldLogFiles()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    return;

                var cutoffDate = DateTime.Now.AddDays(-_retentionDays);
                var logFiles = Directory.GetFiles(_logDirectory, "*.txt");

                foreach (var logFile in logFiles)
                {
                    var fileInfo = new FileInfo(logFile);
                    
                    // Delete files older than retention period
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        try
                        {
                            File.Delete(logFile);
                            Console.WriteLine($"Deleted old log file: {Path.GetFileName(logFile)}");
                        }
                        catch (Exception deleteEx)
                        {
                            Console.WriteLine($"Failed to delete log file {Path.GetFileName(logFile)}: {deleteEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during log cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Manually trigger cleanup of old log files
        /// </summary>
        public void ManualCleanup()
        {
            CleanupOldLogFiles();
        }
    }
}