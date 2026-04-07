using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.Core;

/// <summary>
/// Static class containing all product business logic extracted from ProductService.
/// The only change from the original code is replacing DateTime.Now with a currentTime parameter for testability.
/// </summary>
public static class ProductOperations
{
    /// <summary>
    /// Result of taking a product dose. Contains all the mutations that occurred.
    /// </summary>
    public class TakeDoseResult
    {
        public bool Success { get; set; }
        public UsageHistory? HistoryEntry { get; set; }
        public MissedDosage? MatchedMissedDosage { get; set; }
        public DateTime? NewNextDose { get; set; }
    }

    /// <summary>
    /// Result of processing missed dosages. Contains auto-skipped entries.
    /// </summary>
    public class ProcessMissedResult
    {
        public List<MissedDosage> NewMissedDosages { get; set; } = new();
        public List<UsageHistory> AutoSkippedEntries { get; set; } = new();
        public List<MissedDosage> RemovedMissedDosages { get; set; } = new();
    }

    /// <summary>
    /// Adds quantity to a product's inventory.
    /// Extracted from ProductService.AddPacketAsync.
    /// </summary>
    public static bool AddPacket(Product product, double amountToAdd)
    {
        if (product == null)
            return false;

        // Add the amount to the product quantity
        product.Quantity += amountToAdd;
        return true;
    }

    /// <summary>
    /// Initializes a new product: assigns ID, ensures collections are initialized, calculates NextDose for each dosage.
    /// Extracted from ProductService.AddProductAsync.
    /// </summary>
    public static void InitializeNewProduct(Product product, List<Product> existingProducts, DateTime currentTime)
    {
        product.Id = existingProducts.Count > 0 ? existingProducts.Max(p => p.Id) + 1 : 1;
        product.History ??= new List<UsageHistory>();
        product.MissedDosages ??= new List<MissedDosage>();

        // Calculate and set the NextDose for each dosage
        foreach (var dosage in product.Dosages)
        {
            dosage.NextDose = DoseCalculator.CalculateNextDoseTime(dosage, null, currentTime);
        }
    }

    /// <summary>
    /// Updates a product, preserving history and missed dosages if not provided, and ensuring NextDose is set.
    /// Extracted from ProductService.UpdateProductAsync.
    /// Returns the index of the product in the list, or -1 if not found.
    /// </summary>
    public static int UpdateProduct(Product updatedProduct, List<Product> products, DateTime currentTime)
    {
        var index = products.FindIndex(p => p.Id == updatedProduct.Id);
        if (index == -1)
            return -1;

        // Preserve history if it exists in the current product but not in the updated one
        if (updatedProduct.History == null || !updatedProduct.History.Any())
        {
            updatedProduct.History = products[index].History ?? new List<UsageHistory>();
        }

        // Preserve missed dosages if not in the updated product
        if (updatedProduct.MissedDosages == null || !updatedProduct.MissedDosages.Any())
        {
            updatedProduct.MissedDosages = products[index].MissedDosages ?? new List<MissedDosage>();
        }

        // Make sure all dosages have NextDose set
        foreach (var dosage in updatedProduct.Dosages)
        {
            if (dosage.NextDose == null)
            {
                // Get the matching dosage from the original product if it exists
                var originalDosage = products[index].Dosages.FirstOrDefault(d => d.Id == dosage.Id);
                if (originalDosage?.NextDose != null)
                {
                    dosage.NextDose = originalDosage.NextDose;
                }
                else
                {
                    // Find the last taken/skipped time for this dosage from history
                    var lastTaken = updatedProduct.History
                        .Where(h => h.DosageId == dosage.Id && (h.Event == EventType.Taken || h.Event == EventType.Skipped))
                        .OrderByDescending(h => h.ScheduleTime ?? h.Timestamp)
                        .Select(h => h.ScheduleTime ?? h.Timestamp)
                        .Cast<DateTime?>()
                        .FirstOrDefault();

                    // Calculate new NextDose time
                    dosage.NextDose = DoseCalculator.CalculateNextDoseTime(dosage, lastTaken, currentTime);
                }
            }
        }

        products[index] = updatedProduct;
        return index;
    }

