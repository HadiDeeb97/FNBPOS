using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Text;
using Microsoft.EntityFrameworkCore;
using UniPos.Core.Localization;

namespace UniPos.Data.Services;

/// <summary>Stock of the items that are counted (<see cref="Product.TrackStock"/>); items made to order have none.</summary>
public class InventoryService(IDbContextFactory<PosDbContext> factory)
{
    public async Task<List<Product>> GetStockAsync(string? search = null, int? categoryId = null, bool lowStockOnly = false, bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.TrackStock && (includeInactive || p.IsActive));

        if (categoryId is not null) query = query.Where(p => p.CategoryId == categoryId);
        if (lowStockOnly) query = query.Where(p => p.StockQuantity <= p.ReorderLevel);
        foreach (var term in SmartSearch.Terms(search))
        {
            var pattern = SmartSearch.LikePattern(term);
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern, "\\") ||
                EF.Functions.Like(p.Category!.Name, pattern, "\\") ||
                EF.Functions.Like(p.Sku, pattern, "\\") ||
                p.Barcode == term);
        }

        return await query.OrderBy(p => p.Name).ThenBy(p => p.Id).ToListAsync(ct);
    }

    /// <summary>Adds or removes stock (positive = in, negative = out) with a reason.</summary>
    public async Task<Product> AdjustStockAsync(int productId, int change, StockMovementType type, string? notes, int userId, CancellationToken ct = default)
    {
        if (change == 0) throw new BusinessRuleException(Loc.T("Err.QtyChangeZero"));
        if (type is StockMovementType.Sale or StockMovementType.Return or StockMovementType.Void)
            throw new BusinessRuleException(Loc.T("Err.SalesAdjustAutomatically"));
        if (type == StockMovementType.Damaged && change > 0)
            throw new BusinessRuleException(Loc.T("Err.DamagedNegative"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct)
                      ?? throw new BusinessRuleException(Loc.T("Err.ItemNotFound"));

        StockLedger.Apply(db, product, change, type, userId, notes: QueryHelpers.Clean(notes));
        await SaveAsync(db, ct);
        return product;
    }

    /// <summary>Records a physical count; the difference is booked as a stock-count movement.</summary>
    public async Task<Product> SetCountedStockAsync(int productId, int countedQuantity, string? notes, int userId, CancellationToken ct = default)
    {
        if (countedQuantity < 0) throw new BusinessRuleException(Loc.T("Err.CountedNegative"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct)
                      ?? throw new BusinessRuleException(Loc.T("Err.ItemNotFound"));

        var change = countedQuantity - product.StockQuantity;
        if (change != 0)
        {
            // A count can correct negative stock, so allow the ledger to go through whatever the sign.
            StockLedger.Apply(db, product, change, StockMovementType.StockCount, userId, notes: QueryHelpers.Clean(notes) ?? "Stock count", allowNegative: true);
            await SaveAsync(db, ct);
        }
        return product;
    }

    public async Task UpdateReorderLevelAsync(int productId, int reorderLevel, CancellationToken ct = default)
    {
        if (reorderLevel < 0) throw new BusinessRuleException(Loc.T("Err.ReorderNegative"));
        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await db.Products.FindAsync([productId], ct) ?? throw new BusinessRuleException(Loc.T("Err.ItemNotFound"));
        product.ReorderLevel = reorderLevel;
        await SaveAsync(db, ct);
    }

    public async Task<List<StockMovement>> GetMovementsAsync(int? productId = null, DateTime? from = null, DateTime? to = null, int max = 500, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.StockMovements.AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.User)
            .AsQueryable();

        if (productId is not null) query = query.Where(m => m.ProductId == productId);
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
