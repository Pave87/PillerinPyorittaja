using MauiBlazorHybrid.Services;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Plugin.LocalNotification;

namespace MauiBlazorHybrid
{
    public static class MauiProgram
    {
        public static IServiceProvider Services { get; private set; }
        public static MauiApp CreateMauiApp()
        {
            using (CallContext.BeginCall())
            {
                var builder = MauiApp.CreateBuilder();
                builder
                    .UseMauiApp<App>()
                    .UseMauiCommunityToolkit()
                    .UseLocalNotification() // Add this
                    .ConfigureFonts(fonts =>
                    {
                        fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                        fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    });

                builder.Services.AddMauiBlazorWebView();
#if DEBUG
                builder.Services.AddBlazorWebViewDeveloperTools();
#endif

                // Register services
                builder.Services.AddSingleton<IProductService, ProductService>();
                builder.Services.AddSingleton<MauiBlazorHybrid.Services.INotificationService, NotificationService>();
                builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
                builder.Services.AddSingleton<ISettingsService, SettingsService>();
                builder.Services.AddSingleton<IThemeService, ThemeService>();
                builder.Services.AddSingleton<ILoggerService, LoggerService>();
                builder.Services.AddSingleton<IAdService, AdService>();
                builder.Services.AddSingleton<IBackupService, BackupService>();

                var app = builder.Build();
                Services = app.Services;

                // Resolve ILoggerService and log the message
                var loggerService = app.Services.GetService<ILoggerService>();
                loggerService?.Log("--------------------------Builder done--------------------------");

                return app;
            }

        }
    }
}
