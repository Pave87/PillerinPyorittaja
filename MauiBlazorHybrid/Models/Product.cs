namespace MauiBlazorHybrid.Models;

/// <summary>
/// Base product class that contains the product information.
/// </summary>
public class Product
{
    /// <summary>
    /// Unique identifier for the product.
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// Name of the product.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Schedules when product should be used.
    /// </summary>
    public List<DosageSchedule> Dosages { get; set; } = new();
    /// <summary>
    /// Current quantity of the product.
    /// </summary>
    public double Quantity { get; set; } = 0;
    /// <summary>
    /// Unit of measurement. Only used for display purposes.
    /// </summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>
    /// Amount of product in package. Used when refilling the product.
    /// </summary>
    public double AmountPerPackage { get; set; } = 1;
    /// <summary>
    /// History of product usage.
    /// </summary>
    public List<UsageHistory> History { get; set; } = new();
    /// <summary>
    /// List of missed usages.
    /// </summary>
    public List<MissedDosage> MissedDosages { get; set; } = new();
}

/// <summary>
/// Represents a schedule for taking a product.
/// </summary>
public class DosageSchedule
{
    /// <summary>
    /// Unique identifier for the dosage schedule.
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// Unique identifier for the product associated with this schedule.
    /// </summary>
    public int ProductId { get; set; }
    /// <summary>
    /// Time when the product should be used.
    /// </summary>
    public TimeOnly? Time { get; set; }
    /// <summary>
    /// Days/weeks between each usage.
    /// </summary>
    public int Repetition { get; set; }
    /// <summary>
    /// Frequency of the dosage schedule (e.g., daily, weekly).
    /// </summary>
    public string Frequency { get; set; }
    /// <summary>
    /// If Frequency is "Weekly", this property contains the days of the week when the product should be used.
    /// </summary>
    public List<string> SelectedDays { get; set; } = new();
    /// <summary>
    /// Amount of product to be used at once.
    /// </summary>
    public double AmountTaken { get; set; } = 1.0;
    /// <summary>
    /// When dosage is used next time.
    /// </summary>
    public DateTime? NextDose { get; set; }
}

/// <summary>
/// Represents a missed dosage.
/// </summary>
public class MissedDosage
{
    /// <summary>
    /// Unique identifier for the missed dosage.
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// Unique identifier for the product associated with this missed dosage.
    /// </summary>
    public int ProductId { get; set; }
    /// <summary>
    /// Unique identifier for the dosage schedule associated with this missed dosage.
    /// </summary>
    public int DosageId { get; set; }
    /// <summary>
    /// Time when the product should have been used.
    /// </summary>
    public DateTime ScheduledTime { get; set; }
    /// <summary>
    /// Has this been agknowledged by the user?
    /// </summary>
    public bool Processed { get; set; } = false;
}

/// <summary>
/// Represents the history of product usage.
/// </summary>
public class UsageHistory
{
    /// <summary>
    /// Unique identifier for the usage history entry.
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// Unique identifier for the product associated with this usage history entry.
    /// </summary>
    public int ProductId { get; set; }
    /// <summary>
    /// When this was originally scheduled to be used.
    /// </summary>
    public DateTime? ScheduleTime { get; set; }
    /// <summary>
    /// When this was actually used.
    /// </summary>
    public DateTime Timestamp { get; set; }
    /// <summary>
    /// Amount of product that was used.
    /// </summary>
    public double AmountTaken { get; set; }
    /// <summary>
    /// Unique identifier for the dosage schedule associated with this usage history entry.
    /// </summary>
    public int? DosageId { get; set; }
    /// <summary>
    /// Type of event that occurred (e.g., created, modified, taken, refilled, removed, skipped).
    /// </summary>
    public EventType Event { get; set; }
}

/// <summary>
/// Enumeration for the type of event that occurred.
/// </summary>
public enum EventType
{
    Created,
    Modified,
    Taken,
    Refilled,
    Removed,
    Skipped
}
