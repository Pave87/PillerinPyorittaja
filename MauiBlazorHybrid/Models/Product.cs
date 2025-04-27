namespace MauiBlazorHybrid.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<DosageSchedule> Dosages { get; set; } = new();
    public double Quantity { get; set; } = 0;
    public string Unit { get; set; } = string.Empty;
    public double AmountPerPackage { get; set; } = 1;
    public List<UsageHistory> History { get; set; } = new();
}

public class DosageSchedule
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public TimeOnly? Time { get; set; }
    public int Repetition { get; set; }
    public string Frequency { get; set; }
    public List<string> SelectedDays { get; set; } = new();
    public double AmountTaken { get; set; } = 1.0;
}

public class UsageHistory
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public DateTime Timestamp { get; set; }
    public double AmountTaken { get; set; }
    public int? DosageId { get; set; }
}
