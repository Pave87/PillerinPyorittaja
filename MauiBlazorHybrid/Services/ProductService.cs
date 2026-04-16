using MauiBlazorHybrid.Core;
using MauiBlazorHybrid.Models;
using System.Text.Json;

namespace MauiBlazorHybrid.Services;

public class ProductService : IProductService
{
    private readonly string _filePath;
    private List<Product> _products = new();
    private readonly INotificationService _notificationService;

    private readonly ILoggerService _loggerService; // Injected logger service

    public event Action? DataChanged;

    public ProductService(INotificationService notificationService)
    {
        using (CallContext.BeginCall())
        {
            _loggerService = new LoggerService();
            _loggerService.Log("Initalizing...");

            _notificationService = notificationService;
            // Migration logic: move pills.json to userdata.json if needed
            var appDataDir = FileSystem.AppDataDirectory;
            var oldFilePath = Path.Combine(appDataDir, "pills.json");
            var newFilePath = Path.Combine(appDataDir, "userdata.json");
            _filePath = newFilePath;

            if (!File.Exists(newFilePath) && File.Exists(oldFilePath))
            {
                try
                {
                    File.Move(oldFilePath, newFilePath);
                    _loggerService.Log("Migrated data file from pills.json to userdata.json");
                }
                catch (Exception ex)
                {
                    _loggerService.Log($"Failed to migrate data file: {ex.Message}");
                }
            }
            LoadProducts();

            _loggerService.Log("Initialized");
        }
    }

    public async Task<bool> AddPacketAsync(int productId, double amountToAdd)
    {
        using (CallContext.BeginCall())
        {
            try
            {
                _loggerService.Log($"Adding {amountToAdd} to product {productId}");

                // Get the current product
                var product = await GetProductAsync(productId);
                if (!ProductOperations.AddPacket(product, amountToAdd))
                    return false;

                // Update the product in storage
                await UpdateProductAsync(product);

                _loggerService.Log($"Added {amountToAdd} to product {productId}. New quantity: {product.Quantity}");
                DataChanged?.Invoke();
                return true;
            }
            catch
            {
                _loggerService.Log($"Failed to add {amountToAdd} to product {productId}");
                return false;
            }
        }
    }

