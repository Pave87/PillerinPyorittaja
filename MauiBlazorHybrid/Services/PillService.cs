// MauiBlazorHybrid/Services/PillService.cs
using MauiBlazorHybrid.Models;
using System.Text.Json;

namespace MauiBlazorHybrid.Services;

public class PillService : IPillService
{
    private readonly string _filePath;
    private List<Pill> _pills = new();
    private readonly INotificationService _notificationService;

    public PillService(INotificationService notificationService)
    {
        _notificationService = notificationService;
        _filePath = Path.Combine(FileSystem.AppDataDirectory, "pills.json");
        LoadPills();
    }

    private void LoadPills()
    {
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            _pills = JsonSerializer.Deserialize<List<Pill>>(json) ?? new();
            // Ensure all pills have a history collection
            foreach (var pill in _pills)
            {
                pill.History ??= new List<PillHistory>();
            }
            // Reschedule notifications for all pills on load
            foreach (var pill in _pills)
            {
                RescheduleNotificationsForPill(pill).ConfigureAwait(false);
            }
        }
    }

    private async Task SavePillsAsync()
    {
        var json = JsonSerializer.Serialize(_pills);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task<List<Pill>> GetPillsAsync()
    {
        return _pills;
    }

    public async Task<Pill?> GetPillAsync(int id)
    {
        return _pills.FirstOrDefault(p => p.Id == id);
    }

    public async Task<Pill> AddPillAsync(Pill pill)
    {
        pill.Id = _pills.Count > 0 ? _pills.Max(p => p.Id) + 1 : 1;
        pill.History ??= new List<PillHistory>();
        _pills.Add(pill);
        await SavePillsAsync();

        // Schedule notifications for new pill
        await ScheduleNotificationsForPill(pill);

        return pill;
    }

    public async Task UpdatePillAsync(Pill pill)
    {
        var index = _pills.FindIndex(p => p.Id == pill.Id);
        if (index != -1)
        {
            try
            {
                // Preserve history if it exists in the current pill but not in the updated one
                if (pill.History == null || !pill.History.Any())
                {
                    pill.History = _pills[index].History ?? new List<PillHistory>();
                }

                // Cancel existing notifications
                await _notificationService.CancelPillNotificationsAsync(pill.Id);

                _pills[index] = pill;
                await SavePillsAsync();

                // Add debug output
                System.Diagnostics.Debug.WriteLine($"Scheduling notifications for updated pill {pill.Id}");

                // Schedule new notifications and await it
                await ScheduleNotificationsForPill(pill);

                System.Diagnostics.Debug.WriteLine($"Notifications scheduled for pill {pill.Id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating pill notifications: {ex.Message}");
                throw;
            }
        }
    }

    public async Task DeletePillAsync(int id)
    {
        // Cancel notifications before deleting
        await _notificationService.CancelPillNotificationsAsync(id);

        _pills.RemoveAll(p => p.Id == id);
        await SavePillsAsync();
    }

    // Explicit implementation of the interface method without optional parameter
    public async Task<bool> TakePillDoseAsync(int pillId, double amount)
    {
        // Call the overloaded method with null dosageId
        return await TakePillDoseAsync(pillId, amount, null);
    }

    public async Task<bool> TakePillDoseAsync(int pillId, double amount, int? dosageId)
    {
        var pill = _pills.FirstOrDefault(p => p.Id == pillId);
        if (pill == null) return false;

        // Check if there's enough quantity
        if (pill.Quantity >= amount)
        {
            // Reduce the quantity
            pill.Quantity -= amount;

            // Record the pill intake in history
            var historyEntry = new PillHistory
            {
                Id = pill.History.Count > 0 ? pill.History.Max(h => h.Id) + 1 : 1,
                PillId = pillId,
                Timestamp = DateTime.Now,
                AmountTaken = amount,
                DosageId = dosageId
            };

            pill.History.Add(historyEntry);
            await SavePillsAsync();

            // Reschedule notifications after taking a pill to update the next dose time
            if (dosageId.HasValue)
            {
                await _notificationService.CancelPillNotificationsAsync(pillId);
                await ScheduleNotificationsForPill(pill);
            }

            return true;
        }

        return false;
    }

    public async Task<bool> TakePillDoseFromScheduleAsync(int pillId, int dosageId)
    {
        var pill = _pills.FirstOrDefault(p => p.Id == pillId);
        if (pill == null) return false;

        var dosage = pill.Dosages.FirstOrDefault(d => d.Id == dosageId);
        if (dosage == null) return false;

        // Use the dosage amount for the pill intake
        return await TakePillDoseAsync(pillId, dosage.AmountTaken, dosageId);
    }

    public async Task<List<PillHistory>> GetPillHistoryAsync(int pillId)
    {
        var pill = _pills.FirstOrDefault(p => p.Id == pillId);
        return pill?.History?.OrderByDescending(h => h.Timestamp).ToList() ?? new List<PillHistory>();
    }

    public async Task<List<PillHistory>> GetAllPillHistoryAsync()
    {
        var allHistory = new List<PillHistory>();
        foreach (var pill in _pills)
        {
            if (pill.History != null && pill.History.Any())
            {
                allHistory.AddRange(pill.History);
            }
        }
        return allHistory.OrderByDescending(h => h.Timestamp).ToList();
    }

    public async Task AddPillHistoryManuallyAsync(PillHistory history)
    {
        var pill = _pills.FirstOrDefault(p => p.Id == history.PillId);
        if (pill != null)
        {
            // Assign a new ID to the history entry
            history.Id = pill.History.Count > 0 ? pill.History.Max(h => h.Id) + 1 : 1;

            // Add the history entry
            pill.History.Add(history);
            await SavePillsAsync();

            // Reschedule notifications if this was for a specific dosage
            if (history.DosageId.HasValue)
            {
                await _notificationService.CancelPillNotificationsAsync(pill.Id);
                await ScheduleNotificationsForPill(pill);
            }
        }
    }

    private async Task ScheduleNotificationsForPill(Pill pill)
    {
        // We'll calculate and schedule only the next upcoming dose for each dosage
        foreach (var dosage in pill.Dosages)
        {
            // Find when this dosage was last taken
            var lastTaken = pill.History
                .Where(h => h.DosageId == dosage.Id)
                .OrderByDescending(h => h.Timestamp)
                .FirstOrDefault();

            DateTime nextDoseTime = CalculateNextDoseTime(dosage, lastTaken?.Timestamp);

            // Schedule notification for this next dose
            await _notificationService.SchedulePillNotificationAsync(pill, dosage, nextDoseTime);
        }
    }

    private DateTime CalculateNextDoseTime(PillDosage dosage, DateTime? lastTakenTime)
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
        var selectedDaysOfWeek = selectedDays.Select(day => ParseDayOfWeek(day)).ToList();

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

    private DayOfWeek ParseDayOfWeek(string day)
    {
        return day switch
        {
            "Mon" => DayOfWeek.Monday,
            "Tue" => DayOfWeek.Tuesday,
            "Wed" => DayOfWeek.Wednesday,
            "Thu" => DayOfWeek.Thursday,
            "Fri" => DayOfWeek.Friday,
            "Sat" => DayOfWeek.Saturday,
            "Sun" => DayOfWeek.Sunday,
            _ => DayOfWeek.Monday // Default case
        };
    }

    private async Task RescheduleNotificationsForPill(Pill pill)
    {
        try
        {
            // Cancel any existing notifications
            await _notificationService.CancelPillNotificationsAsync(pill.Id);
            // Schedule new notifications
            await ScheduleNotificationsForPill(pill);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to reschedule notifications for pill {pill.Id}: {ex.Message}");
        }
    }
}
