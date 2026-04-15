using MauiBlazorHybrid.Core;
using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.Core.Tests;

public class RestorePreviewBuilderTests
{
    private static readonly byte[] DummyJson = new byte[] { 1, 2, 3 };

    private static Product MakeProduct(
        int id,
        string name = "Prod",
        double quantity = 10,
        double lowLimit = 2,
        string unit = "pill",
        double amountPerPackage = 30,
        bool consumeByNeed = false,
        double consumeByNeedAmount = 1,
        List<DosageSchedule>? dosages = null,
        List<UsageHistory>? history = null)
    {
        return new Product
        {
            Id = id,
            Name = name,
            Quantity = quantity,
            LowLimit = lowLimit,
            Unit = unit,
            AmountPerPackage = amountPerPackage,
            ConsumeByNeed = consumeByNeed,
            ConsumeByNeedAmount = consumeByNeedAmount,
            Dosages = dosages ?? new List<DosageSchedule>(),
            History = history ?? new List<UsageHistory>()
        };
    }

    private static UsageHistory MakeHistory(int id, int productId = 1)
    {
        return new UsageHistory
        {
            Id = id,
            ProductId = productId,
            Timestamp = new DateTime(2026, 1, 1).AddMinutes(id),
            AmountTaken = 1,
            Event = EventType.Taken
        };
    }

    #region Empty / identical cases

    /// <summary>
    /// Test: Both local and backup lists are empty.
    /// Assumptions: No products on either side.
    /// Expectation: IsIdentical is true, no added/removed/modified, no history counts, JsonBytes passed through.
    /// </summary>
    [Fact]
    public void Build_BothEmpty_IsIdentical()
    {
        var preview = RestorePreviewBuilder.Build(new List<Product>(), new List<Product>(), DummyJson);

        Assert.True(preview.IsIdentical);
        Assert.False(preview.HasDestructiveChanges);
        Assert.Empty(preview.AddedProducts);
        Assert.Empty(preview.RemovedProducts);
        Assert.Empty(preview.ModifiedProducts);
        Assert.Equal(0, preview.AddedHistoryEntries);
        Assert.Equal(0, preview.LostHistoryEntries);
        Assert.Same(DummyJson, preview.JsonBytes);
    }

    /// <summary>
    /// Test: Local and backup contain the exact same single product with identical config and history.
    /// Assumptions: Products match by Id; config fields match; history Id sets match.
    /// Expectation: IsIdentical is true.
    /// </summary>
    [Fact]
    public void Build_SameProductSameHistory_IsIdentical()
    {
        var local = new List<Product> { MakeProduct(1, "A", history: new() { MakeHistory(1), MakeHistory(2) }) };
        var backup = new List<Product> { MakeProduct(1, "A", history: new() { MakeHistory(1), MakeHistory(2) }) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.True(preview.IsIdentical);
        Assert.False(preview.HasDestructiveChanges);
    }

    #endregion

    #region Additive-only cases

    /// <summary>
    /// Test: Backup contains a product that doesn't exist locally.
    /// Assumptions: Local is empty, backup has one product.
    /// Expectation: AddedProducts contains the backup product name, no removed/modified, not destructive.
    /// </summary>
    [Fact]
    public void Build_BackupHasNewProduct_ListedAsAdded()
    {
        var local = new List<Product>();
        var backup = new List<Product> { MakeProduct(1, "NewProd") };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.AddedProducts);
        Assert.Equal("NewProd", preview.AddedProducts[0]);
        Assert.Empty(preview.RemovedProducts);
        Assert.Empty(preview.ModifiedProducts);
        Assert.False(preview.HasDestructiveChanges);
        Assert.False(preview.IsIdentical);
    }

