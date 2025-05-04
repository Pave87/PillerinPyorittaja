using MauiBlazorHybrid.Models;
using System.Text.Json;

namespace MauiBlazorHybrid.Services;

public class ProductService : IProductService
{
    private readonly string _filePath;
    private List<Product> _products = new();
    private readonly INotificationService _notificationService;

    private readonly ILoggerService _loggerService; // Injected logger service

    public ProductService(INotificationService notificationService)
    {
        _loggerService = new LoggerService();
        _loggerService.Log("Initalizing ProductService...");

        _notificationService = notificationService;
        _filePath = Path.Combine(FileSystem.AppDataDirectory, "pills.json");
        LoadProducts();

        _loggerService.Log("ProductService initialized");
    }

    public async Task<bool> AddPacketAsync(int productId, double amountToAdd)
    {
        try
        {
            _loggerService.Log($"Adding {amountToAdd} to product {productId}");

            // Get the current product
            var product = await GetProductAsync(productId);
            if (product == null)
                return false;

            // Add the amount to the product quantity
            product.Quantity += amountToAdd;

            // Update the product in storage
            await UpdateProductAsync(product);

            _loggerService.Log($"Added {amountToAdd} to product {productId}. New quantity: {product.Quantity}");
            return true;
        }
        catch
        {
            _loggerService.Log($"Failed to add {amountToAdd} to product {productId}");
            return false;
        }
    }

