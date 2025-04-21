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
using AndroidX.Core.App;  // Added for NotificationCompat
using Android.Graphics;
#endif

namespace MauiBlazorHybrid.Services
{
    public class NotificationService : INotificationService
    {
        private readonly List<ScheduledNotification> _scheduledNotifications = new();
        private Timer? _notificationTimer;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public NotificationService()
        {
            // Start the timer to check for notifications every minute
            _notificationTimer = new Timer(CheckNotifications, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        public async Task<bool> RequestPermissionAsync()
        {
            bool permissionGranted = false;

#if ANDROID
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
#else
            // Default for other platforms (won't be used as we're focusing on Android)
            permissionGranted = true;
#endif

            return permissionGranted;
        }

        public async Task SchedulePillNotificationAsync(Pill pill, PillDosage dosage)
        {
            if (dosage.Time == null) return;

            var now = DateTime.Now;
            var notificationTime = new DateTime(
                now.Year, now.Month, now.Day,
                dosage.Time.Value.Hour,
                dosage.Time.Value.Minute,
                0);

            // If the time has passed today, schedule for tomorrow
            if (notificationTime < now)
            {
                notificationTime = notificationTime.AddDays(1);
            }

            // Calculate next occurrence based on frequency
            switch (dosage.Frequency)
            {
                case "Days":
                    await ScheduleNotification(pill, dosage, notificationTime, dosage.Repetition);
                    break;

                case "Weeks":
                    if (dosage.SelectedDays?.Any() == true)
                    {
                        foreach (var day in dosage.SelectedDays)
                        {
                            var dayOfWeek = ParseWeekDay(day);
                            var nextOccurrence = GetNextWeekdayOccurrence(notificationTime, dayOfWeek);
                            await ScheduleNotification(pill, dosage, nextOccurrence, 7);
                        }
                    }
                    break;
            }

            // Show a confirmation toast
            var toast = Toast.Make($"Reminder scheduled for {pill.Name} at {notificationTime.ToShortTimeString()}", ToastDuration.Short);
            await toast.Show();
        }

        private async Task ScheduleNotification(Pill pill, PillDosage dosage, DateTime scheduleTime, int repeatDays)
        {
            try
            {
                await _semaphore.WaitAsync();

                var notificationId = GenerateNotificationId(pill.Id, dosage.Id);

                // Remove any existing notifications for this pill/dosage
                _scheduledNotifications.RemoveAll(n => n.NotificationId == notificationId);

                // Add the new notification
                _scheduledNotifications.Add(new ScheduledNotification
                {
                    NotificationId = notificationId,
                    ScheduleTime = scheduleTime,
                    Title = $"Time to take {pill.Name}",
                    Message = $"Take {dosage.AmountTaken} {(dosage.AmountTaken == 1 ? "pill" : "pills")} of {pill.Name}",
                    RepeatDays = repeatDays,
                    PillId = pill.Id,
                    DosageId = dosage.Id
                });
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task CancelPillNotificationsAsync(int pillId)
        {
            try
            {
                await _semaphore.WaitAsync();
                _scheduledNotifications.RemoveAll(n => n.PillId == pillId);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private int GenerateNotificationId(int pillId, int dosageId)
        {
            return pillId * 1000 + dosageId;
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
            catch (Exception)
            {
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
                var channelId = "pill_reminders";
                var channelName = "Pill Reminders";
                
                // Get the notification manager
                var notificationManager = context.GetSystemService(Android.Content.Context.NotificationService) as Android.App.NotificationManager;
                
                // Create notification channel (required for Android Oreo and above)
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O && notificationManager != null)
                {
                    var channel = new Android.App.NotificationChannel(channelId, channelName, Android.App.NotificationImportance.High)
                    {
                        Description = "Shows notifications for medication reminders"
                    };
                    channel.EnableVibration(true);
                    notificationManager.CreateNotificationChannel(channel);
                }
                
                // Create a default intent for when the notification is tapped
                Android.Content.Intent intent = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);
                intent.SetFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTask);
                var pendingIntent = Android.App.PendingIntent.GetActivity(context, notification.NotificationId, intent, 
                    Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.S ? Android.App.PendingIntentFlags.Immutable : Android.App.PendingIntentFlags.UpdateCurrent);
                
                // Create the notification builder
                var builder = new NotificationCompat.Builder(context, channelId)
                    .SetContentTitle(notification.Title)
                    .SetContentText(notification.Message)
                    .SetSmallIcon(Android.Resource.Drawable.IcPopupReminder) // Using a system icon as fallback
                    .SetContentIntent(pendingIntent)
                    .SetAutoCancel(true)
                    .SetDefaults((int)Android.App.NotificationDefaults.Sound | (int)Android.App.NotificationDefaults.Vibrate)
                    .SetPriority(NotificationCompat.PriorityHigh);
                
                // Show the notification
                NotificationManagerCompat.From(context).Notify(notification.NotificationId, builder.Build());
                
                // Attempt to vibrate device as additional feedback
                try
                {
                    // Fixed: Use Vibrate instead of VibrateAsync
                    Vibration.Default.Vibrate(TimeSpan.FromSeconds(0.5));
                    await Task.CompletedTask; // Just to maintain async signature
                }
                catch (FeatureNotSupportedException)
                {
                    // Vibration not supported
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
            public int PillId { get; set; }
            public int DosageId { get; set; }
        }
    }
}
