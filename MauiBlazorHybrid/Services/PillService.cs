// MauiBlazorHybrid/Services/PillService.cs
using MauiBlazorHybrid.Models;
using System.Text.Json;

namespace MauiBlazorHybrid.Services;

public class PillService : IPillService
{
    private readonly string _filePath;
    private List<Pill> _pills = new();

    public PillService()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, "pills.json");
        LoadPills();
    }

    private void LoadPills()
    {
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            _pills = JsonSerializer.Deserialize<List<Pill>>(json) ?? new();
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
        return pill;
    }

    public async Task UpdatePillAsync(Pill pill)
    {
        var index = _pills.FindIndex(p => p.Id == pill.Id);
        if (index != -1)
        {
            _pills[index] = pill;
            await SavePillsAsync();
        }
    }

    public async Task DeletePillAsync(int id)
    {
        _pills.RemoveAll(p => p.Id == id);
        await SavePillsAsync();
    }
}
