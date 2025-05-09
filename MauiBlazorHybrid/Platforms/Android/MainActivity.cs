using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using MauiBlazorHybrid.Services;

namespace MauiBlazorHybrid
{

    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        // Static properties to be accessed by the Blazor components
        public static bool HasPendingReminderConfig { get; private set; }
        public static int PendingProductId { get; private set; }
        public static int PendingDosageId { get; private set; }
        public static bool IsRemindLater { get; private set; }

        private readonly ILoggerService _loggerService = new LoggerService();

        public MainActivity()
        {
            // Initialize default services or leave empty
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            _loggerService.Log("MainActivity: OnCreate called");
            base.OnCreate(savedInstanceState);

            // Process the intent
            ProcessIntent(Intent);
        }

        protected override void OnNewIntent(Intent intent)
        {
            _loggerService.Log("MainActivity: OnNewIntent called");
            base.OnNewIntent(intent);
            ProcessIntent(intent);
        }

        private void ProcessIntent(Android.Content.Intent intent)
        {
            _loggerService.Log("MainActivity: ProcessIntent called");
            if (intent != null && intent.HasExtra("action"))
            {
                string action = intent.GetStringExtra("action");
                _loggerService.Log($"MainActivity: Intent action: {action}");

                if (action == "edit_reminder")
                {
                    int productId = intent.GetIntExtra("productId", 0);
                    int dosageId = intent.GetIntExtra("dosageId", 0);
                    bool remindLater = intent.GetBooleanExtra("remind_later", false);

                    if (productId > 0 && dosageId > 0)
                    {
                        // Store the information for the Blazor app to use
                        HasPendingReminderConfig = true;
                        PendingProductId = productId;
                        PendingDosageId = dosageId;
                        IsRemindLater = remindLater;

                        _loggerService.Log($"MainActivity: Processed intent with productId={productId}, dosageId={dosageId}, remindLater={remindLater}");
                    }
                }
                else if (action == "take_now_action")
                {
                    int productId = intent.GetIntExtra("productId", 0);
                    int dosageId = intent.GetIntExtra("dosageId", 0);
                    double amount = intent.GetDoubleExtra("amount", 0);

                    var productService = MauiApplication.Current.Services.GetService<IProductService>();
                    if (productService != null)
                    {
                        Task.Run(async () =>
                        {
                            bool success = await productService.TakeProductDoseAsync(productId, amount, dosageId);
                            _loggerService.Log($"MainActivity: Processed take_now_action with productId={productId}, dosageId={dosageId}, amount={amount}, success={success}");
                        });
                    }
                }
                else if (action == "remind_15_action")
                {
                    int productId = intent.GetIntExtra("productId", 0);
                    int dosageId = intent.GetIntExtra("dosageId", 0);

                    var productService = MauiApplication.Current.Services.GetService<IProductService>();
                    var notificationService = MauiApplication.Current.Services.GetService<INotificationService>();

                    if (productService != null && notificationService != null)
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                // Retrieve the product and dosage
                                var product = await productService.GetProductAsync(productId);
                                if (product != null)
                                {
                                    var dosage = product.Dosages.FirstOrDefault(d => d.Id == dosageId);
                                    if (dosage != null)
                                    {
                                        // Schedule a new notification 15 minutes from now
                                        DateTime remindTime = DateTime.Now.AddMinutes(15);
                                        var notificationId = productId * 1000 + dosageId * 10 + 2;
                                        await notificationService.ScheduleNotificationAsync(product, dosage, remindTime, notificationId);
                                        _loggerService.Log($"MainActivity: Scheduled remind_15_action for productId={productId}, dosageId={dosageId} at {remindTime}");
                                    }
                                    else
                                    {
                                        _loggerService.Log($"MainActivity: Dosage with ID {dosageId} not found for productId={productId}");
                                    }
                                }
                                else
                                {
                                    _loggerService.Log($"MainActivity: Product with ID {productId} not found");
                                }
                            }
                            catch (Exception ex)
                            {
                                _loggerService.Log($"MainActivity: Error processing remind_15_action: {ex.Message}");
                            }
                        });
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
            IsRemindLater = false;
        }
    }
}