    /// <summary>
    /// Test: Same product exists in both but backup has additional history entries.
    /// Assumptions: New history on backup side is a superset of local.
    /// Expectation: AddedHistoryEntries counts the extras, LostHistoryEntries is zero, not destructive.
    /// </summary>
    [Fact]
    public void Build_BackupHasExtraHistory_AdditiveOnly()
    {
        var local = new List<Product> { MakeProduct(1, history: new() { MakeHistory(1) }) };
        var backup = new List<Product> { MakeProduct(1, history: new() { MakeHistory(1), MakeHistory(2), MakeHistory(3) }) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Equal(2, preview.AddedHistoryEntries);
        Assert.Equal(0, preview.LostHistoryEntries);
        Assert.False(preview.HasDestructiveChanges);
    }

    /// <summary>
    /// Test: Multiple new products appear in backup with no conflicts.
    /// Assumptions: Local has one product, backup has that same one plus two more.
    /// Expectation: AddedProducts lists the two new ones in order, nothing lost.
    /// </summary>
    [Fact]
    public void Build_MultipleNewProducts_AllListedAsAdded()
    {
        var local = new List<Product> { MakeProduct(1, "Existing") };
        var backup = new List<Product>
        {
            MakeProduct(1, "Existing"),
            MakeProduct(2, "NewA"),
            MakeProduct(3, "NewB"),
        };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Equal(2, preview.AddedProducts.Count);
        Assert.Contains("NewA", preview.AddedProducts);
        Assert.Contains("NewB", preview.AddedProducts);
        Assert.False(preview.HasDestructiveChanges);
    }

    #endregion

    #region Destructive cases

    /// <summary>
    /// Test: Local has a product that doesn't exist in the backup.
    /// Assumptions: Applying the backup would erase this product.
    /// Expectation: RemovedProducts lists it; HasDestructiveChanges is true.
    /// </summary>
    [Fact]
    public void Build_LocalHasProductBackupDoesnt_IsDestructive()
    {
        var local = new List<Product> { MakeProduct(1, "LocalOnly") };
        var backup = new List<Product>();

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.RemovedProducts);
        Assert.Equal("LocalOnly", preview.RemovedProducts[0]);
        Assert.True(preview.HasDestructiveChanges);
    }

    /// <summary>
    /// Test: Local history contains entries the backup doesn't have.
    /// Assumptions: Restoring would wipe those entries.
    /// Expectation: LostHistoryEntries counts them; HasDestructiveChanges is true.
    /// </summary>
    [Fact]
    public void Build_LocalHasExtraHistory_IsDestructive()
    {
        var local = new List<Product> { MakeProduct(1, history: new() { MakeHistory(1), MakeHistory(2), MakeHistory(3) }) };
        var backup = new List<Product> { MakeProduct(1, history: new() { MakeHistory(1) }) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Equal(2, preview.LostHistoryEntries);
        Assert.Equal(0, preview.AddedHistoryEntries);
        Assert.True(preview.HasDestructiveChanges);
    }

    /// <summary>
    /// Test: Both sides have overlapping history plus unique entries on each side.
    /// Assumptions: Symmetric difference — some entries only in local, some only in backup.
    /// Expectation: Both AddedHistoryEntries and LostHistoryEntries are non-zero; still destructive.
    /// </summary>
    [Fact]
    public void Build_HistoryDiverges_CountsBothSides()
    {
        var local = new List<Product> { MakeProduct(1, history: new() { MakeHistory(1), MakeHistory(2) }) };
        var backup = new List<Product> { MakeProduct(1, history: new() { MakeHistory(2), MakeHistory(3) }) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Equal(1, preview.AddedHistoryEntries);   // id 3
        Assert.Equal(1, preview.LostHistoryEntries);    // id 1
        Assert.True(preview.HasDestructiveChanges);
    }

    #endregion

    #region Product config change detection

    /// <summary>
    /// Test: Same Id, different Name.
    /// Expectation: Listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductNameDiffers_IsModified()
    {
        var local = new List<Product> { MakeProduct(1, name: "Old") };
        var backup = new List<Product> { MakeProduct(1, name: "New") };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.ModifiedProducts);
        Assert.Equal("Old", preview.ModifiedProducts[0]);
    }

    /// <summary>
    /// Test: Same Id, different Quantity.
    /// Expectation: Listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductQuantityDiffers_IsModified()
    {
        var local = new List<Product> { MakeProduct(1, quantity: 10) };
        var backup = new List<Product> { MakeProduct(1, quantity: 20) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.ModifiedProducts);
    }

    /// <summary>
    /// Test: Same Id, different LowLimit.
    /// Expectation: Listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductLowLimitDiffers_IsModified()
    {
        var local = new List<Product> { MakeProduct(1, lowLimit: 1) };
        var backup = new List<Product> { MakeProduct(1, lowLimit: 5) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.ModifiedProducts);
    }

    /// <summary>
    /// Test: Same Id, different Unit.
    /// Expectation: Listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductUnitDiffers_IsModified()
    {
        var local = new List<Product> { MakeProduct(1, unit: "pill") };
        var backup = new List<Product> { MakeProduct(1, unit: "ml") };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.ModifiedProducts);
    }

    /// <summary>
    /// Test: Same Id, different AmountPerPackage.
    /// Expectation: Listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductAmountPerPackageDiffers_IsModified()
    {
        var local = new List<Product> { MakeProduct(1, amountPerPackage: 30) };
        var backup = new List<Product> { MakeProduct(1, amountPerPackage: 60) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.ModifiedProducts);
    }

