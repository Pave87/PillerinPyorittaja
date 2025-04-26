// MauiBlazorHybrid/Services/IPillService.cs
using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.Services;

public interface IPillService
{
    Task<List<Pill>> GetPillsAsync();
    Task<Pill?> GetPillAsync(int id);
    Task<Pill> AddPillAsync(Pill pill);
    Task UpdatePillAsync(Pill pill);
    Task DeletePillAsync(int id);
    Task<bool> TakePillDoseAsync(int pillId, double amount);
    Task<bool> TakePillDoseAsync(int pillId, double amount, int? dosageId);
    Task<List<PillHistory>> GetPillHistoryAsync(int pillId);
    Task<List<PillHistory>> GetAllPillHistoryAsync();
    Task AddPillHistoryManuallyAsync(PillHistory history);
}
