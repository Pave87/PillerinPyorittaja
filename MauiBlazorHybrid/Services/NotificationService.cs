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
#endif

namespace MauiBlazorHybrid.Services
{
    public class NotificationService : INotificationService
    {
        private readonly List<ScheduledNotification> _scheduledNotifications = new();
        private Timer? _notificationTimer;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly ILocalizationService _localizationService;
        private bool _initialized = false;

        public NotificationService(ILocalizationService localizationService)
        {
            _localizationService = localizationService;

            // Instead of starting the timer right away, we'll delay it a bit
            // to ensure the LocalizationService is fully initialized
            Task.Delay(2000).ContinueWith(_ =>
            {
                _notificationTimer = new Timer(CheckNotifications, null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
                _initialized = true;
            });
        }

        // Helper method to get localized text
        private string L(string key)
        {
            // Make sure we're initialized before trying to use the localization service
            if (!_initialized)
            {
                // Return a placeholder until we're ready to display real messages
                return key;
            }
            return _localizationService.GetString(key);
        }

        // Helper method to get localized text with formatting using named parameters
        private string LF(string key, Dictionary<string, object> parameters)
        {
            // Make sure we're initialized before trying to use the localization service
            if (!_initialized)
            {
                // Return a placeholder until we're ready to display real messages
                return key;
            }
            return _localizationService.Format(key, parameters);
        }

        public async Task<bool> RequestPermissionAsync()
        {
            bool permissionGranted = false;

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
                Console.WriteLine($"Error requesting permissions: {ex.Message}");
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
            // Find the most recent history entry for this dosage
            var lastTaken = product.History
                ?.Where(h => h.DosageId == dosage.Id)
                .OrderByDescending(h => h.Timestamp)
                .FirstOrDefault();

            // Calculate the next dose time based on last taken timestamp
            DateTime nextDoseTime = CalculateNextDoseTime(dosage, lastTaken?.Timestamp);

            // Schedule notification for the next dose
            await ScheduleNotification(product, dosage, nextDoseTime, 0); // No auto-repeat, we'll manually schedule

            // Show a confirmation toast with localized text using named parameters
            var reminderParams = new Dictionary<string, object>
                {
                    { "productName", product.Name },
                    { "scheduledTime", nextDoseTime.ToString("g") }
                };
            var toast = Toast.Make(LF("Reminder_Scheduled_For", reminderParams), ToastDuration.Short);
            await toast.Show();
        }

        public async Task ScheduleNotificationAsync(Product product, DosageSchedule dosage, DateTime nextDoseTime)
        {
            await ScheduleNotification(product, dosage, nextDoseTime, 0); // No auto-repeat, we'll manually schedule
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

        private async Task ScheduleNotification(Product product, DosageSchedule dosage, DateTime scheduleTime, int repeatDays)
        {
            try
            {
                await _semaphore.WaitAsync();

                var notificationId = GenerateNotificationId(product.Id, dosage.Id);

                // Remove any existing notifications for this product/dosage
                _scheduledNotifications.RemoveAll(n => n.NotificationId == notificationId);

                // Create localized notification message using LF with named parameters
                var titleParams = new Dictionary<string, object>
                    {
                        { "productName", product.Name }
                    };
                string title = LF("Time_To_Take", titleParams);

                var messageParams = new Dictionary<string, object>
                    {
                        { "amount", dosage.AmountTaken.ToString() },
                        { "unit", dosage.AmountTaken == 1 ? L("Unit_Single") : L("Unit_Plural") },
                        { "productName", product.Name }
                    };
                string message = LF("Take_Amount_Of", messageParams);

                // Add the new notification with localized text
                _scheduledNotifications.Add(new ScheduledNotification
                {
                    NotificationId = notificationId,
                    ScheduleTime = scheduleTime,
                    Title = title,
                    Message = message,
                    RepeatDays = repeatDays,
                    Id = product.Id,
                    DosageId = dosage.Id
                });

#if ANDROID
                // Also schedule system alarm for reliable background notifications
                ScheduleAndroidAlarm(product, dosage, scheduleTime, notificationId, title, message);
#endif
            }
            finally
            {
                _semaphore.Release();
            }
        }

#if ANDROID
        private void ScheduleAndroidAlarm(Product product, DosageSchedule dosage, DateTime scheduleTime, int notificationId, string title, string message)
        {
            try
            {
                var context = Android.App.Application.Context;

                // Cancel any existing alarms for this notification
                CancelAndroidAlarm(notificationId);

                // Create intent for alarm receiver
                var intent = new Intent(context, typeof(NotificationAlarmReceiver));
                intent.PutExtra("notificationId", notificationId);
                intent.PutExtra("title", title);
                intent.PutExtra("message", message);

                // Also pass localized strings for channel and fallbacks
                intent.PutExtra("channelName", L("Reminders"));
                intent.PutExtra("channelDesc", L("Reminders_Description"));
                intent.PutExtra("fallbackTitle", L("Reminder"));
                intent.PutExtra("fallbackMessage", L("Time_To_Take"));

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

                    Console.WriteLine($"Scheduled Android alarm for {product.Name} at {scheduleTime}");
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Error scheduling Android alarm: {ex.Message}");
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
                Console.WriteLine($"Error canceling alarm: {ex.Message}");
            }
        }
#endif

        public async Task CancelNotificationsAsync(int productId)
        {
            try
            {
                await _semaphore.WaitAsync();

                // Get notification IDs before removing
                var notificationIds = _scheduledNotifications
                    .Where(n => n.Id == productId)
                    .Select(n => n.NotificationId)
                    .ToList();

                // Remove from internal list
                _scheduledNotifications.RemoveAll(n => n.Id == productId);

#if ANDROID
                // Also cancel Android alarms
                foreach (var notificationId in notificationIds)
                {
                    CancelAndroidAlarm(notificationId);
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
            return productId * 1000 + dosageId;
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

        private async void CheckNotifications(object? state)
        {
            // Don't check notifications until we're properly initialized
            if (!_initialized)
                return;

            try
            {
                await _semaphore.WaitAsync();
                var now = DateTime.Now;
                var triggeredNotifications = new List<ScheduledNotification>();

                // Find notifications that should be triggered
                foreach (var notification in _scheduledNotifications.ToList())
                {
                    // Check if the notification time has passed and is within the last minute
                    var timeDiff = now - notification.ScheduleTime;
                    if (timeDiff >= TimeSpan.Zero && timeDiff < TimeSpan.FromMinutes(1.1))
                    {
                        triggeredNotifications.Add(notification);

                        // Schedule the next occurrence if this is a repeating notification
                        if (notification.RepeatDays > 0)
                        {
                            notification.ScheduleTime = notification.ScheduleTime.AddDays(notification.RepeatDays);
                        }
                        else
                        {
                            // Remove non-repeating notifications
                            _scheduledNotifications.Remove(notification);
                        }
                    }
                }

                _semaphore.Release();

                // Show notifications after releasing the lock to avoid deadlocks
                foreach (var notification in triggeredNotifications)
                {
                    await ShowNotification(notification);
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Error checking notifications: {ex.Message}");

                // Ensure semaphore is released in case of exceptions
                if (_semaphore.CurrentCount == 0)
                {
                    _semaphore.Release();
                }
            }
        }

        private async Task ShowNotification(ScheduledNotification notification)
        {
#if ANDROID
            // Android system notification implementation
            var context = Android.App.Application.Context;
            var channelId = "product_reminders";
            var channelName = L("Reminders"); // Localized channel name using L

            // Get the notification manager
            var notificationManager = context.GetSystemService(Android.Content.Context.NotificationService) as Android.App.NotificationManager;

            // Create notification channel (required for Android Oreo and above)
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O && notificationManager != null)
            {
                var channel = new Android.App.NotificationChannel(channelId, channelName, Android.App.NotificationImportance.High)
                {
                    Description = L("Reminders_Description") // Localized description using L
                };
                channel.EnableVibration(true);
                notificationManager.CreateNotificationChannel(channel);
            }

            // Create a default intent for when the notification is tapped
            Android.Content.Intent intent = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);
            if (intent == null)
            {
                // Fallback if launch intent is not found
                intent = new Intent(context, typeof(MainActivity));
            }

            intent.SetFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTask);

            // Update PendingIntent flags to use Immutable on newer Android versions
            PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.S)
            {
                flags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = PendingIntent.GetActivity(context, notification.NotificationId, intent, flags);

            // Create the notification builder with a reliable icon
            int iconResource;
            try
            {
                // Try to get app icon
                iconResource = context.Resources.GetIdentifier("icon", "mipmap", context.PackageName);
                if (iconResource == 0)
                {
                    // Fallback to system icon
                    iconResource = Android.Resource.Drawable.IcDialogInfo;
                }
            }
            catch
            {
                // Another fallback
                iconResource = Android.Resource.Drawable.IcDialogInfo;
            }

            var builder = new NotificationCompat.Builder(context, channelId)
                .SetContentTitle(notification.Title)
                .SetContentText(notification.Message)
                .SetSmallIcon(iconResource)
                .SetContentIntent(pendingIntent)
                .SetAutoCancel(true)
                .SetDefaults((int)NotificationDefaults.Sound | (int)NotificationDefaults.Vibrate)
                .SetPriority(NotificationCompat.PriorityHigh);

            // Show the notification - make sure we check for permission
            try
            {
                // Check notification permission (for Android 13+)
                bool canShowNotification = true;
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
                {
                    var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
                    canShowNotification = status == PermissionStatus.Granted;
                }

                if (canShowNotification)
                {
                    NotificationManagerCompat.From(context).Notify(notification.NotificationId, builder.Build());

                    // Try to vibrate device separately
                    if (Vibration.Default.IsSupported)
                    {
                        try
                        {
                            Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(500));
                        }
                        catch (System.Exception)
                        {
                            // Ignore vibration errors
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Failed to show notification: {ex.Message}");
            }
#else
                        // For non-Android platforms, just do nothing
                        await Task.CompletedTask;
#endif
        }

        private class ScheduledNotification
        {
            public int NotificationId { get; set; }
            public DateTime ScheduleTime { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public int RepeatDays { get; set; }
            public int Id { get; set; }
            public int DosageId { get; set; }
        }
    }

#if ANDROID
    // Add broadcast receiver for alarms
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { "android.intent.action.BOOT_COMPLETED" })]
    public class NotificationAlarmReceiver : BroadcastReceiver
    {
        // We can't inject services in broadcast receivers, so we'll need to use the strings as is
        // from the intent extras
        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                // Get notification details from intent
                int notificationId = intent.GetIntExtra("notificationId", 0);

                // Get pre-localized strings from intent extras (already localized by NotificationService)
                string title = intent.GetStringExtra("title");
                string message = intent.GetStringExtra("message");

                // If title or message is not available, use the key itself - just as it works in Razor files
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
                            channelName,
                            NotificationImportance.High)
                        {
                            Description = channelDesc
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

                // Create pending intent
                PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.S)
                {
                    flags |= PendingIntentFlags.Immutable;
                }
                var pendingIntent = PendingIntent.GetActivity(context, notificationId, resultIntent, flags);

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
                        // Fix: Using int for comparison with BuildVersionCodes
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
                        Console.WriteLine($"Error checking notification permission: {ex.Message}");
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
                        Console.WriteLine($"Error vibrating device: {ex.Message}");
                        // Ignore vibration errors
                    }
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Error in NotificationAlarmReceiver: {ex.Message}");
            }
        }
    }
#endif
}
