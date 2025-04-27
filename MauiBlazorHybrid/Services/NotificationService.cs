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

        public async Task SchedulePillNotificationAsync(Product pill, DosageSchedule dosage)
        {
            // Find the most recent history entry for this dosage
            var lastTaken = pill.History
                ?.Where(h => h.DosageId == dosage.Id)
                .OrderByDescending(h => h.Timestamp)
                .FirstOrDefault();

            // Calculate the next dose time based on last taken timestamp
            DateTime nextDoseTime = CalculateNextDoseTime(dosage, lastTaken?.Timestamp);

            // Schedule notification for the next dose
            await ScheduleNotification(pill, dosage, nextDoseTime, 0); // No auto-repeat, we'll manually schedule

            // Show a confirmation toast
            var toast = Toast.Make($"Reminder scheduled for {pill.Name} at {nextDoseTime:g}", ToastDuration.Short);
            await toast.Show();
        }

        public async Task SchedulePillNotificationAsync(Product pill, DosageSchedule dosage, DateTime nextDoseTime)
        {
            await ScheduleNotification(pill, dosage, nextDoseTime, 0); // No auto-repeat, we'll manually schedule
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

            // If we've never taken this pill before or no dosage ID was recorded
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
                // We have a record of when this pill was last taken
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

        private async Task ScheduleNotification(Product pill, DosageSchedule dosage, DateTime scheduleTime, int repeatDays)
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
                    Message = $"Take {dosage.AmountTaken} {(dosage.AmountTaken == 1 ? pill.Unit : $"{pill.Unit}s")} of {pill.Name}",
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
