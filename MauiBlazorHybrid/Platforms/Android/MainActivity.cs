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

        private readonly IProductService _productService;

        public MainActivity()
        {
            // Initialize default services or leave empty
        }

        public MainActivity(IProductService productService)
        {
            _loggerService.Log("Initializing MainActivity ProductService...");
            _productService = productService;
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

        private void ProcessIntent(Android.Content.Intent intent) // Ensure the correct namespace is used
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
                    int amount = intent.GetIntExtra("amount", 0);

                    var productService = MauiApplication.Current.Services.GetService<IProductService>();
                    var products = productService?.GetProductsAsync();

                    var success = productService.TakeProductDoseAsync(productId, amount, dosageId);
                    _loggerService.Log($"MainActivity: Processed intent with productId={productId}, dosageId={dosageId}, amount={amount}, success={success}");

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
