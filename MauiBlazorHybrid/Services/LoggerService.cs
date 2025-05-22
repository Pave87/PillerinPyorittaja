using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace MauiBlazorHybrid.Services
{
    public class LoggerService : ILoggerService
    {
        private readonly string _logFilePath;
        private static bool _isLoggingEnabled; // Nullable to indicate uninitialized state

        static LoggerService()
        {
            // Resolve ISettingsService from the DI container during static initialization
            var settingsService = MauiProgram.Services.GetService<ISettingsService>();
            _isLoggingEnabled = settingsService?.IsDebugEnabled ?? false;
        }

        public LoggerService()
        {
            // Set the log file path to a writable directory
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            _logFilePath = Path.Combine(FileSystem.AppDataDirectory, $"debug-{date}.log");
            ManageLogFiles();
        }

        private void ManageLogFiles()
        {
            try
            {
                // Get all debug*.log files in the AppDataDirectory
                var logFiles = Directory.GetFiles(FileSystem.AppDataDirectory, "debug*.log");

                // Order files by last write time in descending order (most recent first)
                var orderedFiles = logFiles
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToList();

                // Skip the first 3 files (most recent) and delete the rest
                foreach (var file in orderedFiles.Skip(3))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                // Log any exceptions that occur during file management
                Log("LoggerService", $"Error managing log files: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs a message to the log file and debug output.
        /// </summary>
        /// <param name="logger">Name of function that sends this log line</param>
        /// <param name="exception">Exception</param>
        public void Log(string logger, Exception exception)
        {
            if (_isLoggingEnabled)
            {
                Log(logger, $"{exception.Message}{Environment.NewLine}{exception.StackTrace}");
            }
        }


        /// <summary>
        /// Logs a message to the log file and debug output.
        /// </summary>
        /// <param name="logger">Name of function that sends this log line</param>
        /// <param name="message">Log message</param>
        public void Log(string logger, string message)
        {
            if (_isLoggingEnabled)
            {
                var fullMessage = $"[{DateTime.Now:dd.MM.yyyy HH:mm:ss.fff}] {logger}: {message}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, fullMessage);
                Debug.Write(fullMessage);
#if DEBUG
                SendLogToHost(fullMessage);
#endif
            }
        }

        private void SendLogToHost(string message)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect("10.0.2.2", 5000); // 10.0.2.2 is the host from emulator
                using var stream = client.GetStream();
                var data = Encoding.UTF8.GetBytes(message);
                stream.Write(data, 0, data.Length);
            }
            catch
            {
                // Ignore network errors in logging
            }
        }
    }
}
