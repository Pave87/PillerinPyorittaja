using MauiBlazorHybrid.Models;
using System.Text.Json;

namespace MauiBlazorHybrid.Services;

public class ProductService : IProductService
{
    private readonly string _filePath;
    private List<Product> _products = new();
    private readonly INotificationService _notificationService;

    public ProductService(INotificationService notificationService)
    {
        _notificationService = notificationService;
        _filePath = Path.Combine(FileSystem.AppDataDirectory, "pills.json");
        LoadProducts();
    }

    private void LoadProducts()
    {
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            _products = JsonSerializer.Deserialize<List<Product>>(json) ?? new();
            // Ensure all pills have a history collection
            foreach (var product in _products)
            {
                product.History ??= new List<UsageHistory>();
            }
            // Reschedule notifications for all pills on load
            foreach (var product in _products)
            {
                RescheduleNotificationsForProduct(product).ConfigureAwait(false);
            }
        }
    }

    private async Task SaveProductsAsync()
    {
        var json = JsonSerializer.Serialize(_products);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task<List<Product>> GetProductsAsync()
    {
        return _products;
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        return _products.FirstOrDefault(p => p.Id == id);
    }

    public async Task<Product> AddProductAsync(Product product)
    {
        product.Id = _products.Count > 0 ? _products.Max(p => p.Id) + 1 : 1;
        product.History ??= new List<UsageHistory>();
        _products.Add(product);
        await SaveProductsAsync();

        // Schedule notifications for new pill
        await ScheduleNotificationsForProduct(product);

        return product;
    }

    public async Task UpdateProductAsync(Product product)
    {
        var index = _products.FindIndex(p => p.Id == product.Id);
        if (index != -1)
        {
            try
            {
                // Preserve history if it exists in the current pill but not in the updated one
                if (product.History == null || !product.History.Any())
                {
                    product.History = _products[index].History ?? new List<UsageHistory>();
                }

                // Cancel existing notifications
                await _notificationService.CancelNotificationsAsync(product.Id);

                _products[index] = product;
                await SaveProductsAsync();

                // Add debug output
                System.Diagnostics.Debug.WriteLine($"Scheduling notifications for updated pill {product.Id}");

                // Schedule new notifications and await it
                await ScheduleNotificationsForProduct(product);

                System.Diagnostics.Debug.WriteLine($"Notifications scheduled for pill {product.Id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating pill notifications: {ex.Message}");
                throw;
            }
        }
    }

    public async Task DeleteProductAsync(int id)
    {
        // Cancel notifications before deleting
        await _notificationService.CancelNotificationsAsync(id);

        _products.RemoveAll(p => p.Id == id);
        await SaveProductsAsync();
    }

    // Explicit implementation of the interface method without optional parameter
    public async Task<bool> TakeProductDoseAsync(int productId, double amount)
    {
        // Call the overloaded method with null dosageId
        return await TakeProductDoseAsync(productId, amount, null);
    }

    public async Task<bool> TakeProductDoseAsync(int productId, double amount, int? dosageId)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId);
        if (product == null) return false;

        // Check if there's enough quantity
        if (product.Quantity >= amount)
        {
            // Reduce the quantity
            product.Quantity -= amount;

            // Record the pill intake in history
            var historyEntry = new UsageHistory
            {
                Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1,
                ProductId = productId,
                Timestamp = DateTime.Now,
                AmountTaken = amount,
                DosageId = dosageId
            };

            product.History.Add(historyEntry);
            await SaveProductsAsync();

            // Reschedule notifications after taking a pill to update the next dose time
            if (dosageId.HasValue)
            {
                await _notificationService.CancelNotificationsAsync(productId);
                await ScheduleNotificationsForProduct(product);
            }

            return true;
        }

        return false;
    }

    public async Task<bool> TakeProductDoseFromScheduleAsync(int productId, int dosageId)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId);
        if (product == null) return false;

        var dosage = product.Dosages.FirstOrDefault(d => d.Id == dosageId);
        if (dosage == null) return false;

        // Use the dosage amount for the pill intake
        return await TakeProductDoseAsync(productId, dosage.AmountTaken, dosageId);
    }

    public async Task<List<UsageHistory>> GetProductHistoryAsync(int productId)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId);
        return product?.History?.OrderByDescending(h => h.Timestamp).ToList() ?? new List<UsageHistory>();
    }

    public async Task<List<UsageHistory>> GetAllProductHistoryAsync()
    {
        var allHistory = new List<UsageHistory>();
        foreach (var product in _products)
        {
            if (product.History != null && product.History.Any())
            {
                allHistory.AddRange(product.History);
            }
        }
        return allHistory.OrderByDescending(h => h.Timestamp).ToList();
    }

    public async Task AddProductHistoryManuallyAsync(UsageHistory history)
    {
        var product = _products.FirstOrDefault(p => p.Id == history.ProductId);
        if (product != null)
        {
            // Assign a new ID to the history entry
            history.Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1;

            // Add the history entry
            product.History.Add(history);
            await SaveProductsAsync();

            // Reschedule notifications if this was for a specific dosage
            if (history.DosageId.HasValue)
            {
                await _notificationService.CancelNotificationsAsync(product.Id);
                await ScheduleNotificationsForProduct(product);
            }
        }
    }

    private async Task ScheduleNotificationsForProduct(Product product)
    {
        // We'll calculate and schedule only the next upcoming dose for each dosage
        foreach (var dosage in product.Dosages)
        {
            // Find when this dosage was last taken
            var lastTaken = product.History
                .Where(h => h.DosageId == dosage.Id)
                .OrderByDescending(h => h.Timestamp)
                .FirstOrDefault();

            DateTime nextDoseTime = CalculateNextDoseTime(dosage, lastTaken?.Timestamp);

            // Schedule notification for this next dose
            await _notificationService.ScheduleNotificationAsync(product, dosage, nextDoseTime);
        }
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

    private async Task RescheduleNotificationsForProduct(Product pill)
    {
        try
        {
            // Cancel any existing notifications
            await _notificationService.CancelNotificationsAsync(pill.Id);
            // Schedule new notifications
            await ScheduleNotificationsForProduct(pill);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to reschedule notifications for pill {pill.Id}: {ex.Message}");
        }
    }
}
