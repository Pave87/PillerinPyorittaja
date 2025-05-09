using MauiBlazorHybrid.Models;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.ApplicationModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Android.Graphics;
using Android.Content.PM;
using Java.Lang;
using static Android.Icu.Text.CaseMap;
#endif

namespace MauiBlazorHybrid.Services
{
    public class NotificationService : INotificationService
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly ILocalizationService _localizationService;
        private bool _initialized = false;

        // Action identifiers for notification buttons
        private const string ACTION_TAKE_NOW = "take_now_action";
        private const string ACTION_REMIND_15 = "remind_15_action"; // New action identifier

        private readonly ILoggerService _loggerService = new LoggerService();

        public NotificationService(ILocalizationService localizationService)
        {
            _loggerService.Log("Initializing NotificationService...");
            _localizationService = localizationService;
            _initialized = true;
            _loggerService.Log("NotificationService initialized.");
        }

        // Helper method to get localized text
        private string L(string key)
        {
            int retryCount = 0;

            while (!_initialized && retryCount < 10)
            {
                Task.Delay(100);
                retryCount++;
                _loggerService.Log($"Waiting for NotificationService to initialize... Attempt {retryCount}");
            }
            return _localizationService.GetString(key);
        }

        // Helper method to get localized text with formatting using named parameters
        private string LF(string key, Dictionary<string, object> parameters)
        {
            int retryCount = 0;

            while (!_initialized && retryCount < 10)
            {
                Task.Delay(100);
                retryCount++;
                _loggerService.Log($"Waiting for NotificationService to initialize... Attempt {retryCount}");
            }
            return _localizationService.Format(key, parameters);
        }

