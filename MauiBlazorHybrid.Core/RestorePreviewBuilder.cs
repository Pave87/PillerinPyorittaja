using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.Core;

/// <summary>
/// Pure computation of a <see cref="RestorePreview"/> by diffing a backup's
/// product list against the current local product list. No IO, no crypto,
/// no services — suitable for unit testing.
/// </summary>
public static class RestorePreviewBuilder
{
    /// <summary>
    /// Compares local products against backup products and returns a preview describing
    /// what would change if the backup were applied.
    /// </summary>
    /// <param name="localProducts">The products currently stored on the device.</param>
    /// <param name="backupProducts">The products contained in the backup.</param>
    /// <param name="jsonBytes">The raw JSON bytes that would be written to disk if applied.</param>
    /// <returns>A populated <see cref="RestorePreview"/>.</returns>
    public static RestorePreview Build(
        IReadOnlyList<Product> localProducts,
        IReadOnlyList<Product> backupProducts,
        byte[] jsonBytes)
    {
        var localById = localProducts.ToDictionary(p => p.Id);
        var backupById = backupProducts.ToDictionary(p => p.Id);

        var preview = new RestorePreview { JsonBytes = jsonBytes };

        // Products in backup but not local — additions
        foreach (var bp in backupProducts)
        {
            if (!localById.ContainsKey(bp.Id))
                preview.AddedProducts.Add(bp.Name);
        }

        // Products in local but not backup — would be lost
        foreach (var lp in localProducts)
        {
            if (!backupById.ContainsKey(lp.Id))
                preview.RemovedProducts.Add(lp.Name);
        }

        // Products in both — check for differences
        foreach (var lp in localProducts)
        {
            if (!backupById.TryGetValue(lp.Id, out var bp))
                continue;

            if (HasProductConfigChanged(lp, bp))
                preview.ModifiedProducts.Add(lp.Name);

            // Compare history entries by Id
            var localHistoryIds = new HashSet<int>(lp.History.Select(h => h.Id));
            var backupHistoryIds = new HashSet<int>(bp.History.Select(h => h.Id));

            preview.AddedHistoryEntries += backupHistoryIds.Except(localHistoryIds).Count();
            preview.LostHistoryEntries += localHistoryIds.Except(backupHistoryIds).Count();
        }

        return preview;
    }

    /// <summary>
    /// Compares two products' configuration fields (ignoring transient state like History and MissedDosages).
    /// Returns true if anything a user would consider a "setting" differs between the two.
    /// </summary>
    public static bool HasProductConfigChanged(Product local, Product backup)
    {
        if (local.Name != backup.Name) return true;
        if (local.Quantity != backup.Quantity) return true;
        if (local.LowLimit != backup.LowLimit) return true;
        if (local.Unit != backup.Unit) return true;
        if (local.AmountPerPackage != backup.AmountPerPackage) return true;
        if (local.ConsumeByNeed != backup.ConsumeByNeed) return true;
        if (local.ConsumeByNeedAmount != backup.ConsumeByNeedAmount) return true;
        if (local.Dosages.Count != backup.Dosages.Count) return true;

        // Compare dosage schedules by Id
        var localDosageIds = new HashSet<int>(local.Dosages.Select(d => d.Id));
        var backupDosageIds = new HashSet<int>(backup.Dosages.Select(d => d.Id));
        if (!localDosageIds.SetEquals(backupDosageIds)) return true;

        return false;
    }
}
