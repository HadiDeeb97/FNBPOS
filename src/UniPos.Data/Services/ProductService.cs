using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Text;
using Microsoft.EntityFrameworkCore;
using UniPos.Core.Localization;

namespace UniPos.Data.Services;

/// <summary>The menu: items, with the modifier groups each one offers.</summary>
public class ProductService(IDbContextFactory<PosDbContext> factory)
{
    /// <summary>Most items a list shows at once (alphabetical); a search or category narrows it.</summary>
    public const int ProductListLimit = 500;

    /// <summary>First code given to an item made without one.</summary>
    internal const int FirstCode = 101;

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
                EF.Functions.Like(p.Sku, pattern, "\\") ||
                EF.Functions.Like(p.Category!.Name, pattern, "\\") ||
                EF.Functions.Like(p.Supplier!.Name, pattern, "\\") ||
                (p.Barcode != null && p.Barcode == term));
        }

        // Ids first, then the details of just those (see SearchItemsAsync).
        var ids = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Select(p => p.Id).Take(max).ToListAsync(ct);
        return await LoadAsync(db, ids, ct);
    }

    public async Task<Product?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await LoadAsync(db, [id], ct)).FirstOrDefault();
    }

    /// <summary>Items with everything the register needs: category, supplier, and their groups with every option.</summary>
    public async Task<List<Product>> GetItemsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await LoadAsync(db, ids.Distinct().ToList(), ct);
    }

    /// <summary>The items with these ids, in this order.</summary>
    internal static async Task<List<Product>> LoadAsync(PosDbContext db, List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var found = await WithOptions(db.Products.AsNoTracking())
            .Include(p => p.Supplier)
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        return ids.Where(found.ContainsKey).Select(id => found[id]).ToList();
    }

    /// <summary>An item with its category and its groups with all their options (switched-off ones too: the caller filters).</summary>
    internal static IQueryable<Product> WithOptions(IQueryable<Product> query) =>
        query.Include(p => p.Category)
            .Include(p => p.ModifierGroups).ThenInclude(g => g.ModifierGroup).ThenInclude(g => g!.Options)
            .AsSplitQuery();

    /// <summary>Exact match on barcode or code: what a scanner or a quick code sends.</summary>
    public async Task<Product?> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        code = code.Trim();
        if (code.Length == 0) return null;

        // A 12-digit UPC is the same code as the 13-digit EAN with a leading 0, and scanners send either form.
        var other = SameCodeOtherForm(code) ?? code;

        await using var db = await factory.CreateDbContextAsync(ct);
        // "Barcode != null" lets SQL Server use the barcode index (it only holds rows that have one).
        var id = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && ((p.Barcode != null && (p.Barcode == code || p.Barcode == other)) || p.Sku == code))
            .OrderByDescending(p => p.Barcode == code || p.Sku == code) // the code exactly as scanned first
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        return id is null ? null : (await LoadAsync(db, [id.Value], ct)).FirstOrDefault();
    }

    /// <summary>"0012345678905" ↔ "012345678905": a UPC-A as EAN-13 and back; null for any other code.</summary>
    internal static string? SameCodeOtherForm(string code) => code switch
    {
        { Length: 13 } when code[0] == '0' && code.All(char.IsAsciiDigit) => code[1..],
        { Length: 12 } when code.All(char.IsAsciiDigit) => "0" + code,
        _ => null,
    };

    /// <summary>A trivial round trip to the database, to tell a slow server/PC apart from a slow search.</summary>
    public async Task<TimeSpan> PingAsync(CancellationToken ct = default)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        return watch.Elapsed;
    }

    /// <summary>Free-text lookup for the register (name, code, category, barcode): items on the menu now.</summary>
    public async Task<List<Product>> SearchItemsAsync(string text, int max = 100, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);

        // Every word must match something, in any order, so "nutella banana" narrows nicely.
        var terms = SmartSearch.Terms(text);
        if (terms.Count == 0) return [];
        foreach (var term in terms)
        {
            var pattern = SmartSearch.LikePattern(term);
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern, "\\") ||
                EF.Functions.Like(p.Category!.Name, pattern, "\\") ||
                EF.Functions.Like(p.Sku, pattern, "\\") ||
                (p.Barcode != null && p.Barcode == term));
        }

        // Best matches first: an exact code, then names starting with the first word. Ids first (sorting narrow rows
        // is quick), then the full details of just those.
        var whole = text.Trim();
        var startsWith = SmartSearch.LikePattern(terms[0])[1..];
        var ids = await query
            .OrderByDescending(p => p.Barcode == whole || p.Sku == whole)
            .ThenByDescending(p => EF.Functions.Like(p.Name, startsWith, "\\"))
            .ThenBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => p.Id)
            .Take(max)
            .ToListAsync(ct);
        return await LoadAsync(db, ids, ct);
    }

    /// <summary>
    /// Creates or updates an item and the groups it offers (replaced as given). Stock of an existing item is not touched
    /// here (use the stock screen); a new counted item may carry its opening stock in <see cref="Product.StockQuantity"/>.
    /// </summary>
    public async Task<Product> SaveAsync(Product product, int userId, CancellationToken ct = default)
    {
        Validate(product);

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Categories.AnyAsync(c => c.Id == product.CategoryId, ct))
            throw new BusinessRuleException(Loc.T("Err.ChooseValidCategory"));

        // A name already used by another item (any case, extra spaces ignored; deactivated ones too) is refused.
        var name = product.Name.Trim();
        if (await db.Products.AnyAsync(p => p.Id != product.Id && EF.Functions.Collate(p.Name, "Latin1_General_100_CI_AS") == name, ct))
            throw new BusinessRuleException(Loc.T("Err.ProductNameTaken", name));

        var groupIds = product.ModifierGroups.Select(g => g.ModifierGroupId).ToList();
        if (groupIds.Count != groupIds.Distinct().Count()) throw new BusinessRuleException(Loc.T("Err.GroupTwice"));
        if (await db.ModifierGroups.CountAsync(g => groupIds.Contains(g.Id), ct) != groupIds.Count)
            throw new BusinessRuleException(Loc.T("Err.GroupNotFound"));

        Product entity;
        var isNew = product.Id == 0;
        if (isNew)
        {
            entity = new Product { CreatedAt = DateTime.Now };
            db.Products.Add(entity);
        }
        else
        {
            entity = await db.Products.Include(p => p.ModifierGroups).FirstOrDefaultAsync(p => p.Id == product.Id, ct)
                     ?? throw new BusinessRuleException(Loc.T("Err.ProductNotFound"));
        }

        entity.Name = name;
        entity.Description = QueryHelpers.Clean(product.Description);
        entity.CategoryId = product.CategoryId;
        entity.SupplierId = product.SupplierId;
        entity.Price = Money.Round(product.Price);
        entity.Cost = Money.Round(product.Cost);
        entity.SoldOut = product.SoldOut;
        entity.TrackStock = product.TrackStock;
        entity.ReorderLevel = Math.Max(0, product.ReorderLevel);
        entity.IsActive = product.IsActive;

        var sku = QueryHelpers.Clean(product.Sku)?.ToUpperInvariant() ?? await NextCodeAsync(db, ct);
        if (await db.Products.AnyAsync(p => p.Id != product.Id && p.Sku == sku, ct))
            throw new BusinessRuleException(Loc.T("Err.SkuUsed", sku));
        entity.Sku = sku;

        var barcode = QueryHelpers.Clean(product.Barcode);
        if (barcode is not null && await db.Products.AnyAsync(p => p.Id != product.Id && p.Barcode == barcode, ct))
            throw new BusinessRuleException(Loc.T("Err.BarcodeUsed", barcode));
        entity.Barcode = barcode;

        entity.ModifierGroups.Clear();
        foreach (var (link, order) in product.ModifierGroups.Select((g, i) => (g, i)))
            entity.ModifierGroups.Add(new ProductModifierGroup { ModifierGroupId = link.ModifierGroupId, SortOrder = order });

        if (isNew && entity.TrackStock)
        {
            if (product.StockQuantity < 0) throw new BusinessRuleException(Loc.T("Err.OpeningStockNegative"));
            if (product.StockQuantity > 0)
                StockLedger.Apply(db, entity, product.StockQuantity, StockMovementType.InitialStock, userId, notes: "Opening stock");
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
            // Another PC saved the same new code or barcode a moment earlier; saving again picks a fresh one.
            throw new BusinessRuleException(Loc.T("Err.CodeTakenMeanwhile"));
        }

        return (await GetAsync(entity.Id, ct))!;
    }

    /// <summary>Marks an item sold out (or back) at once, from the register or the menu list.</summary>
    public async Task SetSoldOutAsync(int productId, bool soldOut, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await db.Products.FindAsync([productId], ct) ?? throw new BusinessRuleException(Loc.T("Err.ProductNotFound"));
        product.SoldOut = soldOut;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The next free numeric code: one more than the highest numeric code in use, from 101.</summary>
    internal static async Task<string> NextCodeAsync(PosDbContext db, CancellationToken ct)
    {
        var codes = await db.Products.Select(p => p.Sku).ToListAsync(ct);
        var highest = codes.Select(c => int.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return Math.Max(highest + 1, FirstCode).ToString();
    }

    /// <summary>Deletes an item that was never sold or ordered; otherwise deactivates it.</summary>
    public async Task<bool> DeleteAsync(int productId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return false;

        if (await HasHistoryAsync(db, productId, ct) || (product.TrackStock && product.StockQuantity != 0))
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
    /// erase that history, so such items are deactivated instead.
    /// </summary>
    private static async Task<bool> HasHistoryAsync(PosDbContext db, int productId, CancellationToken ct) =>
        await db.SaleLines.AnyAsync(l => l.ProductId == productId, ct)
        || await db.PurchaseOrderLines.AnyAsync(l => l.ProductId == productId, ct)
        || await db.StockMovements.AnyAsync(m => m.ProductId == productId && m.Type != StockMovementType.InitialStock, ct);

    private static void Validate(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Name)) throw new BusinessRuleException(Loc.T("Err.ProductNameRequired"));
        if (product.CategoryId == 0) throw new BusinessRuleException(Loc.T("Err.ChooseCategory"));
        if (product.Price < 0 || product.Cost < 0) throw new BusinessRuleException(Loc.T("Err.PriceCostNegative"));
    }
}
