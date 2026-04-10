using System.Text.Json;

namespace MauiBlazorHybrid.Services;

public class ThemeService : IThemeService
{
    private const string THEME_KEY = "app_theme";
    private const string DEFAULT_THEME = "auto";

    private readonly ILoggerService _loggerService = new LoggerService();
    private readonly string _customThemesDir;
    private Dictionary<string, Dictionary<string, string>> _builtInThemes = new();

    public string CurrentTheme { get; private set; } = DEFAULT_THEME;
    public string ResolvedTheme { get; private set; } = "light";

    public event EventHandler? ThemeChanged;

    public ThemeService()
    {
        _customThemesDir = Path.Combine(FileSystem.AppDataDirectory, "themes");
    }

    public async Task InitializeAsync()
    {
        try
        {
            _loggerService.Log("ThemeService initializing...");

            // Load built-in themes from app package
            await LoadBuiltInThemeAsync("light");
            await LoadBuiltInThemeAsync("dark");

            // Ensure custom themes directory exists
            if (!Directory.Exists(_customThemesDir))
            {
                Directory.CreateDirectory(_customThemesDir);
            }

            // Load saved preference - defaults to "auto" for new/updated installs
            CurrentTheme = Preferences.Default.Get(THEME_KEY, DEFAULT_THEME);
            ResolvedTheme = ResolveTheme(CurrentTheme);

            // Listen for system theme changes (for auto mode)
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeChanged += OnSystemThemeChanged;
            }

            _loggerService.Log($"ThemeService initialized. Current: {CurrentTheme}, Resolved: {ResolvedTheme}");
        }
        catch (Exception ex)
        {
            _loggerService.Log($"ThemeService initialization error: {ex.Message}");
            CurrentTheme = DEFAULT_THEME;
            ResolvedTheme = "light";
        }
    }

    public async Task SetThemeAsync(string themeName)
    {
        _loggerService.Log($"Setting theme to: {themeName}");
        CurrentTheme = themeName;
        Preferences.Default.Set(THEME_KEY, themeName);
        ResolvedTheme = ResolveTheme(themeName);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<List<string>> GetAvailableThemesAsync()
    {
        var themes = new List<string> { "auto", "light", "dark" };

        try
        {
            if (Directory.Exists(_customThemesDir))
            {
                var customFiles = Directory.GetFiles(_customThemesDir, "*.json");
                foreach (var file in customFiles)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!themes.Contains(name.ToLower()))
                    {
                        themes.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Error listing custom themes: {ex.Message}");
        }

        return themes;
    }

    public async Task<Dictionary<string, string>> GetThemeVariablesAsync(string themeName)
    {
        var resolved = ResolveTheme(themeName);

        // Check built-in themes
        if (_builtInThemes.TryGetValue(resolved, out var builtInVars))
        {
            return builtInVars;
        }

        // Check custom themes
        try
        {
            var customPath = Path.Combine(_customThemesDir, $"{resolved}.json");
            if (File.Exists(customPath))
            {
                var json = await File.ReadAllTextAsync(customPath);
                return ParseThemeJson(json);
            }
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Error loading custom theme '{resolved}': {ex.Message}");
        }

        // Fallback to light
        return _builtInThemes.GetValueOrDefault("light") ?? new Dictionary<string, string>();
    }

    public async Task<string?> ImportThemeAsync(Stream fileStream, string fileName)
    {
        try
        {
            if (!Directory.Exists(_customThemesDir))
            {
                Directory.CreateDirectory(_customThemesDir);
            }

            using var reader = new StreamReader(fileStream);
            var json = await reader.ReadToEndAsync();

            // Validate it's valid theme JSON
            var variables = ParseThemeJson(json);
            if (variables.Count == 0)
            {
                _loggerService.Log("Invalid theme file: no variables found");
                return null;
            }

            // Extract theme name from JSON or use filename
            var themeName = ParseThemeName(json) ?? Path.GetFileNameWithoutExtension(fileName);

            // Don't allow overwriting built-in theme names
            if (themeName.ToLower() == "light" || themeName.ToLower() == "dark" || themeName.ToLower() == "auto")
            {
                themeName = $"{themeName}_custom";
            }

            var savePath = Path.Combine(_customThemesDir, $"{themeName}.json");
            await File.WriteAllTextAsync(savePath, json);

            _loggerService.Log($"Custom theme imported: {themeName}");
            return themeName;
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Error importing theme: {ex.Message}");
            return null;
        }
    }

    public async Task<Stream?> ExportThemeAsync(string themeName)
    {
        try
        {
            var resolved = ResolveTheme(themeName);

            // For built-in themes, generate the JSON from memory
            if (_builtInThemes.ContainsKey(resolved))
            {
                var themeData = new { name = resolved.Substring(0, 1).ToUpper() + resolved.Substring(1), version = 1, variables = _builtInThemes[resolved] };
                var json = JsonSerializer.Serialize(themeData, new JsonSerializerOptions { WriteIndented = true });
                var stream = new MemoryStream();
                var writer = new StreamWriter(stream);
                await writer.WriteAsync(json);
                await writer.FlushAsync();
                stream.Position = 0;
                return stream;
            }

            // For custom themes, return the file stream
            var customPath = Path.Combine(_customThemesDir, $"{resolved}.json");
            if (File.Exists(customPath))
            {
                return File.OpenRead(customPath);
            }
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Error exporting theme '{themeName}': {ex.Message}");
        }

        return null;
    }

    public async Task<bool> DeleteCustomThemeAsync(string themeName)
    {
        try
        {
            // Cannot delete built-in themes
            if (themeName.ToLower() == "light" || themeName.ToLower() == "dark" || themeName.ToLower() == "auto")
            {
                return false;
            }

            var path = Path.Combine(_customThemesDir, $"{themeName}.json");
            if (File.Exists(path))
            {
                File.Delete(path);

                // If the deleted theme was active, switch to auto
                if (CurrentTheme == themeName)
                {
                    await SetThemeAsync(DEFAULT_THEME);
                }

                _loggerService.Log($"Custom theme deleted: {themeName}");
                return true;
            }
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Error deleting theme '{themeName}': {ex.Message}");
        }

        return false;
    }

    private string ResolveTheme(string themeName)
    {
        if (themeName == "auto")
        {
            try
            {
                var systemTheme = Application.Current?.RequestedTheme;
                return systemTheme == AppTheme.Dark ? "dark" : "light";
            }
            catch
            {
                return "light";
            }
        }
        return themeName;
    }

    private void OnSystemThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        if (CurrentTheme == "auto")
        {
            var newResolved = e.RequestedTheme == AppTheme.Dark ? "dark" : "light";
            if (newResolved != ResolvedTheme)
            {
                _loggerService.Log($"System theme changed to {newResolved}");
                ResolvedTheme = newResolved;
                ThemeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task LoadBuiltInThemeAsync(string name)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync($"Themes/{name}.json");
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var variables = ParseThemeJson(json);
            _builtInThemes[name] = variables;
            _loggerService.Log($"Built-in theme loaded: {name} ({variables.Count} variables)");
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Error loading built-in theme '{name}': {ex.Message}");
        }
    }

    private Dictionary<string, string> ParseThemeJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("variables", out var variablesElement))
            {
                var dict = new Dictionary<string, string>();
                foreach (var prop in variablesElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.GetString() ?? "";
                }
                return dict;
            }
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Error parsing theme JSON: {ex.Message}");
        }

        return new Dictionary<string, string>();
    }

    private string? ParseThemeName(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("name", out var nameElement))
            {
                return nameElement.GetString();
            }
        }
        catch { }
        return null;
    }
}
