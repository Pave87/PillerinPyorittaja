using MauiBlazorHybrid.Core;
using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.Core.Tests;

public class ProductOperationsTests
{
    /// <summary>
    /// Creates a test product with sensible defaults.
    /// </summary>
    private Product CreateTestProduct(int id = 1, double quantity = 100, string name = "Test Product")
    {
        return new Product
        {
            Id = id,
            Name = name,
            Quantity = quantity,
            Unit = "mg",
            LowLimit = 10,
            AmountPerPackage = 30,
            History = new List<UsageHistory>(),
            MissedDosages = new List<MissedDosage>(),
            Dosages = new List<DosageSchedule>
            {
                new DosageSchedule
                {
                    Id = 1,
                    ProductId = id,
                    Frequency = "Days",
                    Repetition = 1,
                    Time = new TimeOnly(8, 0),
                    AmountTaken = 1.0,
                    NextDose = new DateTime(2026, 4, 7, 8, 0, 0)
                }
            }
        };
    }

    #region AddPacket

    /// <summary>
    /// Test: User adds a full package of medication to their inventory.
    /// Assumptions: Product exists with 50 units. User adds 30 units (one package).
    /// Expectation: Product quantity increases from 50 to 80.
    /// </summary>
    [Fact]
    public void AddPacket_ValidProduct_IncreasesQuantity()
    {
        var product = CreateTestProduct(quantity: 50);

        var result = ProductOperations.AddPacket(product, 30);

        Assert.True(result);
        Assert.Equal(80, product.Quantity);
    }

    /// <summary>
    /// Test: User tries to add inventory to a null product reference.
    /// Assumptions: Product reference is null (e.g., product was deleted).
    /// Expectation: Returns false, no crash.
    /// </summary>
    [Fact]
    public void AddPacket_NullProduct_ReturnsFalse()
    {
        var result = ProductOperations.AddPacket(null, 30);

        Assert.False(result);
    }

    /// <summary>
    /// Test: User adds a fractional amount (e.g., half a bottle of liquid medication).
    /// Assumptions: Product has 10.5 units. User adds 5.5 units.
    /// Expectation: Quantity becomes 16.0.
    /// </summary>
    [Fact]
    public void AddPacket_FractionalAmount_AddsCorrectly()
    {
        var product = CreateTestProduct(quantity: 10.5);

        var result = ProductOperations.AddPacket(product, 5.5);

        Assert.True(result);
        Assert.Equal(16.0, product.Quantity);
    }

    /// <summary>
    /// Test: User adds inventory to a product that currently has zero quantity.
    /// Assumptions: Product quantity is 0 (fully depleted).
    /// Expectation: Quantity becomes the amount added.
    /// </summary>
    [Fact]
    public void AddPacket_ZeroQuantity_AddsFromZero()
    {
        var product = CreateTestProduct(quantity: 0);

        var result = ProductOperations.AddPacket(product, 30);

        Assert.True(result);
        Assert.Equal(30, product.Quantity);
    }

    #endregion

    #region InitializeNewProduct

    /// <summary>
    /// Test: User creates the very first product in the app.
    /// Assumptions: No existing products. New product has one daily dosage.
    /// Expectation: Product gets ID 1, collections are initialized, NextDose is calculated.
    /// </summary>
    [Fact]
    public void InitializeNewProduct_FirstProduct_GetsId1()
    {
        var currentTime = new DateTime(2026, 4, 7, 7, 0, 0);
        var product = new Product
        {
            Name = "Vitamin D",
            Dosages = new List<DosageSchedule>
            {
                new DosageSchedule { Id = 1, Frequency = "Days", Repetition = 1, Time = new TimeOnly(8, 0) }
            }
        };
        var existingProducts = new List<Product>();

        ProductOperations.InitializeNewProduct(product, existingProducts, currentTime);

        Assert.Equal(1, product.Id);
        Assert.NotNull(product.History);
        Assert.NotNull(product.MissedDosages);
        Assert.NotNull(product.Dosages[0].NextDose);
        // 07:00 now, dose at 08:00 → today at 08:00
        Assert.Equal(new DateTime(2026, 4, 7, 8, 0, 0), product.Dosages[0].NextDose);
    }

    /// <summary>
    /// Test: User creates a product when other products already exist.
    /// Assumptions: Two existing products with IDs 1 and 3 (ID 2 was deleted).
    /// Expectation: New product gets ID 4 (max existing + 1).
    /// </summary>
    [Fact]
    public void InitializeNewProduct_ExistingProducts_GetsNextId()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = new Product
        {
            Name = "Magnesium",
            Dosages = new List<DosageSchedule>
            {
                new DosageSchedule { Id = 1, Frequency = "Days", Repetition = 1, Time = new TimeOnly(9, 0) }
            }
        };
        var existingProducts = new List<Product>
        {
            new Product { Id = 1, Name = "A", Dosages = new() },
            new Product { Id = 3, Name = "B", Dosages = new() }
        };

        ProductOperations.InitializeNewProduct(product, existingProducts, currentTime);

