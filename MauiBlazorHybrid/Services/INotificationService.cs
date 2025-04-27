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
        Task SchedulePillNotificationAsync(Product pill, DosageSchedule dosage);
        Task SchedulePillNotificationAsync(Product pill, DosageSchedule dosage, DateTime nextDoseTime);
        Task CancelPillNotificationsAsync(int pillId);
    }
}