    private void LoadProducts()
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log("Loading products from file...");
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _products = JsonSerializer.Deserialize<List<Product>>(json) ?? new();
                // Ensure all products have the necessary collections and NextDose set
                ProductOperations.InitializeLoadedProducts(_products, DateTime.Now);
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
    }

    private async Task ProcessMissedDosages()
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log("Processing missed dosages...");
            ProductOperations.ProcessMissedDosages(_products, DateTime.Now);
            _loggerService.Log("Missed dosages processed.");
            // Save changes
            await SaveProductsAsync();
        }
    }

    private async Task SaveProductsAsync()
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log("Saving products to file...");
            var json = JsonSerializer.Serialize(_products);
            await File.WriteAllTextAsync(_filePath, json);
            _loggerService.Log("Products saved successfully.");
        }
    }

    public async Task<List<Product>> GetProductsAsync()
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log("Fetching products...");
            await ProcessMissedDosages(); // Check for missed dosages before returning products
            _loggerService.Log($"Fetched {_products.Count} products.");
            return _products;
        }
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Fetching product with ID {id}...");
            await ProcessMissedDosages(); // Check for missed dosages
            _loggerService.Log($"Fetched product with ID {id}.");
            return _products.FirstOrDefault(p => p.Id == id);
        }
    }

    public async Task<Product> AddProductAsync(Product product)
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Adding new product: {product.Name}");
            ProductOperations.InitializeNewProduct(product, _products, DateTime.Now);

            _products.Add(product);
            await SaveProductsAsync();
            _loggerService.Log($"Product added with ID {product.Id}");

            // Schedule notifications for new product
            await ScheduleNotificationsForProduct(product);
            _loggerService.Log($"Scheduled notifications for new product {product.Id}");
            DataChanged?.Invoke();
            return product;
        }
    }

    public async Task UpdateProductAsync(Product product)
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Updating product with ID {product.Id}");
            var index = ProductOperations.UpdateProduct(product, _products, DateTime.Now);
            if (index != -1)
            {
                try
                {
                    // Cancel existing notifications
                    await _notificationService.CancelNotificationsAsync(product.Id);

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
            DataChanged?.Invoke();
        }
    }

    public async Task DeleteProductAsync(int id)
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Deleting product with ID {id}");
            // Cancel notifications before deleting
            await _notificationService.CancelNotificationsAsync(id);

            _products.RemoveAll(p => p.Id == id);
            await SaveProductsAsync();
            _loggerService.Log($"Product with ID {id} deleted successfully");
            DataChanged?.Invoke();
        }
    }

    // Explicit implementation of the interface method without optional parameter
    public async Task<bool> TakeProductDoseAsync(int productId, double amount)
    {
        // Call the overloaded method with null dosageId
        return await TakeProductDoseAsync(productId, amount, null);
    }

    public async Task<bool> TakeProductDoseAsync(int productId, double amount, int? dosageId)
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Taking product dose: Product ID {productId}, Amount {amount}, Dosage ID {dosageId}");
            var product = _products.FirstOrDefault(p => p.Id == productId);
            if (product == null) return false;

            var result = ProductOperations.TakeProductDose(product, amount, dosageId, DateTime.Now);
            if (!result.Success)
            {
                _loggerService.Log($"Not enough quantity to take product dose: Product ID {productId}, Amount {amount}");
                return false;
            }

            await SaveProductsAsync();

            // Reschedule notifications after taking a product to update the next dose time
            if (dosageId.HasValue)
            {
                await _notificationService.CancelNotificationsAsync(productId);
                await ScheduleNotificationsForProduct(product);
            }

            _loggerService.Log($"Product dose taken successfully: Product ID {productId}, Amount {amount}, Dosage ID {dosageId}");
            DataChanged?.Invoke();
            return true;
        }
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
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Skipping missed dosage: Product ID {productId}, Missed Dosage ID {missedDosageId}");
            var product = _products.FirstOrDefault(p => p.Id == productId);
            if (product == null) return false;

            var historyEntry = ProductOperations.SkipMissedDosage(product, missedDosageId, DateTime.Now);
            if (historyEntry == null) return false;

            await SaveProductsAsync();
            _loggerService.Log($"Missed dosage skipped successfully: Product ID {productId}, Missed Dosage ID {missedDosageId}");
            DataChanged?.Invoke();
            return true;
        }
    }

    public async Task<List<UsageHistory>> GetProductHistoryAsync(int productId)
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Fetching history for product ID {productId}");
            var product = _products.FirstOrDefault(p => p.Id == productId);
            _loggerService.Log($"Fetched history for product ID {productId}");
            return product?.History?.OrderByDescending(h => h.Timestamp).ToList() ?? new List<UsageHistory>();
        }
    }

    public async Task<List<UsageHistory>> GetAllProductHistoryAsync()
    {
        using (CallContext.BeginCall())
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
    }

    public async Task<List<MissedDosage>> GetMissedDosagesAsync(int? productId = null)
    {
        using (CallContext.BeginCall())
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
    }

    public async Task AddProductHistoryManuallyAsync(UsageHistory history)
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Adding product history manually: Product ID {history.ProductId}, Event {history.Event}");
            var product = _products.FirstOrDefault(p => p.Id == history.ProductId);
            if (ProductOperations.AddProductHistoryManually(product, history, DateTime.Now))
            {
                await SaveProductsAsync();

                // Reschedule notifications if this was for a specific dosage
                if (history.DosageId.HasValue)
                {
                    await _notificationService.CancelNotificationsAsync(product.Id);
                    await ScheduleNotificationsForProduct(product);
                }
                DataChanged?.Invoke();
            }
            _loggerService.Log($"Product history added manually: Product ID {history.ProductId}, Event {history.Event}");
        }
    }

    private async Task ScheduleNotificationsForProduct(Product product)
    {
        using (CallContext.BeginCall())
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
                    DateTime nextDoseTime = DoseCalculator.CalculateNextDoseTime(dosage, null, DateTime.Now);
                    dosage.NextDose = nextDoseTime;

                    // Schedule notification for this next dose
                    await _notificationService.ScheduleNotificationAsync(product, dosage, nextDoseTime);
                }
            }

            // Save the updated NextDose times
            await SaveProductsAsync();
            _loggerService.Log($"Notifications scheduled for product {product.Id}");
        }
    }

    private DateTime CalculateNextDoseTime(DosageSchedule dosage, DateTime? lastTakenTime)
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Calculating next dose time for product {dosage.ProductId}, Dosage ID {dosage.Id}");
            var result = DoseCalculator.CalculateNextDoseTime(dosage, lastTakenTime, DateTime.Now);
            _loggerService.Log($"Next dose time calculated: {result}");
            return result;
        }
    }

    private async Task RescheduleNotificationsForProduct(Product pill)
    {
        using (CallContext.BeginCall())
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
    }
    /// <summary>
    /// Reloads products from disk. LoadProducts() internally triggers
    /// ProcessMissedDosages() as fire-and-forget, so no need to call it again here.
    /// </summary>
    public async Task ReloadAsync()
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log("Reloading products from file...");
            LoadProducts();
            _loggerService.Log("Products reloaded successfully.");
        }
    }

    public async Task<bool> TakeMissedDosage(int productId, int missedDosageId, double amountTaken)
    {
        using (CallContext.BeginCall())
        {
            _loggerService.Log($"Taking missed dosage: Product ID {productId}, Missed Dosage ID {missedDosageId}, Amount Taken {amountTaken}");
            var product = _products.FirstOrDefault(p => p.Id == productId);
            if (product == null) return false;

            var result = ProductOperations.TakeMissedDosage(product, missedDosageId, amountTaken, DateTime.Now);
            if (!result.Success) return false;

            // Save changes
            await SaveProductsAsync();

            // Reschedule notifications
            await _notificationService.CancelNotificationsAsync(productId);
            await ScheduleNotificationsForProduct(product);

            _loggerService.Log($"Missed dosage taken successfully: Product ID {productId}, Missed Dosage ID {missedDosageId}");
            DataChanged?.Invoke();
            return true;
        }
    }
}