        public async Task<bool> RequestPermissionAsync()
        {
            bool permissionGranted = false;
            _loggerService.Log("Requesting notification permissions...");
#if ANDROID
            try
            {
                // For Android 13+ (API level 33+)
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    // Request POST_NOTIFICATIONS permission for Android 13+
                    var status = await Permissions.RequestAsync<Permissions.PostNotifications>();
                    permissionGranted = status == PermissionStatus.Granted;
                }
                else
                {
                    // For older Android versions, no explicit permission needed
                    permissionGranted = true;
                }

                // If permission granted, also set up the alarm permissions
                if (permissionGranted)
                {
                    _loggerService.Log("Notification permission granted.");
                    // For Android 12+ we need to ask for SCHEDULE_EXACT_ALARM permission
                    if (OperatingSystem.IsAndroidVersionAtLeast(31))
                    {
                        var context = Android.App.Application.Context;
                        var alarmManager = context.GetSystemService(Android.Content.Context.AlarmService) as AlarmManager;

                        if (alarmManager != null && !alarmManager.CanScheduleExactAlarms())
                        {
                            // Open system settings to enable exact alarms
                            var intent = new Intent(Android.Provider.Settings.ActionRequestScheduleExactAlarm);
                            intent.SetFlags(ActivityFlags.NewTask);
                            context.StartActivity(intent);

                            // Show a localized toast explaining what to do
                            var toast = Toast.Make(L("Enable_Alarm_Permission"), ToastDuration.Long);
                            await toast.Show();
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                _loggerService.Log($"Error requesting permissions: {ex.Message}");
                permissionGranted = false;
            }
#else
                // Default for other platforms
                permissionGranted = true;
#endif

            return permissionGranted;
        }

        public async Task ScheduleNotificationAsync(Product product, DosageSchedule dosage)
        {
            _loggerService.Log($"Scheduling notification for product {product.Id} with dosage ID {dosage.Id}");
            // Find the most recent history entry for this dosage
            var lastTaken = product.History
                ?.Where(h => h.DosageId == dosage.Id)
                .OrderByDescending(h => h.Timestamp)
                .FirstOrDefault();

            // Calculate the next dose time based on last taken timestamp
            DateTime nextDoseTime = CalculateNextDoseTime(dosage, lastTaken?.Timestamp);

            // Schedule notification for the next dose
            await ScheduleNotification(product, dosage, nextDoseTime, null);

            // Show a confirmation toast with localized text using named parameters
            var reminderParams = new Dictionary<string, object>
                {
                    { "productName", product.Name },
                    { "scheduledTime", nextDoseTime.ToString("g") }
                };
            var toast = Toast.Make(LF("Reminder_Scheduled_For", reminderParams), ToastDuration.Short);
            await toast.Show();
            _loggerService.Log($"Notification scheduled for {product.Id} at {nextDoseTime}");
        }

        public async Task ScheduleNotificationAsync(Product product, DosageSchedule dosage, DateTime nextDoseTime)
        {
            if (nextDoseTime > DateTime.Now)
                await ScheduleNotification(product, dosage, nextDoseTime, null);
        }

        public async Task ScheduleNotificationAsync(Product product, DosageSchedule dosage, DateTime nextDoseTime, int notificationId)
        {
            if (nextDoseTime > DateTime.Now)
                await ScheduleNotification(product, dosage, nextDoseTime, notificationId);
        }

        private DateTime CalculateNextDoseTime(DosageSchedule dosage, DateTime? lastTakenTime)
        {
            // Current time to base calculations on
            DateTime now = DateTime.Now;

            // If there's no time set in the dosage, default to current time
            TimeOnly dosageTimeOfDay = dosage.Time ?? TimeOnly.FromDateTime(now);

            // Start with today's date at the dosage time
            DateTime baseTime = new DateTime(
                now.Year,
                now.Month,
                now.Day,
                dosageTimeOfDay.Hour,
                dosageTimeOfDay.Minute,
                0);

            // If we've never taken this product before or no dosage ID was recorded
            if (lastTakenTime == null)
            {
                // If today's dose time is already past, schedule for next occurrence
                if (baseTime < now)
                {
                    if (dosage.Frequency == "Days")
                    {
                        // For daily dosage, schedule for tomorrow
                        return baseTime.AddDays(1);
                    }
                    else if (dosage.Frequency == "Weeks" && dosage.SelectedDays?.Any() == true)
                    {
                        // For weekly dosage, find the next selected day
                        return FindNextWeekdayOccurrence(baseTime, dosage.SelectedDays);
                    }
                }
                return baseTime;
            }
            else
            {
                // We have a record of when this product was last taken
                DateTime lastTaken = lastTakenTime.Value;

                // Calculate the next dose based on frequency and repetition
                if (dosage.Frequency == "Days")
                {
                    // Calculate next dose based on repetition (e.g., every X days)
                    DateTime nextDose = lastTaken.Date.AddDays(dosage.Repetition);

                    // Set the time part from the dosage's scheduled time
                    nextDose = new DateTime(
                        nextDose.Year,
                        nextDose.Month,
                        nextDose.Day,
                        dosageTimeOfDay.Hour,
                        dosageTimeOfDay.Minute,
                        0);

                    // If the calculated time is in the past, schedule for the next cycle
                    if (nextDose < now)
                    {
                        int daysToAdd = dosage.Repetition - (int)(now - nextDose).TotalDays % dosage.Repetition;
                        if (daysToAdd == 0 || (now - nextDose).TotalDays % dosage.Repetition == 0)
                        {
                            daysToAdd = dosage.Repetition;
                        }
                        nextDose = now.Date.AddDays(daysToAdd);

                        // Reset time component
                        nextDose = new DateTime(
                            nextDose.Year,
                            nextDose.Month,
                            nextDose.Day,
                            dosageTimeOfDay.Hour,
                            dosageTimeOfDay.Minute,
                            0);
                    }

                    return nextDose;
                }
                else if (dosage.Frequency == "Weeks" && dosage.SelectedDays?.Any() == true)
                {
                    // For weekly dosage, find the next selected day after the last taken date
                    DateTime startPoint = lastTaken.AddDays(1);
                    DateTime nextWeeklyDose = FindNextWeekdayOccurrence(startPoint, dosage.SelectedDays);

                    // Apply repetition (every X weeks)
                    int weeksToAdd = (dosage.Repetition - 1) * 7;
                    nextWeeklyDose = nextWeeklyDose.AddDays(weeksToAdd);

                    // If the calculated time is in the past, find the next occurrence
                    if (nextWeeklyDose < now)
                    {
                        nextWeeklyDose = FindNextWeekdayOccurrence(now, dosage.SelectedDays);
                    }

                    // Set the time part from the dosage's scheduled time
                    nextWeeklyDose = new DateTime(
                        nextWeeklyDose.Year,
                        nextWeeklyDose.Month,
                        nextWeeklyDose.Day,
                        dosageTimeOfDay.Hour,
                        dosageTimeOfDay.Minute,
                        0);

                    return nextWeeklyDose;
                }

                // Default case, schedule for tomorrow at the dosage time
                return baseTime.AddDays(1);
            }
        }

        private DateTime FindNextWeekdayOccurrence(DateTime startDate, List<string> selectedDays)
        {
            DateTime result = startDate;
            int daysToAdd = 0;
            bool found = false;

            // Convert the day names to DayOfWeek enum values for easier comparison
            var selectedDaysOfWeek = selectedDays.Select(day => ParseWeekDay(day)).ToList();

            // Try each day, up to 7 days forward
            for (int i = 0; i < 7; i++)
            {
                DateTime checkDate = startDate.AddDays(i);
                if (selectedDaysOfWeek.Contains(checkDate.DayOfWeek))
                {
                    daysToAdd = i;
                    found = true;
                    break;
                }
            }

            // If no selected day found in the next 7 days (shouldn't happen with valid data)
            // then just add a week
            if (!found)
            {
                daysToAdd = 7;
            }

            return startDate.AddDays(daysToAdd);
        }

        private async Task ScheduleNotification(Product product, DosageSchedule dosage, DateTime scheduleTime, int? notificationId)
        {
            try
            {
                _loggerService.Log($"Scheduling notification for product {product.Id} at {scheduleTime}");
                await _semaphore.WaitAsync();

                if (notificationId == null)
                {
                    notificationId = GenerateNotificationId(product.Id, dosage.Id);
                }

                // Create localized notification message using LF with named parameters
                var titleParams = new Dictionary<string, object>
                    {
                        { "productName", product.Name }
                    };
                string title = LF("Time_To_Take", titleParams);

                var messageParams = new Dictionary<string, object>
                    {
                        { "amount", dosage.AmountTaken.ToString() },
                        { "unit", product.Unit },
                        { "productName", product.Name }
                    };
                string message = LF("Take_Amount_Of", messageParams);

#if ANDROID
                // Schedule system alarm for reliable background notifications
                ScheduleAndroidAlarm(product, dosage, scheduleTime, notificationId ?? 0, title, message);
#endif
            }
            finally
            {
                _semaphore.Release();
            }
            _loggerService.Log($"Notification scheduled for product {product.Id} at {scheduleTime}");
        }

#if ANDROID
        private void ScheduleAndroidAlarm(Product product, DosageSchedule dosage, DateTime scheduleTime, int notificationId, string title, string message)
        {
            try
            {
                _loggerService.Log($"Scheduling Android alarm for product {product.Id} at {scheduleTime}");
                var context = Android.App.Application.Context;

                // Cancel any existing alarms for this notification
                CancelAndroidAlarm(notificationId);

                // Create intent for alarm receiver
                var intent = new Intent(context, typeof(NotificationAlarmReceiver));
                intent.PutExtra("notificationId", notificationId);
                intent.PutExtra("title", title);
                intent.PutExtra("message", message);

                // Add product and dosage IDs for action handling
                intent.PutExtra("productId", product.Id);
                intent.PutExtra("dosageId", dosage.Id);

                // Also pass localized strings for channel and fallbacks
                intent.PutExtra("channelName", L("Reminders"));
                intent.PutExtra("channelDesc", L("Reminders_Description"));
                intent.PutExtra("fallbackTitle", L("Reminder"));
                intent.PutExtra("fallbackMessage", L("Time_To_Take"));

                // Add localized button text
                intent.PutExtra("takeNowText", L("Take_Now"));
                intent.PutExtra("remind15Text", L("Remind_15_Minutes")); // New button text
                intent.PutExtra("amount", (double)dosage.AmountTaken);

                // Create unique pending intent
                PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
                if (OperatingSystem.IsAndroidVersionAtLeast(31)) // Android 12+
                {
                    flags |= PendingIntentFlags.Immutable;
                }

                var pendingIntent = PendingIntent.GetBroadcast(context, notificationId, intent, flags);

                // Get alarm manager
                var alarmManager = context.GetSystemService(Context.AlarmService) as AlarmManager;
                if (alarmManager != null)
                {
                    // Convert to milliseconds since epoch
                    long triggerAtMillis = new DateTimeOffset(scheduleTime).ToUnixTimeMilliseconds();

                    // Schedule with exact timing when possible
                    if (OperatingSystem.IsAndroidVersionAtLeast(31)) // Android 12+
                    {
                        if (alarmManager.CanScheduleExactAlarms())
                        {
                            alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
                        }
                        else
                        {
                            alarmManager.Set(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
                        }
                    }
                    else if (OperatingSystem.IsAndroidVersionAtLeast(23)) // Android 6.0+
                    {
                        alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
                    }
                    else // Older Android
                    {
                        alarmManager.SetExact(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
                    }

                    _loggerService.Log($"Scheduled Android alarm for {product.Name} at {scheduleTime}");
                }
            }
            catch (System.Exception ex)
            {
                _loggerService.Log($"Error scheduling Android alarm: {ex.Message}");
            }
        }

        private void CancelAndroidAlarm(int notificationId)
        {
            try
            {
                var context = Android.App.Application.Context;

                // Create matching intent
                var intent = new Intent(context, typeof(NotificationAlarmReceiver));

                // Create matching pending intent
                PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
                if (OperatingSystem.IsAndroidVersionAtLeast(31)) // Android 12+
                {
                    flags |= PendingIntentFlags.Immutable;
                }

                var pendingIntent = PendingIntent.GetBroadcast(context, notificationId, intent, flags);

                // Get alarm manager and cancel
                var alarmManager = context.GetSystemService(Context.AlarmService) as AlarmManager;
                alarmManager?.Cancel(pendingIntent);

                // Also remove any displayed notification
                var notificationManager = NotificationManagerCompat.From(context);
                notificationManager.Cancel(notificationId);
            }
            catch (System.Exception ex)
            {
                _loggerService.Log($"Error canceling alarm: {ex.Message}");
            }
        }
#endif

        public async Task CancelNotificationsAsync(int productId)
        {
            try
            {
                await _semaphore.WaitAsync();

#if ANDROID
                // We need to find all the possible notification IDs for this product
                // by looking at all dosages (assuming we can access them)
                try
                {
                    // Get the product from the ProductService to access its actual dosages
                    var services = Microsoft.Maui.MauiApplication.Current?.Services;
                    if (services != null)
                    {
                        var productService = services.GetService(typeof(IProductService)) as IProductService;
                        if (productService != null)
                        {
                            var product = await productService.GetProductAsync(productId);
                            if (product != null && product.Dosages != null)
                            {
                                // Only cancel notifications for actual dosages
                                foreach (var dosage in product.Dosages)
                                {
                                    int notificationId = GenerateNotificationId(productId, dosage.Id);
                                    CancelAndroidAlarm(notificationId);
                                }
                                _loggerService.Log($"Canceled {product.Dosages.Count} notifications for product {productId}");
                                return;
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    _loggerService.Log($"Error canceling notifications for product {productId}: {ex.Message}");
                }
#endif
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private int GenerateNotificationId(int productId, int dosageId)
        {
            return productId * 1000 + dosageId * 10;
        }

        private DayOfWeek ParseWeekDay(string day) => day switch
        {
            "Mon" => DayOfWeek.Monday,
            "Tue" => DayOfWeek.Tuesday,
            "Wed" => DayOfWeek.Wednesday,
            "Thu" => DayOfWeek.Thursday,
            "Fri" => DayOfWeek.Friday,
            "Sat" => DayOfWeek.Saturday,
            "Sun" => DayOfWeek.Sunday,
            _ => throw new ArgumentException($"Invalid day: {day}")
        };

        private DateTime GetNextWeekdayOccurrence(DateTime baseTime, DayOfWeek targetDay)
        {
            int daysUntilTarget = ((int)targetDay - (int)baseTime.DayOfWeek + 7) % 7;
            if (daysUntilTarget == 0 && baseTime.TimeOfDay > baseTime.TimeOfDay)
                daysUntilTarget = 7;
            return baseTime.AddDays(daysUntilTarget);
        }
    }

#if ANDROID
    // Add broadcast receiver for alarms
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { "android.intent.action.BOOT_COMPLETED" })]
    public class NotificationAlarmReceiver : BroadcastReceiver
    {
        private readonly ILoggerService _loggerService = new LoggerService();

        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                // Skip showing notifications for boot completed events
                if (intent.Action == Intent.ActionBootCompleted)
                {
                    _loggerService.Log("Device reboot detected. Not showing notifications immediately.");
                    return;
                }

                // Get notification details from intent
                int notificationId = intent.GetIntExtra("notificationId", 0);
                int productId = intent.GetIntExtra("productId", 0);
                int dosageId = intent.GetIntExtra("dosageId", 0);
                double amount = intent.GetDoubleExtra("amount", 0);

                // Get pre-localized strings from intent extras (already localized by NotificationService)
                string title = intent.GetStringExtra("title");
                string message = intent.GetStringExtra("message");

                // If title or message is not available, use the fallback
                if (string.IsNullOrEmpty(title))
                {
                    title = intent.GetStringExtra("fallbackTitle");
                }

                if (string.IsNullOrEmpty(message))
                {
                    message = intent.GetStringExtra("fallbackMessage");
                }

                // Get pre-localized channel name and description
                string channelName = intent.GetStringExtra("channelName");
                string channelDesc = intent.GetStringExtra("channelDesc");

                // Create notification channel
                string channelId = "product_reminders";
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                {
                    var notificationManager = context.GetSystemService(Context.NotificationService) as NotificationManager;
                    if (notificationManager != null)
                    {
                        var channel = new NotificationChannel(
                            channelId,
                            channelName ?? "Reminders",
                            NotificationImportance.High)
                        {
                            Description = channelDesc ?? "Reminder notifications"
                        };
                        channel.EnableVibration(true);
                        notificationManager.CreateNotificationChannel(channel);
                    }
                }

                // Create intent for when notification is tapped
                var resultIntent = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);
                if (resultIntent == null)
                {
                    // Fallback intent
                    resultIntent = new Intent(context, typeof(MainActivity));
                }
                resultIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);

                // Add action, product ID, and dosage ID to the intent
                resultIntent.PutExtra("action", "edit_reminder");
                resultIntent.PutExtra("productId", productId);
                resultIntent.PutExtra("dosageId", dosageId);
                resultIntent.PutExtra("remind_later", false);

                _loggerService.Log($"Setting notification intent with action=edit_reminder, productId={productId}, dosageId={dosageId}");

                // Create pending intent
                PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.S)
                {
                    flags |= PendingIntentFlags.Immutable;
                }
                var pendingIntent = PendingIntent.GetActivity(context, notificationId * 10, resultIntent, flags);

                // Try to find the app icon
                int iconResource;
                try
                {
                    iconResource = context.Resources.GetIdentifier("icon", "mipmap", context.PackageName);
                    if (iconResource == 0)
                    {
                        iconResource = Android.Resource.Drawable.IcDialogInfo;
                    }
                }
                catch
                {
                    iconResource = Android.Resource.Drawable.IcDialogInfo;
                }

                // Build the notification
                var notificationBuilder = new NotificationCompat.Builder(context, channelId)
                    .SetContentTitle(title)
                    .SetContentText(message)
                    .SetSmallIcon(iconResource)
                    .SetContentIntent(pendingIntent)
                    .SetAutoCancel(true)
                    .SetDefaults((int)NotificationDefaults.Sound | (int)NotificationDefaults.Vibrate)
                    .SetPriority(NotificationCompat.PriorityHigh);


                // Add Take Now action button
                string takeNowText = intent.GetStringExtra("takeNowText") ?? "Take Now";
                var takeNowIntent = new Intent(context, typeof(NotificationActionReceiver));
                takeNowIntent.SetAction("take_now_action");
                takeNowIntent.PutExtra("notificationId", notificationId);
                takeNowIntent.PutExtra("productId", productId);
                takeNowIntent.PutExtra("dosageId", dosageId);
                takeNowIntent.PutExtra("amount", amount);

                var takeNowPendingIntent = PendingIntent.GetBroadcast(
                    context,
                    notificationId * 10 + 1, // Unique request code
                    takeNowIntent,
                    flags);

                notificationBuilder.AddAction(Android.Resource.Drawable.IcDialogInfo, takeNowText, takeNowPendingIntent);

                // Add Remind 15 Minutes action button
                string remind15Text = intent.GetStringExtra("remind15Text") ?? "Remind in 15 Min";
                var remind15Intent = new Intent(context, typeof(NotificationActionReceiver));
                remind15Intent.SetAction("remind_15_action");
                remind15Intent.PutExtra("notificationId", notificationId);
                remind15Intent.PutExtra("productId", productId);
                remind15Intent.PutExtra("dosageId", dosageId);

                var remind15PendingIntent = PendingIntent.GetBroadcast(
                    context,
                    notificationId * 10 + 2, // Unique request code
                    remind15Intent,
                    flags);

                notificationBuilder.AddAction(Android.Resource.Drawable.IcDialogInfo, remind15Text, remind15PendingIntent);

                // Show the notification
                var notificationManagerCompat = NotificationManagerCompat.From(context);

                // Check for notification permission on Android 13+
                bool canShowNotification = true;
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
                {
                    try
                    {
                        var pm = context.PackageManager;
                        var packageInfo = pm.GetPackageInfo(context.PackageName, 0);
                        var applicationInfo = packageInfo.ApplicationInfo;
                        int targetSdkVersion = (int)applicationInfo.TargetSdkVersion;
                        int tiramisuVersion = (int)Android.OS.BuildVersionCodes.Tiramisu;

                        if (targetSdkVersion >= tiramisuVersion)
                        {
                            var permissionStatus = context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications);
                            canShowNotification = permissionStatus == Permission.Granted;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        canShowNotification = false;
                        _loggerService.Log($"Error checking notification permission: {ex.Message}");
                    }
                }

                if (canShowNotification)
                {
                    notificationManagerCompat.Notify(notificationId, notificationBuilder.Build());

                    // Also try to vibrate
                    try
                    {
                        var vibrator = context.GetSystemService(Context.VibratorService) as Vibrator;
                        if (vibrator != null && vibrator.HasVibrator)
                        {
                            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                            {
                                vibrator.Vibrate(VibrationEffect.CreateOneShot(500, VibrationEffect.DefaultAmplitude));
                            }
                            else
                            {
                                // Deprecated in API 26
                                vibrator.Vibrate(500);
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        _loggerService.Log($"Error vibrating device: {ex.Message}");
                        // Ignore vibration errors
                    }
                }
            }
            catch (System.Exception ex)
            {
                _loggerService.Log($"Error in NotificationAlarmReceiver: {ex.Message}");
            }
        }
    }

    // Add broadcast receiver for notification actions
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { "take_now_action", "remind_15_action" })] // Added remind_15_action
    public class NotificationActionReceiver : BroadcastReceiver
    {
        private readonly ILoggerService _loggerService = new LoggerService();

        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                string action = intent.Action;
                int notificationId = intent.GetIntExtra("notificationId", 0);
                int productId = intent.GetIntExtra("productId", 0);
                int dosageId = intent.GetIntExtra("dosageId", 0);
                double amount = intent.GetDoubleExtra("amount", 0);

                _loggerService.Log($"Action received: {action} for notification ID: {notificationId}");

                // Always cancel the notification when an action is taken
                var notificationManager = NotificationManagerCompat.From(context);
                notificationManager.Cancel(notificationId);

                if (action == "take_now_action")
                {
                    _loggerService.Log($"Take Now action triggered for notification ID: {notificationId}");

                    // Get the IProductService instance from the application context
                    var productService = GetProductService(context);
                    if (productService != null)
                    {
                        // Execute the TakeProductDoseAsync method in background
                        Task.Run(async () =>
                        {
                            try
                            {
                                // Take the dose using the product id, amount and dosage id
                                bool result = await productService.TakeProductDoseAsync(productId, amount, dosageId);

                                // Show a toast message indicating success or failure
                                string toastMessage = result ?
                                    "Dose_Taken_Successfully" :
                                    "Error_Taking_Dose";

                                // Send a broadcast to show toast
                                var toastIntent = new Intent("show_toast_message");
                                toastIntent.PutExtra("message", toastMessage);
                                context.SendBroadcast(toastIntent);

                                _loggerService.Log($"TakeProductDoseAsync completed with result: {result}");
                            }
                            catch (System.Exception ex)
                            {
                                _loggerService.Log($"Error taking dose: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        _loggerService.Log("Failed to get ProductService instance for TakeNow action");
                    }
                }
                else if (action == "remind_15_action")
                {
                    _loggerService.Log($"Remind 15 Minutes action triggered for notification ID: {notificationId}");

                    // Schedule a new notification 15 minutes from now
                    Task.Run(async () =>
                    {
                        try
                        {
                            var notificationService = GetNotificationService(context);
                            if (notificationService != null)
                            {
                                DateTime remindTime = DateTime.Now.AddMinutes(15);

                                // Get the product and dosage details
                                var productService = GetProductService(context);
                                if (productService != null)
                                {
                                    var product = await productService.GetProductAsync(productId);
                                    if (product != null)
                                    {
                                        var dosage = product.Dosages.FirstOrDefault(d => d.Id == dosageId);
                                        if (dosage != null)
                                        {
                                            await notificationService.ScheduleNotificationAsync(product, dosage, remindTime, notificationId);
                                            _loggerService.Log($"Scheduled reminder in 15 minutes for product ID: {productId}, dosage ID: {dosageId}");
                                        }
                                    }
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            _loggerService.Log($"Error scheduling reminder in 15 minutes: {ex.Message}");
                        }
                    });
                }
            }
            catch (System.Exception ex)
            {
                _loggerService.Log($"Error in NotificationActionReceiver: {ex.Message}");
            }
        }

        private IProductService GetProductService(Context context)
        {
            try
            {
                // Access the MauiApp to get the registered services
                var application = Microsoft.Maui.MauiApplication.Current;

                if (application != null)
                {
                    // Get the service provider
                    var services = application.Services;

                    if (services != null)
                    {
                        // Get the IProductService instance
                        var productService = services.GetService(typeof(IProductService)) as IProductService;
                        return productService;
                    }
                }

                return null;
            }
            catch (System.Exception ex)
            {
                _loggerService.Log($"Error getting ProductService: {ex.Message}");
                return null;
            }
        }

        private INotificationService GetNotificationService(Context context)
        {
            try
            {
                // Access the MauiApp to get the registered services
                var application = Microsoft.Maui.MauiApplication.Current;

                if (application != null)
                {
                    // Get the service provider
                    var services = application.Services;

                    if (services != null)
                    {
                        // Get the INotificationService instance
                        var notificationService = services.GetService(typeof(INotificationService)) as INotificationService;
                        return notificationService;
                    }
                }

                return null;
            }
            catch (System.Exception ex)
            {
                _loggerService.Log($"Error getting NotificationService: {ex.Message}");
                return null;
            }
        }
    }
#endif
}
