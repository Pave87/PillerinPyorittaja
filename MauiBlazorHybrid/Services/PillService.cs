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

    private async Task ScheduleNotificationsForPill(Pill pill)
    {
        foreach (var dosage in pill.Dosages)
        {
            await _notificationService.SchedulePillNotificationAsync(pill, dosage);
        }
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
