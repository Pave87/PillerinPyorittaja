using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using MauiBlazorHybrid.Services;
using Plugin.LocalNotification;

namespace MauiBlazorHybrid
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        // Static properties to be accessed by the Blazor components
        public static bool HasPendingReminderConfig { get; set; }
        public static int PendingProductId { get; set; }
        public static int PendingDosageId { get; set; }

        private readonly ILoggerService _loggerService = new LoggerService();

        protected override void OnCreate(Bundle savedInstanceState)
        {
            _loggerService.Log("MainActivity: OnCreate called");
            base.OnCreate(savedInstanceState);

            // Set the event listeners for Plugin.LocalNotification
            Plugin.LocalNotification.LocalNotificationCenter.Current.NotificationActionTapped += Current_NotificationActionTapped;

            // Process the intent
            ProcessIntent(Intent);
        }

        protected override void OnNewIntent(Intent intent)
        {
            _loggerService.Log("MainActivity: OnNewIntent called");
            base.OnNewIntent(intent);
            ProcessIntent(intent);
        }

        private void Current_NotificationActionTapped(Plugin.LocalNotification.EventArgs.NotificationActionEventArgs e)
        {
            _loggerService.Log($"MainActivity: Notification action tapped - {e.ActionId} from {e.Request.Title}");
            // The notification service will handle this event
        }

        private void ProcessIntent(Android.Content.Intent intent) // Ensure the correct namespace is used
        {
            _loggerService.Log("MainActivity: ProcessIntent called");

            try
            {
                // Just skip the notification plugin's processing for now to avoid the error
                // We'll handle our custom intents manually
            }
            catch (Exception ex)
            {
                _loggerService.Log($"Error in ProcessIntent: {ex.Message}");
            }

            if (intent != null && intent.HasExtra("action"))
            {
                string action = intent.GetStringExtra("action");

                if (action == "edit_reminder")
                {
                    int productId = intent.GetIntExtra("productId", 0);
                    int dosageId = intent.GetIntExtra("dosageId", 0);

                    if (productId > 0 && dosageId > 0)
                    {
                        // Store the information for the Blazor app to use
                        HasPendingReminderConfig = true;
                        PendingProductId = productId;
                        PendingDosageId = dosageId;

                        _loggerService.Log($"MainActivity: Processed intent with productId={productId}, dosageId={dosageId}");
                    }
                }
            }
        }

        // Method to clear the pending navigation after it's been handled
        public static void ClearPendingReminderConfig()
        {
            HasPendingReminderConfig = false;
            PendingProductId = 0;
            PendingDosageId = 0;
        }
    }
}