        Assert.Equal(4, product.Id);
    }

    /// <summary>
    /// Test: User creates a product with null History and MissedDosages (shouldn't happen but defensive).
    /// Assumptions: Product's History and MissedDosages are null.
    /// Expectation: Both are initialized to empty lists.
    /// </summary>
    [Fact]
    public void InitializeNewProduct_NullCollections_InitializesEmptyLists()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = new Product
        {
            Name = "Test",
            History = null,
            MissedDosages = null,
            Dosages = new List<DosageSchedule>()
        };

        ProductOperations.InitializeNewProduct(product, new List<Product>(), currentTime);

        Assert.NotNull(product.History);
        Assert.Empty(product.History);
        Assert.NotNull(product.MissedDosages);
        Assert.Empty(product.MissedDosages);
    }

    /// <summary>
    /// Test: User creates a product with multiple dosage schedules.
    /// Assumptions: Product has two dosages: morning 08:00 and evening 20:00. Current time is 10:00.
    /// Expectation: Morning dose (past) gets scheduled for tomorrow, evening dose gets scheduled for today.
    /// </summary>
    [Fact]
    public void InitializeNewProduct_MultipleDosages_AllGetNextDose()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = new Product
        {
            Name = "Medication",
            Dosages = new List<DosageSchedule>
            {
                new DosageSchedule { Id = 1, Frequency = "Days", Repetition = 1, Time = new TimeOnly(8, 0) },
                new DosageSchedule { Id = 2, Frequency = "Days", Repetition = 1, Time = new TimeOnly(20, 0) }
            }
        };

        ProductOperations.InitializeNewProduct(product, new List<Product>(), currentTime);

        // 08:00 is past → tomorrow
        Assert.Equal(new DateTime(2026, 4, 8, 8, 0, 0), product.Dosages[0].NextDose);
        // 20:00 is future → today
        Assert.Equal(new DateTime(2026, 4, 7, 20, 0, 0), product.Dosages[1].NextDose);
    }

    #endregion

    #region UpdateProduct

    /// <summary>
    /// Test: User updates a product's name but sends it without history (e.g., from an edit form that doesn't include history).
    /// Assumptions: Original product has 3 history entries. Updated product has empty history.
    /// Expectation: History is preserved from the original product.
    /// </summary>
    [Fact]
    public void UpdateProduct_EmptyHistory_PreservesOriginalHistory()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var original = CreateTestProduct();
        original.History.Add(new UsageHistory { Id = 1, ProductId = 1, Event = EventType.Taken, Timestamp = currentTime.AddDays(-1) });
        original.History.Add(new UsageHistory { Id = 2, ProductId = 1, Event = EventType.Taken, Timestamp = currentTime.AddDays(-2) });
        original.History.Add(new UsageHistory { Id = 3, ProductId = 1, Event = EventType.Taken, Timestamp = currentTime.AddDays(-3) });
        var products = new List<Product> { original };

        var updated = new Product
        {
            Id = 1,
            Name = "Renamed Product",
            History = new List<UsageHistory>(), // empty — should be preserved
            Dosages = new List<DosageSchedule>
            {
                new DosageSchedule { Id = 1, ProductId = 1, Frequency = "Days", Repetition = 1, Time = new TimeOnly(8, 0), NextDose = new DateTime(2026, 4, 8, 8, 0, 0) }
            }
        };

        var index = ProductOperations.UpdateProduct(updated, products, currentTime);

        Assert.Equal(0, index);
        Assert.Equal(3, products[0].History.Count);
    }

    /// <summary>
    /// Test: User updates a product but the updated version has null MissedDosages.
    /// Assumptions: Original product has 1 missed dosage. Updated product has null MissedDosages.
    /// Expectation: Missed dosages are preserved from the original.
    /// </summary>
    [Fact]
    public void UpdateProduct_NullMissedDosages_PreservesOriginal()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var original = CreateTestProduct();
        original.MissedDosages.Add(new MissedDosage { Id = 1, ProductId = 1, DosageId = 1, ScheduledTime = currentTime.AddHours(-2) });
        var products = new List<Product> { original };

        var updated = new Product
        {
            Id = 1,
            Name = "Updated",
            MissedDosages = null,
            Dosages = new List<DosageSchedule>
            {
                new DosageSchedule { Id = 1, ProductId = 1, Frequency = "Days", Repetition = 1, Time = new TimeOnly(8, 0), NextDose = new DateTime(2026, 4, 8, 8, 0, 0) }
            }
        };

        ProductOperations.UpdateProduct(updated, products, currentTime);

        Assert.Single(products[0].MissedDosages);
    }

    /// <summary>
    /// Test: User updates a product and adds a new dosage schedule that doesn't have NextDose set, but a matching original dosage exists.
    /// Assumptions: Original dosage with same ID has NextDose set. Updated dosage has null NextDose.
    /// Expectation: NextDose is copied from the original dosage.
    /// </summary>
    [Fact]
    public void UpdateProduct_NewDosageWithoutNextDose_CopiesFromOriginal()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var original = CreateTestProduct();
        original.Dosages[0].NextDose = new DateTime(2026, 4, 8, 8, 0, 0);
        var products = new List<Product> { original };

        var updated = new Product
        {
            Id = 1,
            Name = "Updated",
            History = new List<UsageHistory> { new UsageHistory { Id = 1 } }, // non-empty so it's not overwritten
            MissedDosages = new List<MissedDosage> { new MissedDosage { Id = 1 } },
            Dosages = new List<DosageSchedule>
            {
                new DosageSchedule { Id = 1, ProductId = 1, Frequency = "Days", Repetition = 1, Time = new TimeOnly(8, 0), NextDose = null }
            }
        };

        ProductOperations.UpdateProduct(updated, products, currentTime);

        Assert.Equal(new DateTime(2026, 4, 8, 8, 0, 0), products[0].Dosages[0].NextDose);
    }

    /// <summary>
    /// Test: User updates a product that has a brand new dosage (no matching original).
    /// Assumptions: Updated product has a dosage with ID 99 which doesn't exist in the original. NextDose is null.
    /// Expectation: NextDose is calculated fresh using DoseCalculator.
    /// </summary>
    [Fact]
    public void UpdateProduct_BrandNewDosage_CalculatesNextDose()
    {
        var currentTime = new DateTime(2026, 4, 7, 7, 0, 0);
        var original = CreateTestProduct();
        var products = new List<Product> { original };

        var updated = new Product
        {
            Id = 1,
            Name = "Updated",
            History = new List<UsageHistory> { new UsageHistory { Id = 1 } },
            MissedDosages = new List<MissedDosage> { new MissedDosage { Id = 1 } },
            Dosages = new List<DosageSchedule>
            {
                new DosageSchedule { Id = 99, ProductId = 1, Frequency = "Days", Repetition = 1, Time = new TimeOnly(9, 0), NextDose = null }
            }
        };

        ProductOperations.UpdateProduct(updated, products, currentTime);

        // 07:00 now, dose at 09:00 → today at 09:00
        Assert.Equal(new DateTime(2026, 4, 7, 9, 0, 0), products[0].Dosages[0].NextDose);
    }

    /// <summary>
    /// Test: User tries to update a product that doesn't exist.
    /// Assumptions: Product with ID 999 is not in the product list.
    /// Expectation: Returns -1, no changes to the list.
    /// </summary>
    [Fact]
    public void UpdateProduct_ProductNotFound_ReturnsNegativeOne()
    {
        var products = new List<Product> { CreateTestProduct(id: 1) };
        var nonExistent = new Product { Id = 999, Name = "Ghost", Dosages = new() };

        var index = ProductOperations.UpdateProduct(nonExistent, products, DateTime.Now);

        Assert.Equal(-1, index);
        Assert.Single(products);
        Assert.Equal(1, products[0].Id); // original unchanged
    }

    #endregion

    #region TakeProductDose

    /// <summary>
    /// Test: User takes their scheduled daily dose of medication.
    /// Assumptions: Product has 100 units. Dosage amount is 1 unit. NextDose is set to today 08:00.
    /// Expectation: Quantity reduces by 1 to 99. History entry is created with EventType.Taken. NextDose is recalculated for tomorrow.
    /// </summary>
    [Fact]
    public void TakeProductDose_NormalDose_ReducesQuantityAndCreatesHistory()
    {
        var currentTime = new DateTime(2026, 4, 7, 8, 5, 0);
        var product = CreateTestProduct(quantity: 100);

        var result = ProductOperations.TakeProductDose(product, 1.0, 1, currentTime);

        Assert.True(result.Success);
        Assert.Equal(99, product.Quantity);
        Assert.Single(product.History);
        Assert.Equal(EventType.Taken, result.HistoryEntry.Event);
        Assert.Equal(1.0, result.HistoryEntry.AmountTaken);
        Assert.Equal(1, result.HistoryEntry.DosageId);
    }

    /// <summary>
    /// Test: User tries to take a dose but doesn't have enough inventory.
    /// Assumptions: Product has 0.5 units. User tries to take 1 unit.
    /// Expectation: Returns failure. Quantity stays at 0.5. No history entry created.
    /// </summary>
    [Fact]
    public void TakeProductDose_InsufficientQuantity_Fails()
    {
        var currentTime = new DateTime(2026, 4, 7, 8, 0, 0);
        var product = CreateTestProduct(quantity: 0.5);

        var result = ProductOperations.TakeProductDose(product, 1.0, 1, currentTime);

        Assert.False(result.Success);
        Assert.Equal(0.5, product.Quantity);
        Assert.Empty(product.History);
    }

    /// <summary>
    /// Test: User takes a dose and there's a matching missed dosage within 12 hours.
    /// Assumptions: Product has a missed dosage scheduled at 08:00. User takes dose at 10:00 (2 hours later). The missed dosage is for the same dosage ID.
    /// Expectation: The missed dosage is matched and removed. Dose is taken normally.
    /// </summary>
    [Fact]
    public void TakeProductDose_WithMatchingMissedDosage_RemovesMissed()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = CreateTestProduct(quantity: 100);
        product.Dosages[0].NextDose = new DateTime(2026, 4, 7, 8, 0, 0);
        product.MissedDosages.Add(new MissedDosage
        {
            Id = 1,
            ProductId = 1,
            DosageId = 1,
            ScheduledTime = new DateTime(2026, 4, 7, 8, 0, 0),
            Processed = false
        });

        var result = ProductOperations.TakeProductDose(product, 1.0, 1, currentTime);

        Assert.True(result.Success);
        Assert.NotNull(result.MatchedMissedDosage);
        Assert.Empty(product.MissedDosages);
    }

    /// <summary>
    /// Test: User takes a dose but the missed dosage is for a different time (more than 12 hours apart).
    /// Assumptions: Missed dosage was at yesterday 08:00 (26 hours ago). NextDose is today 08:00.
    /// Expectation: The missed dosage is NOT matched (>12h gap). It stays in the list.
    /// </summary>
    [Fact]
    public void TakeProductDose_MissedDosageTooFarApart_DoesNotRemoveMissed()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = CreateTestProduct(quantity: 100);
        product.Dosages[0].NextDose = new DateTime(2026, 4, 7, 8, 0, 0);
        product.MissedDosages.Add(new MissedDosage
        {
            Id = 1,
            ProductId = 1,
            DosageId = 1,
            ScheduledTime = new DateTime(2026, 4, 5, 8, 0, 0), // 2 days ago — more than 12h from NextDose
            Processed = false
        });

        var result = ProductOperations.TakeProductDose(product, 1.0, 1, currentTime);

        Assert.True(result.Success);
        Assert.Null(result.MatchedMissedDosage);
        Assert.Single(product.MissedDosages); // still there
    }

    /// <summary>
    /// Test: User takes a dose without specifying a dosageId (ad-hoc dose, e.g., "consume by need").
    /// Assumptions: No dosageId provided. Product has 50 units.
    /// Expectation: Quantity reduces. History entry created with null DosageId. No NextDose recalculation.
    /// </summary>
    [Fact]
    public void TakeProductDose_NoDosageId_TakesWithoutScheduleUpdate()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = CreateTestProduct(quantity: 50);

        var result = ProductOperations.TakeProductDose(product, 2.0, null, currentTime);

        Assert.True(result.Success);
        Assert.Equal(48, product.Quantity);
        Assert.Single(product.History);
        Assert.Null(result.HistoryEntry.DosageId);
        Assert.Null(result.NewNextDose);
    }

    /// <summary>
    /// Test: User takes exactly the last remaining dose.
    /// Assumptions: Product has exactly 1 unit left. User takes 1 unit.
    /// Expectation: Success. Quantity becomes 0.
    /// </summary>
    [Fact]
    public void TakeProductDose_ExactlyEnoughQuantity_Succeeds()
    {
        var currentTime = new DateTime(2026, 4, 7, 8, 0, 0);
        var product = CreateTestProduct(quantity: 1.0);

        var result = ProductOperations.TakeProductDose(product, 1.0, 1, currentTime);

        Assert.True(result.Success);
        Assert.Equal(0, product.Quantity);
    }

    /// <summary>
    /// Test: User takes a dose for a dosage that has no NextDose set but has a Time.
    /// Assumptions: Dosage has Time = 08:00 but NextDose is null.
    /// Expectation: Schedule time is calculated from current date + dosage time. History entry records it.
    /// </summary>
    [Fact]
    public void TakeProductDose_NoNextDose_FallsBackToDosageTime()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = CreateTestProduct(quantity: 100);
        product.Dosages[0].NextDose = null;

        var result = ProductOperations.TakeProductDose(product, 1.0, 1, currentTime);

        Assert.True(result.Success);
        // Fallback: schedule time = today at 08:00
        Assert.Equal(new DateTime(2026, 4, 7, 8, 0, 0), result.HistoryEntry.ScheduleTime);
    }

    /// <summary>
    /// Test: History entry IDs are assigned correctly when product already has history.
    /// Assumptions: Product already has 3 history entries (IDs 1, 2, 3).
    /// Expectation: New entry gets ID 4.
    /// </summary>
    [Fact]
    public void TakeProductDose_ExistingHistory_AssignsNextId()
    {
        var currentTime = new DateTime(2026, 4, 7, 8, 0, 0);
        var product = CreateTestProduct(quantity: 100);
        product.History.Add(new UsageHistory { Id = 1 });
        product.History.Add(new UsageHistory { Id = 2 });
        product.History.Add(new UsageHistory { Id = 3 });

        var result = ProductOperations.TakeProductDose(product, 1.0, 1, currentTime);

        Assert.Equal(4, result.HistoryEntry.Id);
    }

    /// <summary>
    /// Test: Taking a dose recalculates NextDose based on the schedule time, not the current time.
    /// Assumptions: Dosage every 1 day at 08:00. NextDose is today 08:00. Current time is 08:05.
    /// Expectation: NextDose is recalculated to tomorrow 08:00 (based on today's scheduled time + 1 day).
    /// </summary>
    [Fact]
    public void TakeProductDose_RecalculatesNextDose_BasedOnScheduleTime()
    {
        var currentTime = new DateTime(2026, 4, 7, 8, 5, 0);
        var product = CreateTestProduct(quantity: 100);
        product.Dosages[0].NextDose = new DateTime(2026, 4, 7, 8, 0, 0);

        var result = ProductOperations.TakeProductDose(product, 1.0, 1, currentTime);

        Assert.True(result.Success);
        // NextDose was Apr 7 08:00 → passed as lastTakenTime → next = Apr 8 08:00
        Assert.Equal(new DateTime(2026, 4, 8, 8, 0, 0), result.NewNextDose);
    }

    #endregion

    #region SkipMissedDosage

    /// <summary>
    /// Test: User skips a missed dosage they don't want to take.
    /// Assumptions: Product has a missed dosage. User chooses to skip it.
    /// Expectation: Missed dosage is marked processed, history entry created with EventType.Skipped and amount 0, missed dosage removed from list.
    /// </summary>
    [Fact]
    public void SkipMissedDosage_ValidMissedDosage_CreatesSkippedHistoryAndRemoves()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = CreateTestProduct();
        product.MissedDosages.Add(new MissedDosage
        {
            Id = 1,
            ProductId = 1,
            DosageId = 1,
            ScheduledTime = new DateTime(2026, 4, 7, 8, 0, 0),
            Processed = false
        });

        var historyEntry = ProductOperations.SkipMissedDosage(product, 1, currentTime);

        Assert.NotNull(historyEntry);
        Assert.Equal(EventType.Skipped, historyEntry.Event);
        Assert.Equal(0, historyEntry.AmountTaken);
        Assert.Equal(1, historyEntry.DosageId);
        Assert.Equal(new DateTime(2026, 4, 7, 8, 0, 0), historyEntry.ScheduleTime);
        Assert.Empty(product.MissedDosages);
        Assert.Single(product.History);
    }

    /// <summary>
    /// Test: User tries to skip a missed dosage that doesn't exist.
    /// Assumptions: Missed dosage ID 999 doesn't exist in the product.
    /// Expectation: Returns null. No changes to product.
    /// </summary>
    [Fact]
    public void SkipMissedDosage_NonExistentId_ReturnsNull()
    {
        var product = CreateTestProduct();

        var result = ProductOperations.SkipMissedDosage(product, 999, DateTime.Now);

        Assert.Null(result);
        Assert.Empty(product.History);
    }

    /// <summary>
    /// Test: User skips a missed dosage on a null product.
    /// Assumptions: Product is null.
    /// Expectation: Returns null, no crash.
    /// </summary>
    [Fact]
    public void SkipMissedDosage_NullProduct_ReturnsNull()
    {
        var result = ProductOperations.SkipMissedDosage(null, 1, DateTime.Now);

        Assert.Null(result);
    }

    #endregion

    #region TakeMissedDosage

    /// <summary>
    /// Test: User decides to take a missed dose they forgot earlier.
    /// Assumptions: Product has 100 units and a missed dosage. User takes 1 unit for it.
    /// Expectation: Quantity reduces. History entry created. Missed dosage removed. NextDose recalculated based on the missed dose's scheduled time.
    /// </summary>
    [Fact]
    public void TakeMissedDosage_ValidMissedDosage_TakesAndRemoves()
    {
        var currentTime = new DateTime(2026, 4, 7, 12, 0, 0);
        var product = CreateTestProduct(quantity: 100);
        product.MissedDosages.Add(new MissedDosage
        {
            Id = 1,
            ProductId = 1,
            DosageId = 1,
            ScheduledTime = new DateTime(2026, 4, 7, 8, 0, 0),
            Processed = false
        });

        var result = ProductOperations.TakeMissedDosage(product, 1, 1.0, currentTime);

        Assert.True(result.Success);
        Assert.Equal(99, product.Quantity);
        Assert.Single(product.History);
        Assert.Equal(EventType.Taken, result.HistoryEntry.Event);
        Assert.Equal(new DateTime(2026, 4, 7, 8, 0, 0), result.HistoryEntry.ScheduleTime);
        Assert.Empty(product.MissedDosages);
        // NextDose recalculated based on missed dose scheduled time (Apr 7 08:00) + 1 day
        Assert.Equal(new DateTime(2026, 4, 8, 8, 0, 0), result.NewNextDose);
    }

    /// <summary>
    /// Test: User tries to take a missed dose but doesn't have enough inventory.
    /// Assumptions: Product has 0.5 units. User tries to take 1 unit.
    /// Expectation: Fails. Quantity unchanged. Missed dosage still in list.
    /// </summary>
    [Fact]
    public void TakeMissedDosage_InsufficientQuantity_Fails()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = CreateTestProduct(quantity: 0.5);
        product.MissedDosages.Add(new MissedDosage
        {
            Id = 1,
            ProductId = 1,
            DosageId = 1,
            ScheduledTime = new DateTime(2026, 4, 7, 8, 0, 0)
        });

        var result = ProductOperations.TakeMissedDosage(product, 1, 1.0, currentTime);

        Assert.False(result.Success);
        Assert.Equal(0.5, product.Quantity);
        Assert.Single(product.MissedDosages);
    }

    /// <summary>
    /// Test: User tries to take a missed dosage that doesn't exist.
    /// Assumptions: Missed dosage ID 999 not found.
    /// Expectation: Fails. No changes.
    /// </summary>
    [Fact]
    public void TakeMissedDosage_NonExistentMissedDosage_Fails()
    {
        var product = CreateTestProduct(quantity: 100);

        var result = ProductOperations.TakeMissedDosage(product, 999, 1.0, DateTime.Now);

        Assert.False(result.Success);
        Assert.Equal(100, product.Quantity);
    }

    #endregion

    #region ProcessMissedDosages

    /// <summary>
    /// Test: A dosage is more than 1 hour past its scheduled time and should be detected as missed.
    /// Assumptions: Dosage NextDose was at 08:00. Current time is 09:30 (1.5 hours later, more than 1 hour threshold).
    /// Expectation: A new MissedDosage record is created. NextDose is recalculated.
    /// </summary>
    [Fact]
    public void ProcessMissedDosages_DoseMoreThan1HourLate_CreatesMissedRecord()
    {
        var currentTime = new DateTime(2026, 4, 7, 9, 30, 0);
        var product = CreateTestProduct();
        product.Dosages[0].NextDose = new DateTime(2026, 4, 7, 8, 0, 0);
        var products = new List<Product> { product };

        var result = ProductOperations.ProcessMissedDosages(products, currentTime);

        Assert.Single(result.NewMissedDosages);
        Assert.Equal(new DateTime(2026, 4, 7, 8, 0, 0), result.NewMissedDosages[0].ScheduledTime);
        Assert.Equal(1, result.NewMissedDosages[0].DosageId);
        Assert.Single(product.MissedDosages);
        // NextDose should be recalculated to tomorrow
        Assert.NotEqual(new DateTime(2026, 4, 7, 8, 0, 0), product.Dosages[0].NextDose);
    }

    /// <summary>
    /// Test: A dosage is only 30 minutes late (less than the 1 hour threshold).
    /// Assumptions: Dosage NextDose was at 08:00. Current time is 08:30 (30 min late).
    /// Expectation: NOT detected as missed. No MissedDosage record created.
    /// </summary>
    [Fact]
    public void ProcessMissedDosages_DoseLessThan1HourLate_NotMissed()
    {
        var currentTime = new DateTime(2026, 4, 7, 8, 30, 0);
        var product = CreateTestProduct();
        product.Dosages[0].NextDose = new DateTime(2026, 4, 7, 8, 0, 0);
        var products = new List<Product> { product };

        var result = ProductOperations.ProcessMissedDosages(products, currentTime);

        Assert.Empty(result.NewMissedDosages);
        Assert.Empty(product.MissedDosages);
    }

    /// <summary>
    /// Test: A dosage with no NextDose set should not be detected as missed.
    /// Assumptions: Dosage NextDose is null.
    /// Expectation: No missed dosage created.
    /// </summary>
    [Fact]
    public void ProcessMissedDosages_NoNextDose_NotMissed()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var product = CreateTestProduct();
        product.Dosages[0].NextDose = null;
        var products = new List<Product> { product };

        var result = ProductOperations.ProcessMissedDosages(products, currentTime);

        Assert.Empty(result.NewMissedDosages);
    }

    /// <summary>
    /// Test: Three missed dosages exist for the same dosage schedule. The oldest should be auto-skipped.
    /// Assumptions: 3 missed dosages for dosage ID 1, scheduled at 08:00 on Apr 5, 6, and 7. Max allowed is 2.
    /// Expectation: The oldest (Apr 5) is auto-skipped: marked processed, history entry with Skipped event created, removed from list. The 2 most recent remain.
    /// </summary>
    [Fact]
    public void ProcessMissedDosages_MoreThan2Missed_AutoSkipsOldest()
    {
        var currentTime = new DateTime(2026, 4, 8, 10, 0, 0);
        var product = CreateTestProduct();
        product.Dosages[0].NextDose = new DateTime(2026, 4, 9, 8, 0, 0); // future, not missed
        product.MissedDosages = new List<MissedDosage>
        {
            new MissedDosage { Id = 1, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 5, 8, 0, 0) },
            new MissedDosage { Id = 2, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 6, 8, 0, 0) },
            new MissedDosage { Id = 3, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 7, 8, 0, 0) }
        };
        var products = new List<Product> { product };

        var result = ProductOperations.ProcessMissedDosages(products, currentTime);

        // Oldest (Apr 5) should be auto-skipped
        Assert.Single(result.AutoSkippedEntries);
        Assert.Equal(EventType.Skipped, result.AutoSkippedEntries[0].Event);
        Assert.Equal(0, result.AutoSkippedEntries[0].AmountTaken);
        Assert.Single(result.RemovedMissedDosages);
        // 2 most recent remain
        Assert.Equal(2, product.MissedDosages.Count);
        Assert.Equal(2, product.MissedDosages[0].Id); // Apr 6
        Assert.Equal(3, product.MissedDosages[1].Id); // Apr 7
    }

    /// <summary>
    /// Test: Five missed dosages exist. Three oldest should be auto-skipped, keeping only the 2 most recent.
    /// Assumptions: 5 missed dosages for the same dosage.
    /// Expectation: 3 oldest auto-skipped, 2 newest remain.
    /// </summary>
    [Fact]
    public void ProcessMissedDosages_FiveMissed_AutoSkipsThreeOldest()
    {
        var currentTime = new DateTime(2026, 4, 10, 10, 0, 0);
        var product = CreateTestProduct();
        product.Dosages[0].NextDose = new DateTime(2026, 4, 11, 8, 0, 0); // future
        product.MissedDosages = new List<MissedDosage>
        {
            new MissedDosage { Id = 1, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 5, 8, 0, 0) },
            new MissedDosage { Id = 2, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 6, 8, 0, 0) },
            new MissedDosage { Id = 3, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 7, 8, 0, 0) },
            new MissedDosage { Id = 4, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 8, 8, 0, 0) },
            new MissedDosage { Id = 5, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 9, 8, 0, 0) }
        };
        var products = new List<Product> { product };

        var result = ProductOperations.ProcessMissedDosages(products, currentTime);

        Assert.Equal(3, result.AutoSkippedEntries.Count);
        Assert.Equal(3, result.RemovedMissedDosages.Count);
        Assert.Equal(2, product.MissedDosages.Count);
        Assert.Equal(4, product.MissedDosages[0].Id); // Apr 8
        Assert.Equal(5, product.MissedDosages[1].Id); // Apr 9
    }

    /// <summary>
    /// Test: Two missed dosages exist (at the max). None should be auto-skipped.
    /// Assumptions: Exactly 2 missed dosages for dosage ID 1.
    /// Expectation: Both remain. No auto-skip.
    /// </summary>
    [Fact]
    public void ProcessMissedDosages_ExactlyTwoMissed_NoAutoSkip()
    {
        var currentTime = new DateTime(2026, 4, 8, 10, 0, 0);
        var product = CreateTestProduct();
        product.Dosages[0].NextDose = new DateTime(2026, 4, 9, 8, 0, 0);
        product.MissedDosages = new List<MissedDosage>
        {
            new MissedDosage { Id = 1, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 6, 8, 0, 0) },
            new MissedDosage { Id = 2, ProductId = 1, DosageId = 1, ScheduledTime = new DateTime(2026, 4, 7, 8, 0, 0) }
        };
        var products = new List<Product> { product };

        var result = ProductOperations.ProcessMissedDosages(products, currentTime);

        Assert.Empty(result.AutoSkippedEntries);
        Assert.Equal(2, product.MissedDosages.Count);
    }

    /// <summary>
    /// Test: Multiple products each have missed dosages. Both should be processed independently.
    /// Assumptions: Product A has a missed dose. Product B has a missed dose. Both are >1h late.
    /// Expectation: Both products get MissedDosage records created.
    /// </summary>
    [Fact]
    public void ProcessMissedDosages_MultipleProducts_ProcessesAll()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var productA = CreateTestProduct(id: 1, quantity: 100);
        productA.Dosages[0].NextDose = new DateTime(2026, 4, 7, 8, 0, 0);
        var productB = CreateTestProduct(id: 2, quantity: 50);
        productB.Dosages[0].ProductId = 2;
        productB.Dosages[0].NextDose = new DateTime(2026, 4, 7, 7, 0, 0);
        var products = new List<Product> { productA, productB };

        var result = ProductOperations.ProcessMissedDosages(products, currentTime);

        Assert.Equal(2, result.NewMissedDosages.Count);
        Assert.Single(productA.MissedDosages);
        Assert.Single(productB.MissedDosages);
    }

    #endregion

    #region AddProductHistoryManually

    /// <summary>
    /// Test: User manually logs a taken dose in the history (e.g., they took it but forgot to press the button).
    /// Assumptions: Product has dosage ID 1 with NextDose set. User adds a Taken event manually.
    /// Expectation: History entry is added with next ID. NextDose is recalculated based on the manually entered schedule time.
    /// </summary>
    [Fact]
    public void AddProductHistoryManually_TakenEvent_UpdatesNextDose()
    {
        var currentTime = new DateTime(2026, 4, 7, 12, 0, 0);
        var product = CreateTestProduct();
        var history = new UsageHistory
        {
            ProductId = 1,
            Timestamp = new DateTime(2026, 4, 7, 8, 0, 0),
            AmountTaken = 1.0,
            DosageId = 1,
            ScheduleTime = new DateTime(2026, 4, 7, 8, 0, 0),
            Event = EventType.Taken
        };

        var result = ProductOperations.AddProductHistoryManually(product, history, currentTime);

        Assert.True(result);
        Assert.Single(product.History);
        Assert.Equal(1, history.Id);
        // NextDose recalculated: schedule was Apr 7 08:00, daily rep 1 → next = Apr 8 08:00
        Assert.Equal(new DateTime(2026, 4, 8, 8, 0, 0), product.Dosages[0].NextDose);
    }

    /// <summary>
    /// Test: User manually logs a Skipped event. NextDose should still be recalculated.
    /// Assumptions: Skipped event with dosage ID 1.
    /// Expectation: History added. NextDose recalculated (Skipped events also trigger NextDose update).
    /// </summary>
    [Fact]
    public void AddProductHistoryManually_SkippedEvent_AlsoUpdatesNextDose()
    {
        var currentTime = new DateTime(2026, 4, 7, 12, 0, 0);
        var product = CreateTestProduct();
        var history = new UsageHistory
        {
            ProductId = 1,
            Timestamp = currentTime,
            AmountTaken = 0,
            DosageId = 1,
            ScheduleTime = new DateTime(2026, 4, 7, 8, 0, 0),
            Event = EventType.Skipped
        };

        ProductOperations.AddProductHistoryManually(product, history, currentTime);

        Assert.Equal(new DateTime(2026, 4, 8, 8, 0, 0), product.Dosages[0].NextDose);
    }

    /// <summary>
    /// Test: User manually adds a Refilled event (restocked inventory). No dosage ID.
    /// Assumptions: Event is Refilled, no DosageId.
    /// Expectation: History entry added. NextDose is NOT recalculated (only Taken/Skipped with dosageId triggers that).
    /// </summary>
    [Fact]
    public void AddProductHistoryManually_RefilledEvent_DoesNotUpdateNextDose()
    {
        var currentTime = new DateTime(2026, 4, 7, 12, 0, 0);
        var product = CreateTestProduct();
        var originalNextDose = product.Dosages[0].NextDose;
        var history = new UsageHistory
        {
            ProductId = 1,
            Timestamp = currentTime,
            AmountTaken = 30,
            DosageId = null,
            Event = EventType.Refilled
        };

        ProductOperations.AddProductHistoryManually(product, history, currentTime);

        Assert.Single(product.History);
        Assert.Equal(originalNextDose, product.Dosages[0].NextDose); // unchanged
    }

    /// <summary>
    /// Test: User manually adds history with no ScheduleTime (uses Timestamp as fallback).
    /// Assumptions: ScheduleTime is null. Timestamp is provided.
    /// Expectation: NextDose is recalculated using Timestamp as fallback.
    /// </summary>
    [Fact]
    public void AddProductHistoryManually_NoScheduleTime_UsesTimestampForNextDose()
    {
        var currentTime = new DateTime(2026, 4, 7, 12, 0, 0);
        var product = CreateTestProduct();
        var history = new UsageHistory
        {
            ProductId = 1,
            Timestamp = new DateTime(2026, 4, 7, 8, 0, 0),
            AmountTaken = 1.0,
            DosageId = 1,
            ScheduleTime = null, // no schedule time
            Event = EventType.Taken
        };

        ProductOperations.AddProductHistoryManually(product, history, currentTime);

        // Uses Timestamp (Apr 7 08:00) as lastTakenTime → next = Apr 8 08:00
        Assert.Equal(new DateTime(2026, 4, 8, 8, 0, 0), product.Dosages[0].NextDose);
    }

    /// <summary>
    /// Test: Adding history to a null product.
    /// Assumptions: Product is null.
    /// Expectation: Returns false, no crash.
    /// </summary>
    [Fact]
    public void AddProductHistoryManually_NullProduct_ReturnsFalse()
    {
        var result = ProductOperations.AddProductHistoryManually(null, new UsageHistory(), DateTime.Now);

        Assert.False(result);
    }

    #endregion

    #region InitializeLoadedProducts

    /// <summary>
    /// Test: Products loaded from file have null History and MissedDosages (old data format or corruption).
    /// Assumptions: Two products loaded, both have null History and null MissedDosages.
    /// Expectation: Both get initialized to empty lists.
    /// </summary>
    [Fact]
    public void InitializeLoadedProducts_NullCollections_InitializesToEmptyLists()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var products = new List<Product>
        {
            new Product { Id = 1, Name = "A", History = null, MissedDosages = null, Dosages = new List<DosageSchedule>() },
            new Product { Id = 2, Name = "B", History = null, MissedDosages = null, Dosages = new List<DosageSchedule>() }
        };

        ProductOperations.InitializeLoadedProducts(products, currentTime);

        Assert.NotNull(products[0].History);
        Assert.NotNull(products[0].MissedDosages);
        Assert.NotNull(products[1].History);
        Assert.NotNull(products[1].MissedDosages);
    }

    /// <summary>
    /// Test: Product loaded from file has a dosage with no NextDose.
    /// Assumptions: Dosage at 08:00 daily, NextDose is null. Current time is 07:00.
    /// Expectation: NextDose is calculated to today 08:00.
    /// </summary>
    [Fact]
    public void InitializeLoadedProducts_DosageWithoutNextDose_CalculatesIt()
    {
        var currentTime = new DateTime(2026, 4, 7, 7, 0, 0);
        var products = new List<Product>
        {
            new Product
            {
                Id = 1,
                Name = "Test",
                Dosages = new List<DosageSchedule>
                {
                    new DosageSchedule { Id = 1, ProductId = 1, Frequency = "Days", Repetition = 1, Time = new TimeOnly(8, 0), NextDose = null }
                }
            }
        };

        ProductOperations.InitializeLoadedProducts(products, currentTime);

        Assert.Equal(new DateTime(2026, 4, 7, 8, 0, 0), products[0].Dosages[0].NextDose);
    }

    /// <summary>
    /// Test: Product loaded from file already has NextDose set. Should not be overwritten.
    /// Assumptions: Dosage has NextDose already set to Apr 10 08:00.
    /// Expectation: NextDose stays as-is.
    /// </summary>
    [Fact]
    public void InitializeLoadedProducts_DosageWithExistingNextDose_DoesNotOverwrite()
    {
        var currentTime = new DateTime(2026, 4, 7, 10, 0, 0);
        var existingNextDose = new DateTime(2026, 4, 10, 8, 0, 0);
        var products = new List<Product>
        {
            new Product
            {
                Id = 1,
                Name = "Test",
                Dosages = new List<DosageSchedule>
                {
                    new DosageSchedule { Id = 1, Frequency = "Days", Repetition = 3, Time = new TimeOnly(8, 0), NextDose = existingNextDose }
                }
            }
        };

        ProductOperations.InitializeLoadedProducts(products, currentTime);

        Assert.Equal(existingNextDose, products[0].Dosages[0].NextDose);
    }

    #endregion
}