    /// <summary>
    /// Test: Same Id, different ConsumeByNeed flag.
    /// Expectation: Listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductConsumeByNeedDiffers_IsModified()
    {
        var local = new List<Product> { MakeProduct(1, consumeByNeed: false) };
        var backup = new List<Product> { MakeProduct(1, consumeByNeed: true) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.ModifiedProducts);
    }

    /// <summary>
    /// Test: Same Id, different ConsumeByNeedAmount.
    /// Expectation: Listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductConsumeByNeedAmountDiffers_IsModified()
    {
        var local = new List<Product> { MakeProduct(1, consumeByNeedAmount: 1) };
        var backup = new List<Product> { MakeProduct(1, consumeByNeedAmount: 2) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.ModifiedProducts);
    }

    /// <summary>
    /// Test: Same Id, same config fields but different dosage schedule Ids.
    /// Expectation: Listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductDosageIdsDiffer_IsModified()
    {
        var local = new List<Product>
        {
            MakeProduct(1, dosages: new() { new DosageSchedule { Id = 10, ProductId = 1, Frequency = "Days" } })
        };
        var backup = new List<Product>
        {
            MakeProduct(1, dosages: new() { new DosageSchedule { Id = 20, ProductId = 1, Frequency = "Days" } })
        };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.ModifiedProducts);
    }

    /// <summary>
    /// Test: Same Id, different dosage schedule counts.
    /// Expectation: Listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductDosageCountDiffers_IsModified()
    {
        var local = new List<Product>
        {
            MakeProduct(1, dosages: new() { new DosageSchedule { Id = 10, ProductId = 1, Frequency = "Days" } })
        };
        var backup = new List<Product>
        {
            MakeProduct(1, dosages: new()
            {
                new DosageSchedule { Id = 10, ProductId = 1, Frequency = "Days" },
                new DosageSchedule { Id = 11, ProductId = 1, Frequency = "Days" },
            })
        };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Single(preview.ModifiedProducts);
    }

    /// <summary>
    /// Test: Completely identical product, including same dosage schedule Ids.
    /// Expectation: NOT listed as modified.
    /// </summary>
    [Fact]
    public void Build_ProductsAreEqual_NotModified()
    {
        var dosages = () => new List<DosageSchedule>
        {
            new DosageSchedule { Id = 10, ProductId = 1, Frequency = "Days" },
        };
        var local = new List<Product> { MakeProduct(1, dosages: dosages()) };
        var backup = new List<Product> { MakeProduct(1, dosages: dosages()) };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Empty(preview.ModifiedProducts);
        Assert.True(preview.IsIdentical);
    }

    #endregion

    #region Mixed complex scenario

    /// <summary>
    /// Test: Realistic mixed scenario — one new product added, one removed, one modified, plus history diff.
    /// Assumptions: All four kinds of change happen in a single diff.
    /// Expectation: Each category is counted correctly and the preview is destructive.
    /// </summary>
    [Fact]
    public void Build_MixedChanges_AllCategoriesPopulated()
    {
        var local = new List<Product>
        {
            MakeProduct(1, "Existing", quantity: 10, history: new() { MakeHistory(1), MakeHistory(2) }),
            MakeProduct(2, "WillBeLost"),
        };
        var backup = new List<Product>
        {
            MakeProduct(1, "Existing", quantity: 20, history: new() { MakeHistory(1), MakeHistory(2), MakeHistory(3) }),
            MakeProduct(3, "BrandNew"),
        };

        var preview = RestorePreviewBuilder.Build(local, backup, DummyJson);

        Assert.Contains("BrandNew", preview.AddedProducts);
        Assert.Contains("WillBeLost", preview.RemovedProducts);
        Assert.Contains("Existing", preview.ModifiedProducts);
        Assert.Equal(1, preview.AddedHistoryEntries);
        Assert.Equal(0, preview.LostHistoryEntries);
        Assert.True(preview.HasDestructiveChanges);
        Assert.False(preview.IsIdentical);
    }

    /// <summary>
    /// Test: JsonBytes parameter is always passed through to the preview unchanged.
    /// Assumptions: Caller relies on preview.JsonBytes to apply the restore later.
    /// Expectation: The byte array reference is preserved.
    /// </summary>
    [Fact]
    public void Build_JsonBytes_PassedThroughToPreview()
    {
        var bytes = new byte[] { 10, 20, 30, 40 };

        var preview = RestorePreviewBuilder.Build(new List<Product>(), new List<Product>(), bytes);

        Assert.Same(bytes, preview.JsonBytes);
    }

    #endregion
}
