using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorHybrid.Services
{
    internal interface ILoggerService
    {
        /// <summary>
        /// Logs a message with contextual information about the caller.
        /// All methods that use ILoggerService should use using (CallContext.BeginCall()) to ensure that the call chain is logged correctly.
        /// </summary>
        /// <param name="message">The message to log. Cannot be null or empty. Refer to products by their ID</param>
        /// <param name="memberName">The name of the calling member. This is automatically populated by the compiler  unless explicitly provided.</param>
        /// <param name="filePath">The full file path of the source code file containing the caller. This is  automatically populated by the
        /// compiler unless explicitly provided.</param>
        void Log(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "");
        /// <summary>
        /// Logs the specified exception along with the caller's member name and file path.
        /// All methods that use ILoggerService should use using (CallContext.BeginCall()) to ensure that the call chain is logged correctly.
        /// </summary>
        /// <param name="exception">The exception to log. Cannot be <see langword="null"/>.</param>
        /// <param name="memberName">The name of the calling member. This is automatically populated by the compiler.</param>
        /// <param name="filePath">The file path of the calling code. This is automatically populated by the compiler.</param>
        void Log(Exception exception, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "");
    }
}
