using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorHybrid.Services
{
    internal interface ILoggerService
    {
        void Log(string message);
        void Log(Exception exception);
    }
}
