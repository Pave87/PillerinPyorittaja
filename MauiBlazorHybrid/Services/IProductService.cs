using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.Services;

public interface IProductService
{
    Task<List<Product>> GetProductsAsync();
    Task<Product?> GetProductAsync(int id);
    Task<Product> AddProductAsync(Product product);
    Task UpdateProductAsync(Product product);
    Task DeleteProductAsync(int id);
    Task<bool> TakeProductDoseAsync(int productId, double amount);
    Task<bool> TakeProductDoseAsync(int productId, double amount, int? dosageId);
    Task<bool> AddPacketAsync(int productId, double amountToAdd);
    Task<List<UsageHistory>> GetProductHistoryAsync(int productId);
    Task<List<UsageHistory>> GetAllProductHistoryAsync();
    Task AddProductHistoryManuallyAsync(UsageHistory history);
    Task<List<MissedDosage>> GetMissedDosagesAsync(int? productId = null);
    Task<bool> SkipMissedDosageAsync(int productId, int missedDosageId);
    Task<bool> TakeMissedDosage(int productId, int missedDosageId, double amountTaken);
}
