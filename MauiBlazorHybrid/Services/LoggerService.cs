using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            _logFilePath = Path.Combine(FileSystem.AppDataDirectory, "debug.log");
        }

        public void Log(Exception exception)
        {
            if (_isLoggingEnabled)
            {
                Log($"{exception.Message}{Environment.NewLine}{exception.StackTrace}");
            }
        }

        public void Log(string message)
        {
            if (_isLoggingEnabled)
            {
                var fullMessage = $"[{DateTime.Now:dd.MM.yyyy HH:mm:ss.fff}] {message}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, fullMessage);
                Debug.Write(fullMessage);
            }
        }
    }
}
