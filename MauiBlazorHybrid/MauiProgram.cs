using MauiBlazorHybrid.Services;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;

namespace MauiBlazorHybrid
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
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
            builder.Services.AddSingleton<ILoggerService, LoggerService>();
            //builder.Services.AddSingleton<IAdService, AdService>(); Add support temporarily disabled

            var app = builder.Build();

            // Resolve ILoggerService and log the message
            var loggerService = app.Services.GetService<ILoggerService>();
            loggerService?.Log("--------------------------Builder done--------------------------");

            return app;
        }
    }
}