    /// <summary>
    /// Takes a product dose: validates quantity, reduces it, matches missed dosages, creates history entry, calculates next dose.
    /// Extracted from ProductService.TakeProductDoseAsync.
    /// </summary>
    public static TakeDoseResult TakeProductDose(Product product, double amount, int? dosageId, DateTime currentTime)
    {
        var result = new TakeDoseResult { Success = false };

        if (product == null) return result;

        // Check if there's enough quantity
        if (product.Quantity < amount) return result;

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
                    scheduleTime = new DateTime(
                        currentTime.Year,
                        currentTime.Month,
                        currentTime.Day,
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
                        result.MatchedMissedDosage = missedDosage;
                        product.MissedDosages.Remove(missedDosage);
                    }
                }

                // Calculate and set next dose time based on the scheduled time (not current time)
                // This ensures proper scheduling even if taking tomorrow's dose today
                dosage.NextDose = DoseCalculator.CalculateNextDoseTime(dosage, scheduleTime ?? currentTime, currentTime);
                result.NewNextDose = dosage.NextDose;
            }
        }

        // Record the product intake in history
        var historyEntry = new UsageHistory
        {
            Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1,
            ProductId = product.Id,
            Timestamp = currentTime,
            AmountTaken = amount,
            DosageId = dosageId,
            ScheduleTime = scheduleTime,
            Event = EventType.Taken
        };

        product.History.Add(historyEntry);
        result.HistoryEntry = historyEntry;
        result.Success = true;
        return result;
    }

    /// <summary>
    /// Skips a missed dosage: marks as processed, creates history entry with 0 amount, removes from list.
    /// Extracted from ProductService.SkipMissedDosageAsync.
    /// </summary>
    public static UsageHistory? SkipMissedDosage(Product product, int missedDosageId, DateTime currentTime)
    {
        if (product == null) return null;

        var missedDosage = product.MissedDosages.FirstOrDefault(m => m.Id == missedDosageId);
        if (missedDosage == null) return null;

        // Mark as processed
        missedDosage.Processed = true;

        // Create a history entry for skipping this dose
        var historyEntry = new UsageHistory
        {
            Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1,
            ProductId = product.Id,
            Timestamp = currentTime,
            AmountTaken = 0, // 0 amount for skipped doses
            DosageId = missedDosage.DosageId,
            ScheduleTime = missedDosage.ScheduledTime,
            Event = EventType.Skipped
        };

        product.History.Add(historyEntry);

        // Remove the missed dosage from the list
        product.MissedDosages.Remove(missedDosage);

        return historyEntry;
    }

    /// <summary>
    /// Takes a missed dosage: validates quantity, reduces it, creates history entry, removes missed dosage, recalculates NextDose.
    /// Extracted from ProductService.TakeMissedDosage.
    /// </summary>
    public static TakeDoseResult TakeMissedDosage(Product product, int missedDosageId, double amountTaken, DateTime currentTime)
    {
        var result = new TakeDoseResult { Success = false };

        if (product == null) return result;

        // Find the missed dosage
        var missedDosage = product.MissedDosages.FirstOrDefault(m => m.Id == missedDosageId);
        if (missedDosage == null) return result;

        // Check if we have enough product
        if (product.Quantity < amountTaken) return result;

        // Reduce the quantity
        product.Quantity -= amountTaken;

        // Create a history entry for taking this dose
        var historyEntry = new UsageHistory
        {
            Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1,
            ProductId = product.Id,
            Timestamp = currentTime,
            AmountTaken = amountTaken,
            DosageId = missedDosage.DosageId,
            ScheduleTime = missedDosage.ScheduledTime,
            Event = EventType.Taken
        };

        // Add the history entry
        product.History.Add(historyEntry);
        result.HistoryEntry = historyEntry;

        // Remove the missed dosage
        result.MatchedMissedDosage = missedDosage;
        product.MissedDosages.Remove(missedDosage);

        // If there's a related dosage, update its NextDose time
        var dosage = product.Dosages.FirstOrDefault(d => d.Id == missedDosage.DosageId);
        if (dosage != null)
        {
            dosage.NextDose = DoseCalculator.CalculateNextDoseTime(dosage, missedDosage.ScheduledTime, currentTime);
            result.NewNextDose = dosage.NextDose;
        }

        result.Success = true;
        return result;
    }

    /// <summary>
    /// Processes missed dosages for all products: detects missed (>1h late), creates MissedDosage records,
    /// auto-skips oldest when more than 2 missed per dosage.
    /// Extracted from ProductService.ProcessMissedDosages.
    /// </summary>
    public static ProcessMissedResult ProcessMissedDosages(List<Product> products, DateTime currentTime)
    {
        var result = new ProcessMissedResult();

        foreach (var product in products)
        {
            // Check for missed dosages
            foreach (var dosage in product.Dosages.ToList())
            {
                if (dosage.NextDose.HasValue && dosage.NextDose.Value < currentTime.AddHours(-1))
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
                    result.NewMissedDosages.Add(missedDosage);

                    // Update the NextDose time
                    dosage.NextDose = DoseCalculator.CalculateNextDoseTime(dosage, currentTime, currentTime);
                }
            }

            // For each dosage, if more than 2 missed, auto-skip oldest
            var missedByDosage = product.MissedDosages
                .GroupBy(m => m.DosageId)
                .ToList();

            foreach (var group in missedByDosage)
            {
                var missedList = group.OrderBy(m => m.ScheduledTime).ToList();
                if (missedList.Count > 2)
                {
                    // Skip all but the 2 most recent
                    var toSkip = missedList.Take(missedList.Count - 2).ToList();
                    foreach (var missed in toSkip)
                    {
                        // Mark as processed
                        missed.Processed = true;

                        // Create a history entry for skipping this dose
                        var historyEntry = new UsageHistory
                        {
                            Id = product.History.Count > 0 ? product.History.Max(h => h.Id) + 1 : 1,
                            ProductId = product.Id,
                            Timestamp = currentTime,
                            AmountTaken = 0, // 0 amount for skipped doses
                            DosageId = missed.DosageId,
                            ScheduleTime = missed.ScheduledTime,
                            Event = EventType.Skipped
                        };

                        product.History.Add(historyEntry);
                        result.AutoSkippedEntries.Add(historyEntry);

                        // Remove the missed dosage from the list
                        product.MissedDosages.Remove(missed);
                        result.RemovedMissedDosages.Add(missed);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Adds a manual history entry and updates NextDose if it was a Taken or Skipped event.
    /// Extracted from ProductService.AddProductHistoryManuallyAsync.
    /// </summary>
    public static bool AddProductHistoryManually(Product product, UsageHistory history, DateTime currentTime)
    {
        if (product == null) return false;

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
                dosage.NextDose = DoseCalculator.CalculateNextDoseTime(dosage, history.ScheduleTime ?? history.Timestamp, currentTime);
            }
        }

        return true;
    }

    /// <summary>
    /// Initializes product collections after loading from storage (ensures no nulls).
    /// Extracted from ProductService.LoadProducts.
    /// </summary>
    public static void InitializeLoadedProducts(List<Product> products, DateTime currentTime)
    {
        foreach (var product in products)
        {
            product.History ??= new List<UsageHistory>();
            product.MissedDosages ??= new List<MissedDosage>();

            // Calculate next dose for each dosage if not set
            foreach (var dosage in product.Dosages)
            {
                if (dosage.NextDose == null)
                {
                    dosage.NextDose = DoseCalculator.CalculateNextDoseTime(dosage, null, currentTime);
                }
            }
        }
    }
}
