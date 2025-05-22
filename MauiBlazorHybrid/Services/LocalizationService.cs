using System.Globalization;
using System.Resources;

namespace MauiBlazorHybrid.Services
{
    public class LocalizationService : ILocalizationService
    {
        private readonly ResourceManager _resourceManager;
        private CultureInfo _currentCulture;
        private const string PreferenceKey = "UserCulture";

        public event EventHandler? CultureChanged;
        public string CurrentCulture => _currentCulture.Name;

        private readonly ILoggerService _loggerService; // Injected logger service

        public LocalizationService()
        {
            _loggerService = new LoggerService();
            _loggerService.Log("LocalizationService", "Initalizing...");
            // Point to your resource file
            _resourceManager = new ResourceManager("MauiBlazorHybrid.Resources.Localization.AppResources",
                                                 typeof(LocalizationService).Assembly);

            // Load preferred culture from preferences or use system default
            var savedCulture = Preferences.Get(PreferenceKey, string.Empty);
            _currentCulture = string.IsNullOrEmpty(savedCulture)
                ? CultureInfo.CurrentCulture
                : new CultureInfo(savedCulture);

            CultureInfo.CurrentCulture = _currentCulture;
            CultureInfo.CurrentUICulture = _currentCulture;

            _loggerService.Log("LocalizationService", "Initialized with culture: " + _currentCulture.Name);
        }

        public string GetString(string key)
        {
            var value = _resourceManager.GetString(key, _currentCulture);

            if (string.IsNullOrEmpty(value))
            {
                return key; // Return the key as fallback
            }

            return value;
        }

        public string GetString(string key, params object[] args)
        {
            var format = GetString(key);

            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                // If formatting fails, return the unformatted string
                return format;
            }
        }

        public string Format(string key, Dictionary<string, object> parameters)
        {
            var template = GetString(key);

            if (parameters == null || parameters.Count == 0)
            {
                return template;
            }

            // Replace named parameters in format {paramName}
            var result = template;
            foreach (var param in parameters)
            {
                result = result.Replace("{" + param.Key + "}", param.Value?.ToString());
            }

            return result;
        }

        public string GetRepetitionText(int repetition, string frequency = "Days")
        {
            // Handle repetitions based on frequency type (days or weeks)
            if (frequency == "Weeks")
            {
                return GetWeekRepetitionText(repetition);
            }
            else
            {
                return GetDayRepetitionText(repetition);
            }
        }

        private string GetDayRepetitionText(int repetition)
        {
            // Special handling for Finnish repetition text
            if (_currentCulture.Name.StartsWith("fi", StringComparison.OrdinalIgnoreCase))
            {
                if (repetition == 1)
                    return GetString("Repetition_Daily");
                else if (repetition == 2)
                    return GetString("Repetition_EveryOtherDay");
                else
                    return GetString("Repetition_Generic").Replace("{count}", repetition.ToString());
            }

            // Default English-style formatting for other languages
            return $"{GetString("Every")} {repetition} {(repetition == 1 ? GetString("Day_Single") : GetString("Days_Plural"))}";
        }

        private string GetWeekRepetitionText(int repetition)
        {
            // Special handling for Finnish week repetition text
            if (_currentCulture.Name.StartsWith("fi", StringComparison.OrdinalIgnoreCase))
            {
                if (repetition == 1)
                    return GetString("Repetition_Weekly");
                else if (repetition == 2)
                    return GetString("Repetition_EveryOtherWeek");
                else
                    return GetString("Repetition_WeekGeneric").Replace("{count}", repetition.ToString());
            }

            // Default English-style formatting for other languages
            return $"{GetString("Every")} {repetition} {(repetition == 1 ? GetString("Week_Single") : GetString("Weeks_Plural"))}";
        }

        public Task SetCultureAsync(string cultureName)
        {
            if (cultureName != _currentCulture.Name)
            {
                _currentCulture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = _currentCulture;
                CultureInfo.CurrentUICulture = _currentCulture;

                // Save the preference
                Preferences.Set(PreferenceKey, cultureName);

                CultureChanged?.Invoke(this, EventArgs.Empty);
            }

            return Task.CompletedTask;
        }
    }
}
