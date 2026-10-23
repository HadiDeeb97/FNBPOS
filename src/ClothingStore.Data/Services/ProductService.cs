using ClothingStore.Core;
using ClothingStore.Core.Barcodes;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Text;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

public class ProductService(IDbContextFactory<PosDbContext> factory)
{
    /// <summary>Most products a list shows at once (alphabetical); a search or category narrows it.</summary>
    public const int ProductListLimit = 500;

    public async Task<List<Product>> SearchAsync(string? text = null, int? categoryId = null, bool includeInactive = false,
        CancellationToken ct = default, int max = ProductListLimit)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Products.AsNoTracking().Where(p => includeInactive || p.IsActive);

        if (categoryId is not null) query = query.Where(p => p.CategoryId == categoryId);
        foreach (var term in SmartSearch.Terms(text))
        {
            var pattern = SmartSearch.LikePattern(term);
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern, "\\") ||
                EF.Functions.Like(p.Brand!, pattern, "\\") ||
                EF.Functions.Like(p.StyleCode!, pattern, "\\") ||
                EF.Functions.Like(p.Category!.Name, pattern, "\\") ||
                EF.Functions.Like(p.Supplier!.Name, pattern, "\\") ||
                p.Variants.Any(v => EF.Functions.Like(v.Sku, pattern, "\\") || EF.Functions.Like(v.Color, pattern, "\\") ||
                                    v.Size == term || (v.Barcode != null && v.Barcode == term)));
        }

        // Ids first, then the details of just those (with every variant): see SearchVariantsAsync.
        var ids = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Select(p => p.Id).Take(max).ToListAsync(ct);
        if (ids.Count == 0) return [];
        var found = await db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.Variants)
            .AsSplitQuery()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        return ids.Where(found.ContainsKey).Select(id => found[id]).ToList();
    }

    public async Task<Product?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    /// <summary>Exact match on barcode or SKU — what a scanner sends.</summary>
    public async Task<ProductVariant?> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        code = code.Trim();
        if (code.Length == 0) return null;

        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ProductVariants.AsNoTracking()
            .Include(v => v.Product).ThenInclude(p => p!.Category)
            // "Barcode != null" lets SQL Server use the barcode index (it only holds rows that have one), so a scan is
            // a quick lookup instead of reading every variant.
            .Where(v => v.IsActive && v.Product!.IsActive && ((v.Barcode != null && v.Barcode == code) || v.Sku == code))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>A trivial round trip to the database, to tell a slow server/PC apart from a slow search.</summary>
    public async Task<TimeSpan> PingAsync(CancellationToken ct = default)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        return watch.Elapsed;
    }

    public async Task<List<ProductVariant>> GetVariantsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ProductVariants.AsNoTracking()
            .Include(v => v.Product).ThenInclude(p => p!.Category)
            .Where(v => idList.Contains(v.Id))
            .ToListAsync(ct);
    }

    /// <summary>Free-text variant lookup for the register (name, brand, SKU, colour, size).</summary>
    public async Task<List<ProductVariant>> SearchVariantsAsync(string text, int max = 100, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.ProductVariants.AsNoTracking()
            .Where(v => v.IsActive && v.Product!.IsActive);

        // Every word must match something, in any order, so "blue oxford m" narrows nicely.
        var terms = SmartSearch.Terms(text);
        if (terms.Count == 0) return [];
        foreach (var term in terms)
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
                (v.Barcode != null && v.Barcode == term));
        }

        // Best matches first: an exact code, then names starting with the first word.
        var whole = text.Trim();
        var startsWith = SmartSearch.LikePattern(terms[0])[1..];
        // First only the ids of the best matches (sorting narrow rows is quick), then the full details of just those:
        // loading products and categories for every match before keeping the first 100 made searches several times slower.
        var ids = await query
            .OrderByDescending(v => v.Barcode == whole || v.Sku == whole)
            .ThenByDescending(v => EF.Functions.Like(v.Product!.Name, startsWith, "\\"))
            .ThenBy(v => v.Product!.Name).ThenBy(v => v.Color).ThenBy(v => v.Id)
            .Select(v => v.Id)
            .Take(max)
            .ToListAsync(ct);
        if (ids.Count == 0) return [];
        var found = await db.ProductVariants.AsNoTracking()
            .Include(v => v.Product).ThenInclude(p => p!.Category)
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);
        return ids.Where(found.ContainsKey).Select(id => found[id]).ToList();
    }

    public async Task<List<string>> GenerateBarcodesAsync(int count, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var next = await NextBarcodeSequenceAsync(db, ct);
        return Enumerable.Range(0, count).Select(i => Ean13.CreateInStore(next + i)).ToList();
    }

    private static async Task<long> NextBarcodeSequenceAsync(PosDbContext db, CancellationToken ct)
    {
        var existing = await db.ProductVariants
            .Where(v => v.Barcode != null && EF.Functions.Like(v.Barcode, Ean13.InStorePrefix + "%"))
            .Select(v => v.Barcode!)
            .ToListAsync(ct);

        var max = existing
            .Where(Ean13.IsValid)
            .Select(b => long.Parse(b.Substring(2, 10)))
            .DefaultIfEmpty(0)
            .Max();
        return max + 1;
    }

    /// <summary>
    /// Creates or updates a product with its variants. Stock of existing variants is not touched here
    /// (use the inventory screen); new variants may carry an opening stock quantity.
    /// </summary>
    public async Task<Product> SaveAsync(Product product, int userId, CancellationToken ct = default)
    {
        Validate(product);

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Categories.AnyAsync(c => c.Id == product.CategoryId, ct))
            throw new BusinessRuleException(Loc.T("Err.ChooseValidCategory"));

        Product entity;
        if (product.Id == 0)
        {
            entity = new Product { CreatedAt = DateTime.Now };
            db.Products.Add(entity);
        }
        else
        {
            entity = await db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == product.Id, ct)
                     ?? throw new BusinessRuleException(Loc.T("Err.ProductNotFound"));
        }

        entity.Name = product.Name.Trim();
        entity.Description = QueryHelpers.Clean(product.Description);
        entity.Brand = QueryHelpers.Clean(product.Brand);
        entity.StyleCode = QueryHelpers.Clean(product.StyleCode)?.ToUpperInvariant()
                           ?? SkuGenerator.StyleCodeFromName(entity.Name);
        entity.Gender = product.Gender;
        entity.Season = QueryHelpers.Clean(product.Season);
        entity.Material = QueryHelpers.Clean(product.Material);
        entity.CategoryId = product.CategoryId;
        entity.SupplierId = product.SupplierId;
        entity.Price = Money.Round(product.Price);
        entity.Cost = Money.Round(product.Cost);
        entity.IsActive = product.IsActive;

        var ownIds = entity.Variants.Where(v => v.Id != 0).Select(v => v.Id).ToHashSet();
        var takenSkus = (await db.ProductVariants.Where(v => !ownIds.Contains(v.Id)).Select(v => v.Sku).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var takenBarcodes = (await db.ProductVariants.Where(v => v.Barcode != null && !ownIds.Contains(v.Id)).Select(v => v.Barcode!).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextBarcode = await NextBarcodeSequenceAsync(db, ct);

        // Remove variants the user deleted (or retire them if they have history).
        var incomingIds = product.Variants.Where(v => v.Id != 0).Select(v => v.Id).ToHashSet();
        foreach (var removed in entity.Variants.Where(v => v.Id != 0 && !incomingIds.Contains(v.Id)).ToList())
        {
            if (await HasHistoryAsync(db, [removed.Id], ct) || removed.StockQuantity != 0)
            {
                // Retired, not deleted: it keeps its SKU and barcode, so a new variant can't take them.
                removed.IsActive = false;
                takenSkus.Add(removed.Sku);
                if (removed.Barcode is not null) takenBarcodes.Add(removed.Barcode);
            }
            else entity.Variants.Remove(removed);
        }

        foreach (var incoming in product.Variants)
        {
            var isNew = incoming.Id == 0;
            var variant = isNew
                ? new ProductVariant()
                : entity.Variants.FirstOrDefault(v => v.Id == incoming.Id)
                  ?? throw new BusinessRuleException(Loc.T("Err.VariantNotOfProduct"));

            variant.Size = incoming.Size.Trim();
            variant.Color = incoming.Color.Trim();
            variant.PriceOverride = incoming.PriceOverride is { } p ? Money.Round(p) : null;
            variant.CostOverride = incoming.CostOverride is { } c ? Money.Round(c) : null;
            variant.ReorderLevel = Math.Max(0, incoming.ReorderLevel);
            variant.IsActive = incoming.IsActive;

            var sku = QueryHelpers.Clean(incoming.Sku)?.ToUpperInvariant();
            if (sku is null)
                sku = SkuGenerator.MakeUnique(SkuGenerator.Build(entity.StyleCode, variant.Size, variant.Color), takenSkus);
            else if (takenSkus.Contains(sku))
                throw new BusinessRuleException(Loc.T("Err.SkuUsed", sku));
            takenSkus.Add(sku);
            variant.Sku = sku;

            var barcode = QueryHelpers.Clean(incoming.Barcode) ?? Ean13.CreateInStore(nextBarcode++);
            if (!takenBarcodes.Add(barcode))
                throw new BusinessRuleException(Loc.T("Err.BarcodeUsed", barcode));
            variant.Barcode = barcode;

            if (isNew)
            {
                entity.Variants.Add(variant);
                variant.Product = entity;
                if (incoming.StockQuantity < 0) throw new BusinessRuleException(Loc.T("Err.OpeningStockNegative"));
                if (incoming.StockQuantity > 0)
                    StockLedger.Apply(db, variant, incoming.StockQuantity, StockMovementType.InitialStock, userId, notes: "Opening stock");
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.ProductChanged"));
        }
        catch (DbUpdateException ex) when (QueryHelpers.IsUniqueViolation(ex))
        {
            // Another PC saved the same new SKU or barcode a moment earlier; saving again picks fresh ones.
            throw new BusinessRuleException(Loc.T("Err.CodeTakenMeanwhile"));
        }

        return (await GetAsync(entity.Id, ct))!;
    }

    /// <summary>Deletes a product that was never sold or ordered; otherwise deactivates it.</summary>
    public async Task<bool> DeleteAsync(int productId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return false;

        var variantIds = product.Variants.Select(v => v.Id).ToList();
        if (await HasHistoryAsync(db, variantIds, ct) || product.Variants.Any(v => v.StockQuantity != 0))
        {
            product.IsActive = false;
            await db.SaveChangesAsync(ct);
            return false;
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Sold, ordered, or stock moved other than the opening stock (adjustments, write-offs, counts): deleting would
    /// erase that history, so such variants are deactivated instead.
    /// </summary>
    private static async Task<bool> HasHistoryAsync(PosDbContext db, List<int> variantIds, CancellationToken ct) =>
        await db.SaleLines.AnyAsync(l => variantIds.Contains(l.ProductVariantId), ct)
        || await db.PurchaseOrderLines.AnyAsync(l => variantIds.Contains(l.ProductVariantId), ct)
        || await db.StockMovements.AnyAsync(m => variantIds.Contains(m.ProductVariantId) && m.Type != StockMovementType.InitialStock, ct);

    private static void Validate(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Name)) throw new BusinessRuleException(Loc.T("Err.ProductNameRequired"));
        if (product.CategoryId == 0) throw new BusinessRuleException(Loc.T("Err.ChooseCategory"));
        if (product.Price < 0 || product.Cost < 0) throw new BusinessRuleException(Loc.T("Err.PriceCostNegative"));
        if (product.Variants.Count == 0) throw new BusinessRuleException(Loc.T("Err.NeedVariant"));
        if (product.Variants.Any(v => v.PriceOverride < 0 || v.CostOverride < 0))
            throw new BusinessRuleException(Loc.T("Err.VariantPriceNegative"));

        var duplicate = product.Variants
            .GroupBy(v => (Size: v.Size.Trim().ToUpperInvariant(), Color: v.Color.Trim().ToUpperInvariant()))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new BusinessRuleException(Loc.T("Err.VariantTwice", ProductVariant.DescribeVariant(duplicate.Key.Size, duplicate.Key.Color)));

        var dupSku = product.Variants
            .Select(v => v.Sku?.Trim().ToUpperInvariant())
            .Where(s => !string.IsNullOrEmpty(s))
            .GroupBy(s => s).FirstOrDefault(g => g.Count() > 1);
        if (dupSku is not null) throw new BusinessRuleException(Loc.T("Err.SkuTwice", dupSku.Key));
    }
}
