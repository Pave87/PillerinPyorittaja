using MauiBlazorHybrid.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorHybrid.Services
{
    public interface INotificationService
    {
        Task<bool> RequestPermissionAsync();
        Task ScheduleNotificationAsync(Product pill, DosageSchedule dosage);
        Task ScheduleNotificationAsync(Product pill, DosageSchedule dosage, DateTime nextDoseTime);
        Task ScheduleNotificationAsync(Product pill, DosageSchedule dosage, DateTime nextDoseTime, int notificationId);
        Task CancelNotificationsAsync(int pillId);
    }
}
