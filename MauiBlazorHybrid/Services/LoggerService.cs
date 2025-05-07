using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorHybrid.Services
{
    public class LoggerService : ILoggerService
    {
        internal string _logFilePath;

        public LoggerService()
        {
            // Set the log file path to a writable directory
            _logFilePath = Path.Combine(FileSystem.AppDataDirectory, "debug.log");
        }
        public void Log(System.Exception exception)
        {
            if (true)
            {
                Log($"{exception.Message}{Environment.NewLine}{exception.StackTrace}");
            }
        }

        public void Log(string message)
        {
            if (true)
            {
                File.AppendAllText(_logFilePath, $"[{DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff")}] {message}{Environment.NewLine}{Environment.NewLine}");
                Debug.WriteLine($"[LoggerService] {message}");
            }
        }
    }
}
