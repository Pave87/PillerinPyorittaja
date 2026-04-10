namespace MauiBlazorHybrid.Services;

public interface IThemeService
{
    /// <summary>
    /// The stored theme preference: "auto", "light", "dark", or a custom theme filename.
    /// </summary>
    string CurrentTheme { get; }

    /// <summary>
    /// The resolved theme after evaluating "auto" against the system setting.
    /// Returns "light" or "dark" or a custom theme name.
    /// </summary>
    string ResolvedTheme { get; }

    /// <summary>
    /// Fired when the active theme changes (explicit change or auto-detection).
    /// </summary>
    event EventHandler ThemeChanged;

    /// <summary>
    /// Initialize the theme service: load preference, detect system theme, load theme files.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Set and persist the theme preference, then apply it.
    /// </summary>
    Task SetThemeAsync(string themeName);

    /// <summary>
    /// Get list of available theme names: "auto", "light", "dark", plus any custom themes.
    /// </summary>
    Task<List<string>> GetAvailableThemesAsync();

    /// <summary>
    /// Get the CSS variable dictionary for a given theme name.
    /// </summary>
    Task<Dictionary<string, string>> GetThemeVariablesAsync(string themeName);

    /// <summary>
    /// Import a custom theme from a JSON file stream.
    /// </summary>
    Task<string?> ImportThemeAsync(Stream fileStream, string fileName);

    /// <summary>
    /// Export a theme as a stream for saving.
    /// </summary>
    Task<Stream?> ExportThemeAsync(string themeName);

    /// <summary>
    /// Delete a custom theme.
    /// </summary>
    Task<bool> DeleteCustomThemeAsync(string themeName);
}
