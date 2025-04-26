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
        Task SchedulePillNotificationAsync(Pill pill, PillDosage dosage);
        Task SchedulePillNotificationAsync(Pill pill, PillDosage dosage, DateTime nextDoseTime);
        Task CancelPillNotificationsAsync(int pillId);
    }
}
