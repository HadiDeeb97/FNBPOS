using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Text;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

public class InventoryService(IDbContextFactory<PosDbContext> factory)
{
    public async Task<List<ProductVariant>> GetStockAsync(string? search = null, int? categoryId = null, bool lowStockOnly = false, bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.ProductVariants.AsNoTracking()
            .Include(v => v.Product).ThenInclude(p => p!.Category)
            .Where(v => includeInactive || (v.IsActive && v.Product!.IsActive));

        if (categoryId is not null) query = query.Where(v => v.Product!.CategoryId == categoryId);
        if (lowStockOnly) query = query.Where(v => v.StockQuantity <= v.ReorderLevel);
        foreach (var term in SmartSearch.Terms(search))
        {
            var pattern = SmartSearch.LikePattern(term);
            query = query.Where(v =>
                EF.Functions.Like(v.Product!.Name, pattern, "\\") ||
                EF.Functions.Like(v.Product!.Brand!, pattern, "\\") ||
                EF.Functions.Like(v.Product!.StyleCode!, pattern, "\\") ||
                EF.Functions.Like(v.Product!.Category!.Name, pattern, "\\") ||
                EF.Functions.Like(v.Sku, pattern, "\\") ||
                EF.Functions.Like(v.Color, pattern, "\\") ||
                v.Size == term ||
                v.Barcode == term);
        }

        return await query.OrderBy(v => v.Product!.Name).ThenBy(v => v.Color).ThenBy(v => v.Id).ToListAsync(ct);
    }

    /// <summary>Adds or removes stock (positive = in, negative = out) with a reason.</summary>
    public async Task<ProductVariant> AdjustStockAsync(int variantId, int change, StockMovementType type, string? notes, int userId, CancellationToken ct = default)
    {
        if (change == 0) throw new BusinessRuleException(Loc.T("Err.QtyChangeZero"));
        if (type is StockMovementType.Sale or StockMovementType.Return or StockMovementType.Void)
            throw new BusinessRuleException(Loc.T("Err.SalesAdjustAutomatically"));
        if (type == StockMovementType.Damaged && change > 0)
            throw new BusinessRuleException(Loc.T("Err.DamagedNegative"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var variant = await db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == variantId, ct)
                      ?? throw new BusinessRuleException(Loc.T("Err.ItemNotFound"));

        StockLedger.Apply(db, variant, change, type, userId, notes: QueryHelpers.Clean(notes));
        await SaveAsync(db, ct);
        return variant;
    }

    /// <summary>Records a physical count; the difference is booked as a stock-count movement.</summary>
    public async Task<ProductVariant> SetCountedStockAsync(int variantId, int countedQuantity, string? notes, int userId, CancellationToken ct = default)
    {
        if (countedQuantity < 0) throw new BusinessRuleException(Loc.T("Err.CountedNegative"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var variant = await db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == variantId, ct)
                      ?? throw new BusinessRuleException(Loc.T("Err.ItemNotFound"));

        var change = countedQuantity - variant.StockQuantity;
        if (change != 0)
        {
            // A count can correct negative stock, so allow the ledger to go through whatever the sign.
            StockLedger.Apply(db, variant, change, StockMovementType.StockCount, userId, notes: QueryHelpers.Clean(notes) ?? "Stock count", allowNegative: true);
            await SaveAsync(db, ct);
        }
        return variant;
    }

    public async Task UpdateReorderLevelAsync(int variantId, int reorderLevel, CancellationToken ct = default)
    {
        if (reorderLevel < 0) throw new BusinessRuleException(Loc.T("Err.ReorderNegative"));
        await using var db = await factory.CreateDbContextAsync(ct);
        var variant = await db.ProductVariants.FindAsync([variantId], ct) ?? throw new BusinessRuleException(Loc.T("Err.ItemNotFound"));
        variant.ReorderLevel = reorderLevel;
        await SaveAsync(db, ct);
    }

    public async Task<List<StockMovement>> GetMovementsAsync(int? variantId = null, DateTime? from = null, DateTime? to = null, int max = 500, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.StockMovements.AsNoTracking()
            .Include(m => m.ProductVariant).ThenInclude(v => v!.Product)
            .Include(m => m.User)
            .AsQueryable();

        if (variantId is not null) query = query.Where(m => m.ProductVariantId == variantId);
        if (from is not null) query = query.Where(m => m.CreatedAt >= from);
        if (to is not null) query = query.Where(m => m.CreatedAt < to);

        return await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(max).ToListAsync(ct);
    }

    private static async Task SaveAsync(PosDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.StockChangedRefresh"));
        }
    }
}