    private void LoadProducts()
    {
        _loggerService.Log("Loading products from file...");
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            _products = JsonSerializer.Deserialize<List<Product>>(json) ?? new();
            // Ensure all products have the necessary collections
            foreach (var product in _products)
            {
                product.History ??= new List<UsageHistory>();
                product.MissedDosages ??= new List<MissedDosage>();

                // Calculate next dose for each dosage if not set
                foreach (var dosage in product.Dosages)
                {
                    if (dosage.NextDose == null)
                    {
                        dosage.NextDose = CalculateNextDoseTime(dosage, null);
                    }
                }
            }
            _loggerService.Log($"Loaded {_products.Count} products from file.");
            // Reschedule notifications for all products on load
            foreach (var product in _products)
            {
                RescheduleNotificationsForProduct(product).ConfigureAwait(false);
            }
            _loggerService.Log("Notifications rescheduled for all products.");

            // Process any missed dosages
            ProcessMissedDosages().ConfigureAwait(false);
        }
        _loggerService.Log("Products loaded successfully.");
    }

    private async Task ProcessMissedDosages()
    {
        DateTime now = DateTime.Now;

        _loggerService.Log("Processing missed dosages...");

        foreach (var product in _products)
        {
            // Check for missed dosages
            foreach (var dosage in product.Dosages.ToList())
            {
                if (dosage.NextDose.HasValue && dosage.NextDose.Value < now.AddHours(-1))
                {
                    // This dosage is missed (more than 1 hour late)
                    // Create a MissedDosage record
                    var missedDosage = new MissedDosage
                    {
                        Id = product.MissedDosages.Count > 0 ? product.MissedDosages.Max(m => m.Id) + 1 : 1,
                        ProductId = product.Id,
                        DosageId = dosage.Id,
                        ScheduledTime = dosage.NextDose.Value,
                        Processed = false
                    };

                    product.MissedDosages.Add(missedDosage);

                    // Update the NextDose time
                    dosage.NextDose = CalculateNextDoseTime(dosage, now);
                }
            }
        }

        _loggerService.Log("Missed dosages processed.");
        // Save changes
        await SaveProductsAsync();
    }

    private async Task SaveProductsAsync()
    {
        _loggerService.Log("Saving products to file...");
        var json = JsonSerializer.Serialize(_products);
        await File.WriteAllTextAsync(_filePath, json);
        _loggerService.Log("Products saved successfully.");
    }

    public async Task<List<Product>> GetProductsAsync()
    {
        _loggerService.Log("Fetching products...");
        await ProcessMissedDosages(); // Check for missed dosages before returning products
        _loggerService.Log($"Fetched {_products.Count} products.");
        return _products;
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        _loggerService.Log($"Fetching product with ID {id}...");
        await ProcessMissedDosages(); // Check for missed dosages
        _loggerService.Log($"Fetched product with ID {id}.");
        return _products.FirstOrDefault(p => p.Id == id);
    }

    public async Task<Product> AddProductAsync(Product product)
    {
        _loggerService.Log($"Adding new product: {product.Name}");
        product.Id = _products.Count > 0 ? _products.Max(p => p.Id) + 1 : 1;
        product.History ??= new List<UsageHistory>();
        product.MissedDosages ??= new List<MissedDosage>();

        // Calculate and set the NextDose for each dosage
        foreach (var dosage in product.Dosages)
        {
            dosage.NextDose = CalculateNextDoseTime(dosage, null);
        }

        _products.Add(product);
        await SaveProductsAsync();
        _loggerService.Log($"Product added with ID {product.Id}");

        // Schedule notifications for new product
        await ScheduleNotificationsForProduct(product);
        _loggerService.Log($"Scheduled notifications for new product {product.Id}");
        return product;
    }

    public async Task UpdateProductAsync(Product product)
    {
        _loggerService.Log($"Updating product with ID {product.Id}");
        var index = _products.FindIndex(p => p.Id == product.Id);
        if (index != -1)
        {
            try
            {
                // Preserve history if it exists in the current product but not in the updated one
                if (product.History == null || !product.History.Any())
                {
                    product.History = _products[index].History ?? new List<UsageHistory>();
                }

                // Preserve missed dosages if not in the updated product
                if (product.MissedDosages == null || !product.MissedDosages.Any())
                {
                    product.MissedDosages = _products[index].MissedDosages ?? new List<MissedDosage>();
                }

                // Make sure all dosages have NextDose set
                foreach (var dosage in product.Dosages)
                {
                    if (dosage.NextDose == null)
                    {
                        // Get the matching dosage from the original product if it exists
                        var originalDosage = _products[index].Dosages.FirstOrDefault(d => d.Id == dosage.Id);
                        if (originalDosage?.NextDose != null)
                        {
                            dosage.NextDose = originalDosage.NextDose;
                        }
                        else
                        {
                            // Calculate new NextDose time
                            dosage.NextDose = CalculateNextDoseTime(dosage, null);
                        }
                    }
                }

                // Cancel existing notifications
                await _notificationService.CancelNotificationsAsync(product.Id);

                _products[index] = product;
                await SaveProductsAsync();

                // Add debug output
                _loggerService.Log($"Scheduling notifications for updated product {product.Id}");

                // Schedule new notifications and await it
                await ScheduleNotificationsForProduct(product);

                _loggerService.Log($"Notifications scheduled for product {product.Id}");
            }
            catch (Exception ex)
            {
                _loggerService.Log($"Error updating product notifications: {ex.Message}");
                throw;
            }
        }
        _loggerService.Log($"Product with ID {product.Id} updated successfully");
    }

    public async Task DeleteProductAsync(int id)
    {
        _loggerService.Log($"Deleting product with ID {id}");
        // Cancel notifications before deleting
        await _notificationService.CancelNotificationsAsync(id);

        _products.RemoveAll(p => p.Id == id);
        await SaveProductsAsync();
        _loggerService.Log($"Product with ID {id} deleted successfully");
    }

    // Explicit implementation of the interface method without optional parameter
    public async Task<bool> TakeProductDoseAsync(int productId, double amount)
    {
        // Call the overloaded method with null dosageId
        return await TakeProductDoseAsync(productId, amount, null);
    }

    public async Task<bool> TakeProductDoseAsync(int productId, double amount, int? dosageId)
    {
        _loggerService.Log($"Taking product dose: Product ID {productId}, Amount {amount}, Dosage ID {dosageId}");
        var product = _products.FirstOrDefault(p => p.Id == productId);
        if (product == null) return false;

        // Check if there's enough quantity
        if (product.Quantity >= amount)
        {
            // Reduce the quantity
            product.Quantity -= amount;

            // Determine the scheduled time
            DateTime? scheduleTime = null;

            if (dosageId.HasValue)
            {
                // Find the matching dosage
                var dosage = product.Dosages.FirstOrDefault(d => d.Id == dosageId.Value);
                if (dosage != null)
                {
                    // Use NextDose as the schedule time if available
                    if (dosage.NextDose.HasValue)
                    {
                        scheduleTime = dosage.NextDose;
                    }
                    else if (dosage.Time.HasValue)
                    {
                        // Fallback to calculating it
                        var now = DateTime.Now;
                        scheduleTime = new DateTime(
                            now.Year,
                            now.Month,
                            now.Day,
                            dosage.Time.Value.Hour,
                            dosage.Time.Value.Minute,
                            0);
                    }

                    // Remove any matching missed dosage if within a reasonable timeframe
                    if (scheduleTime.HasValue)
                    {
                        var missedDosage = product.MissedDosages.FirstOrDefault(m =>
                            m.DosageId == dosageId.Value &&
                            Math.Abs((m.ScheduledTime - scheduleTime.Value).TotalHours) < 12);

                        if (missedDosage != null)
                        {
                            product.MissedDosages.Remove(missedDosage);
                        }
                    }

                    // Calculate and set next dose time based on the scheduled time (not current time)
                    // This ensures proper scheduling even if taking tomorrow's dose today
                    dosage.NextDose = CalculateNextDoseTime(dosage, scheduleTime ?? DateTime.Now);
                }
            }

            // Record the product intake in history
            var historyEntry = new UsageHistory
            {
                Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1,
                ProductId = productId,
                Timestamp = DateTime.Now,
                AmountTaken = amount,
                DosageId = dosageId,
                ScheduleTime = scheduleTime,
                Event = EventType.Taken
            };

            product.History.Add(historyEntry);
            await SaveProductsAsync();

            // Reschedule notifications after taking a product to update the next dose time
            if (dosageId.HasValue)
            {
                await _notificationService.CancelNotificationsAsync(productId);
                await ScheduleNotificationsForProduct(product);
            }

            _loggerService.Log($"Product dose taken successfully: Product ID {productId}, Amount {amount}, Dosage ID {dosageId}");
            return true;
        }
        _loggerService.Log($"Not enough quantity to take product dose: Product ID {productId}, Amount {amount}");
        return false;
    }

    public async Task<bool> TakeProductDoseFromScheduleAsync(int productId, int dosageId)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId);
        if (product == null) return false;

        var dosage = product.Dosages.FirstOrDefault(d => d.Id == dosageId);
        if (dosage == null) return false;

        // Use the dosage amount for the product intake
        return await TakeProductDoseAsync(productId, dosage.AmountTaken, dosageId);
    }

    public async Task<bool> SkipMissedDosageAsync(int productId, int missedDosageId)
    {
        _loggerService.Log($"Skipping missed dosage: Product ID {productId}, Missed Dosage ID {missedDosageId}");
        var product = _products.FirstOrDefault(p => p.Id == productId);
        if (product == null) return false;

        var missedDosage = product.MissedDosages.FirstOrDefault(m => m.Id == missedDosageId);
        if (missedDosage == null) return false;

        // Mark as processed
        missedDosage.Processed = true;

        // Create a history entry for skipping this dose
        var historyEntry = new UsageHistory
        {
            Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1,
            ProductId = productId,
            Timestamp = DateTime.Now,
            AmountTaken = 0, // 0 amount for skipped doses
            DosageId = missedDosage.DosageId,
            ScheduleTime = missedDosage.ScheduledTime,
            Event = EventType.Skipped
        };

        product.History.Add(historyEntry);

        // Remove the missed dosage from the list
        product.MissedDosages.Remove(missedDosage);

        await SaveProductsAsync();
        _loggerService.Log($"Missed dosage skipped successfully: Product ID {productId}, Missed Dosage ID {missedDosageId}");
        return true;
    }

    public async Task<List<UsageHistory>> GetProductHistoryAsync(int productId)
    {
        _loggerService.Log($"Fetching history for product ID {productId}");
        var product = _products.FirstOrDefault(p => p.Id == productId);
        _loggerService.Log($"Fetched history for product ID {productId}");
        return product?.History?.OrderByDescending(h => h.Timestamp).ToList() ?? new List<UsageHistory>();
    }

    public async Task<List<UsageHistory>> GetAllProductHistoryAsync()
    {
        _loggerService.Log("Fetching all product history...");
        var allHistory = new List<UsageHistory>();
        foreach (var product in _products)
        {
            if (product.History != null && product.History.Any())
            {
                allHistory.AddRange(product.History);
            }
        }
        _loggerService.Log($"Fetched all product history. Total entries: {allHistory.Count}");
        return allHistory.OrderByDescending(h => h.Timestamp).ToList();
    }

    public async Task<List<MissedDosage>> GetMissedDosagesAsync(int? productId = null)
    {
        _loggerService.Log($"Fetching missed dosages for product ID {productId}");
        await ProcessMissedDosages(); // Make sure missed dosages are up to date

        if (productId.HasValue)
        {
            var product = _products.FirstOrDefault(p => p.Id == productId.Value);
            _loggerService.Log($"Fetched missed dosages for product ID {productId}");
            return product?.MissedDosages?.OrderBy(m => m.ScheduledTime).ToList() ?? new List<MissedDosage>();
        }
        else
        {
            var allMissedDosages = new List<MissedDosage>();
            foreach (var product in _products)
            {
                if (product.MissedDosages != null && product.MissedDosages.Any())
                {
                    allMissedDosages.AddRange(product.MissedDosages);
                }
            }
            _loggerService.Log($"Fetched missed dosages for all products. Total missed dosages: {allMissedDosages.Count}");
            return allMissedDosages.OrderBy(m => m.ScheduledTime).ToList();
        }
    }

    public async Task AddProductHistoryManuallyAsync(UsageHistory history)
    {
        _loggerService.Log($"Adding product history manually: Product ID {history.ProductId}, Event {history.Event}");
        var product = _products.FirstOrDefault(p => p.Id == history.ProductId);
        if (product != null)
        {
            // Assign a new ID to the history entry
            history.Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1;

            // Add the history entry
            product.History.Add(history);

            // If this is a Taken or Skipped event with a dosage ID, update the NextDose time
            if ((history.Event == EventType.Taken || history.Event == EventType.Skipped) && history.DosageId.HasValue)
            {
                var dosage = product.Dosages.FirstOrDefault(d => d.Id == history.DosageId.Value);
                if (dosage != null)
                {
                    // Use the scheduled time rather than timestamp for calculating next dose
                    dosage.NextDose = CalculateNextDoseTime(dosage, history.ScheduleTime ?? history.Timestamp);
                }
            }

            await SaveProductsAsync();

            // Reschedule notifications if this was for a specific dosage
            if (history.DosageId.HasValue)
            {
                await _notificationService.CancelNotificationsAsync(product.Id);
                await ScheduleNotificationsForProduct(product);
            }
        }
        _loggerService.Log($"Product history added manually: Product ID {history.ProductId}, Event {history.Event}");
    }

    private async Task ScheduleNotificationsForProduct(Product product)
    {
        _loggerService.Log($"Scheduling notifications for product {product.Id}");
        // We'll schedule notifications for each dosage based on its NextDose time
        foreach (var dosage in product.Dosages)
        {
            if (dosage.NextDose.HasValue)
            {
                // Use the precalculated NextDose time
                await _notificationService.ScheduleNotificationAsync(product, dosage, dosage.NextDose.Value);
            }
            else
            {
                // Calculate it if not available
                DateTime nextDoseTime = CalculateNextDoseTime(dosage, null);
                dosage.NextDose = nextDoseTime;

                // Schedule notification for this next dose
                await _notificationService.ScheduleNotificationAsync(product, dosage, nextDoseTime);
            }
        }

        // Save the updated NextDose times
        await SaveProductsAsync();
        _loggerService.Log($"Notifications scheduled for product {product.Id}");
    }

    private DateTime CalculateNextDoseTime(DosageSchedule dosage, DateTime? lastTakenTime)
    {
        _loggerService.Log($"Calculating next dose time for product {dosage.ProductId}, Dosage ID {dosage.Id}");
        // Current time to base calculations on
        DateTime now = lastTakenTime ?? DateTime.Now;

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
            if (baseTime < DateTime.Now)
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
            _loggerService.Log($"Next dose time calculated: {baseTime}");
            return baseTime;
        }
        else
        {
            // We have a record of when this product was last taken or scheduled
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
                if (nextDose < DateTime.Now)
                {
                    int daysToAdd = dosage.Repetition - (int)(DateTime.Now - nextDose).TotalDays % dosage.Repetition;
                    if (daysToAdd == 0 || (DateTime.Now - nextDose).TotalDays % dosage.Repetition == 0)
                    {
                        daysToAdd = dosage.Repetition;
                    }
                    nextDose = DateTime.Now.Date.AddDays(daysToAdd);

                    // Reset time component
                    nextDose = new DateTime(
                        nextDose.Year,
                        nextDose.Month,
                        nextDose.Day,
                        dosageTimeOfDay.Hour,
                        dosageTimeOfDay.Minute,
                        0);
                }
                _loggerService.Log($"Next dose time calculated: {nextDose}");
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
                if (nextWeeklyDose < DateTime.Now)
                {
                    nextWeeklyDose = FindNextWeekdayOccurrence(DateTime.Now, dosage.SelectedDays);
                }

                // Set the time part from the dosage's scheduled time
                nextWeeklyDose = new DateTime(
                    nextWeeklyDose.Year,
                    nextWeeklyDose.Month,
                    nextWeeklyDose.Day,
                    dosageTimeOfDay.Hour,
                    dosageTimeOfDay.Minute,
                    0);
                _loggerService.Log($"Next weekly dose time calculated: {nextWeeklyDose}");
                return nextWeeklyDose;
            }

            // Default case, schedule for tomorrow at the dosage time
            _loggerService.Log($"Defaulting to next day for dosage time: {baseTime.AddDays(1)}");
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
        _loggerService.Log($"Rescheduling notifications for product {pill.Id}");
        try
        {
            // Cancel any existing notifications
            await _notificationService.CancelNotificationsAsync(pill.Id);
            // Schedule new notifications
            await ScheduleNotificationsForProduct(pill);
        }
        catch (Exception ex)
        {
            _loggerService.Log($"Failed to reschedule notifications for product {pill.Id}: {ex.Message}");
        }
        _loggerService.Log($"Notifications rescheduled for product {pill.Id}");
    }
    public async Task<bool> TakeMissedDosage(int productId, int missedDosageId, double amountTaken)
    {
        _loggerService.Log($"Taking missed dosage: Product ID {productId}, Missed Dosage ID {missedDosageId}, Amount Taken {amountTaken}");
        var product = _products.FirstOrDefault(p => p.Id == productId);
        if (product == null) return false;

        // Find the missed dosage
        var missedDosage = product.MissedDosages.FirstOrDefault(m => m.Id == missedDosageId);
        if (missedDosage == null) return false;

        // Check if we have enough product
        if (product.Quantity < amountTaken) return false;

        // Reduce the quantity
        product.Quantity -= amountTaken;

        // Create a history entry for taking this dose
        var historyEntry = new UsageHistory
        {
            Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1,
            ProductId = productId,
            Timestamp = DateTime.Now,
            AmountTaken = amountTaken,
            DosageId = missedDosage.DosageId,
            ScheduleTime = missedDosage.ScheduledTime,
            Event = EventType.Taken
        };

        // Add the history entry
        product.History.Add(historyEntry);

        // Remove the missed dosage
        product.MissedDosages.Remove(missedDosage);

        // If there's a related dosage, update its NextDose time
        var dosage = product.Dosages.FirstOrDefault(d => d.Id == missedDosage.DosageId);
        if (dosage != null)
        {
            dosage.NextDose = CalculateNextDoseTime(dosage, missedDosage.ScheduledTime);
        }

        // Save changes
        await SaveProductsAsync();

        // Reschedule notifications
        await _notificationService.CancelNotificationsAsync(productId);
        await ScheduleNotificationsForProduct(product);

        _loggerService.Log($"Missed dosage taken successfully: Product ID {productId}, Missed Dosage ID {missedDosageId}");
        return true;
    }
}
