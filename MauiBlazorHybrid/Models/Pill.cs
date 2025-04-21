// MauiBlazorHybrid/Models/Pill.cs
namespace MauiBlazorHybrid.Models;

public class Pill
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<PillDosage> Dosages { get; set; } = new();
    public double Quantity { get; set; } = 0;
    public double AmountPerPackage { get; set; } = 1;
}

public class PillDosage
{
    public int Id { get; set; }
    public int PillId { get; set; }
    public TimeOnly? Time { get; set; }
    public int Repetition { get; set; }
    public string Frequency { get; set; }
    public List<string> SelectedDays { get; set; } = new();
    public double AmountTaken { get; set; } = 1.0;
}
