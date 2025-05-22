using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Android.Service.Autofill;
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
        public void Log(Exception exception, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            if (_isLoggingEnabled)
            {
                Log($"{exception.Message}{Environment.NewLine}{exception.StackTrace}", memberName, filePath);
            }
        }


        /// <summary>
        /// Logs a message to the log file and debug output.
        /// </summary>
        /// <param name="logger">Name of function that sends this log line</param>
        /// <param name="message">Log message</param>
        public void Log(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            if (_isLoggingEnabled)
            {
                var callChain = CallContext.CurrentCallChain;
                var callChainPart = !string.IsNullOrEmpty(callChain) ? $"[{callChain}]" : "";
                var className = filePath.Split(new[] { '\\', '/' }).Last();
                className = Path.GetFileNameWithoutExtension(className);
                if (memberName == ".ctor") memberName = "Constructor";
                if (memberName == ".cctor") memberName = "StaticConstructor";
                var fullMessage = $"[{DateTime.Now:dd.MM.yyyy HH:mm:ss.fff}] {className}/{memberName} {callChainPart}: {message}{Environment.NewLine}{Environment.NewLine}";
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

    public static class CallContext
    {
        private static readonly AsyncLocal<CallChainInfo?> _current = new();
        private static int _nextId = 0; // Numeric, incrementing for uniqueness

        public static string? CurrentCallChain => _current.Value?.Chain;

        public static IDisposable BeginCall()
        {
            var previous = _current.Value;
            var newId = GetNextId();
            string newChain = previous == null ? newId : $"{previous.Chain}-{newId}";
            _current.Value = new CallChainInfo
            {
                Chain = newChain,
                Previous = previous
            };
            return new DisposableAction(() => _current.Value = previous);
        }

        private static string GetNextId()
        {
            // Use base36 for shortness (0-9, A-Z)
            int id = Interlocked.Increment(ref _nextId);
            return id.ToString();
        }

        private class CallChainInfo
        {
            public string Chain { get; set; } = "";
            public CallChainInfo? Previous { get; set; }
        }

        private class DisposableAction : IDisposable
        {
            private readonly Action _onDispose;
            public DisposableAction(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }
    }

}
