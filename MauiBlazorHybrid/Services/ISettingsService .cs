using System;
using Microsoft.Maui.Storage;

namespace MauiBlazorHybrid.Services
{
    public interface ISettingsService
    {
        bool IsDebugEnabled { get; set; }
        event EventHandler SettingsChanged;
    }

    public class SettingsService : ISettingsService
    {
        private const string DEBUG_KEY = "debug_enabled";
        public event EventHandler? SettingsChanged;

        public bool IsDebugEnabled
        {
            get => Preferences.Default.Get(DEBUG_KEY, false);
            set
            {
                Preferences.Default.Set(DEBUG_KEY, value);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
