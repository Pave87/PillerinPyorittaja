using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorHybrid.Services
{
    public interface ILocalizationService
    {
        string GetString(string key);
        string GetString(string key, params object[] args);
        string Format(string key, Dictionary<string, object> parameters);
        string GetRepetitionText(int repetition, string frequency);
        string CurrentCulture { get; }
        Task SetCultureAsync(string cultureName);
        event EventHandler CultureChanged;
    }
}
