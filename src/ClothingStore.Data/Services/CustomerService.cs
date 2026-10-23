using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Text;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Security;

namespace ClothingStore.Data.Services;

public class CustomerService(IDbContextFactory<PosDbContext> factory)
{
    /// <summary>Customers matching the text (name, phone, email, address), each with what they spent and how often.</summary>
    public async Task<List<Customer>> SearchAsync(
        string? text = null, bool includeInactive = false, int max = 200, int? regionId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Customers.AsNoTracking().Where(c => includeInactive || c.IsActive);
        if (regionId is { } r) query = query.Where(c => c.RegionId == r);
        if (SmartSearch.PhoneDigits(text) is { } digits)
        {
            // A phone number however it was typed or saved ("70 123 456", "70-123456").
            var pattern = SmartSearch.LikePattern(digits);
            query = query.Where(c => EF.Functions.Like(c.Phone!.Replace(" ", "").Replace("-", "").Replace(".", "").Replace("+", ""), pattern, "\\"));
        }
        else
        {
            foreach (var term in SmartSearch.Terms(text))
            {
                var pattern = SmartSearch.LikePattern(term);
                query = query.Where(c =>
                    EF.Functions.Like(c.FirstName, pattern, "\\") ||
                    EF.Functions.Like(c.LastName, pattern, "\\") ||
                    EF.Functions.Like(c.Phone!, pattern, "\\") ||
                    EF.Functions.Like(c.Email!, pattern, "\\") ||
                    EF.Functions.Like(c.Address!, pattern, "\\") ||
                    EF.Functions.Like(c.Region!.Name, pattern, "\\") ||
                    EF.Functions.Like(c.Region!.NameAr!, pattern, "\\"));
            }
        }
        return await query.OrderBy(c => c.FirstName).ThenBy(c => c.LastName).Take(max)
            .Select(c => new Customer
            {
                Id = c.Id, FirstName = c.FirstName, LastName = c.LastName, Phone = c.Phone, Email = c.Email, Birthday = c.Birthday,
                Notes = c.Notes, Address = c.Address, RegionId = c.RegionId, Region = c.Region, LoyaltyPoints = c.LoyaltyPoints,
                StoreCredit = c.StoreCredit, IsActive = c.IsActive, CreatedAt = c.CreatedAt, Version = c.Version,
                // Net of refunds, completed sales only.
                TotalSpent = (db.Sales.Where(s => s.CustomerId == c.Id && s.Status == SaleStatus.Completed).Sum(s => (decimal?)s.Total) ?? 0)
                             - (db.Returns.Where(r => r.CustomerId == c.Id).Sum(r => (decimal?)r.TotalRefund) ?? 0),
                Visits = db.Sales.Count(s => s.CustomerId == c.Id && s.Status == SaleStatus.Completed),
                LastVisit = db.Sales.Where(s => s.CustomerId == c.Id && s.Status == SaleStatus.Completed).Max(s => (DateTime?)s.CreatedAt),
            })
            .ToListAsync(ct);
    }

    public async Task<List<Region>> GetRegionsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Regions.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(ct);
    }

    /// <summary>Adds a state / governorate (or returns the existing one with that name).</summary>
    public async Task<Region> AddRegionAsync(string name, CancellationToken ct = default)
    {
        name = name.Trim();
        if (name.Length == 0) throw new BusinessRuleException(Loc.T("Err.RegionNameRequired"));
        if (name.Length > 100) name = name[..100];

        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Regions.FirstOrDefaultAsync(r => r.Name == name || r.NameAr == name, ct);
        if (existing is not null)
        {
            if (!existing.IsActive) { existing.IsActive = true; await db.SaveChangesAsync(ct); }
            return existing;
        }
        var region = new Region { Name = name, SortOrder = (await db.Regions.MaxAsync(r => (int?)r.SortOrder, ct) ?? 0) + 1 };
        db.Regions.Add(region);
        await db.SaveChangesAsync(ct);
        return region;
    }

    public async Task<Customer?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking().Include(c => c.Region).FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<Customer> SaveAsync(Customer customer, CancellationToken ct = default)
    {
        customer.FirstName = customer.FirstName.Trim();
        customer.LastName = customer.LastName.Trim();
        customer.Phone = QueryHelpers.Clean(customer.Phone);
        customer.Email = QueryHelpers.Clean(customer.Email);
        if (customer.FirstName.Length == 0) throw new BusinessRuleException(Loc.T("Err.FirstNameRequired"));
        if (customer.Email is not null && !customer.Email.Contains('@')) throw new BusinessRuleException(Loc.T("Err.EmailInvalid"));

        await using var db = await factory.CreateDbContextAsync(ct);
        if (customer.Phone is not null && await db.Customers.AnyAsync(c => c.Phone == customer.Phone && c.Id != customer.Id, ct))
            throw new BusinessRuleException(Loc.T("Err.PhoneUsed", customer.Phone));

        var entity = customer.Id == 0
            ? db.Customers.Add(new Customer { CreatedAt = DateTime.Now }).Entity
            : await db.Customers.FindAsync([customer.Id], ct) ?? throw new BusinessRuleException(Loc.T("Err.CustomerNotFound"));

        // Points and store credit only change through sales, returns and explicit adjustments.
        entity.FirstName = customer.FirstName;
        entity.LastName = customer.LastName;
        entity.Phone = customer.Phone;
        entity.Email = customer.Email;
        entity.Birthday = customer.Birthday;
        entity.Notes = QueryHelpers.Clean(customer.Notes);
        entity.Address = QueryHelpers.Clean(customer.Address);
        entity.RegionId = customer.RegionId;
        entity.IsActive = customer.IsActive;

        await SaveAsync(db, ct);
        return entity;
    }

    /// <summary>Manually add (positive) or remove (negative) store credit, e.g. goodwill gestures.</summary>
    public async Task<Customer> AdjustStoreCreditAsync(int customerId, decimal amount, int userId, CancellationToken ct = default)
    {
        amount = Money.Round(amount);
        if (amount == 0) throw new BusinessRuleException(Loc.T("Err.AmountZero"));

        await using var db = await factory.CreateDbContextAsync(ct);
        // Checked here too, not only by hiding the button: store credit is money.
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || !Permissions.Has(user.Role, Permission.AdjustStoreCredit))
            throw new BusinessRuleException(Loc.T("Err.NoPermission"));
        var customer = await db.Customers.FindAsync([customerId], ct) ?? throw new BusinessRuleException(Loc.T("Err.CustomerNotFound"));
        if (customer.StoreCredit + amount < 0)
            throw new BusinessRuleException(Loc.T("Err.CustomerCreditOnly", customer.StoreCredit));
        customer.StoreCredit += amount;
        await SaveAsync(db, ct);
        return customer;
    }

    public async Task<List<Sale>> GetPurchaseHistoryAsync(int customerId, int max = 200, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Sales.AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.User)
            .Where(s => s.CustomerId == customerId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(max)
            .ToListAsync(ct);
    }

    private static async Task SaveAsync(PosDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.CustomerChanged"));
        }
        catch (DbUpdateException ex) when (QueryHelpers.IsUniqueViolation(ex))
        {
            // Another till saved a customer with the same phone a moment earlier.
            throw new BusinessRuleException(Loc.T("Err.PhoneUsedMeanwhile"));
        }
    }
}
