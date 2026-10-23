using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Pricing;
using ClothingStore.Core.Security;
using ClothingStore.Core.Text;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Data.Services;

/// <summary>An online order paid (partly) through the delivery company, with what it still owes for it.</summary>
public sealed record DeliveryRow
{
    public required int SaleId { get; init; }
    public required string ReceiptNumber { get; init; }
    public required DateTime CreatedAt { get; init; }
    public SalesChannel Channel { get; init; }
    public string? Courier { get; init; }

    /// <summary>The delivery company's invoice / tracking number.</summary>
    public string? DeliveryReference { get; init; }
    public string? Customer { get; init; }
    public string? Notes { get; init; }
    public decimal Total { get; init; }

    /// <summary>Paid with <see cref="PaymentMethod.Delivery"/>, less refunds taken off it.</summary>
    public decimal Owed { get; init; }

    public int? SettlementId { get; init; }
    public DateTime? SettledAt { get; init; }
    public SettlementMethod? SettledWith { get; init; }

    public bool IsPaid => SettlementId is not null;
}

public sealed record CourierBalance(string Courier, int Orders, decimal Owed);

public sealed record SettleDeliveriesRequest
{
    public required IReadOnlyList<int> SaleIds { get; init; }
    public required int UserId { get; init; }
    public int? ShiftId { get; init; }
    public SettlementMethod Method { get; init; }

    /// <summary>Dollars received (Cash, Transfer) or pounds received (CashLbp). Null = exactly what was owed.</summary>
    public decimal? Received { get; init; }

    public decimal ExchangeRate { get; init; }
    public string? Reference { get; init; }
}

/// <summary>
/// Money the delivery companies collect for online orders and pay the store later. Until it arrives it is owed, not
/// cash in the drawer; recording it (cash into the open drawer, or a transfer) settles the orders it covers.
/// </summary>
public class DeliveryService(IDbContextFactory<PosDbContext> factory)
{
    /// <summary>Orders paid through a delivery company. <paramref name="paid"/>: null = all, false = still owed, true = paid.</summary>
    public async Task<List<DeliveryRow>> GetAsync(bool? paid, string? courier = null, string? text = null, int take = 1000, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = DeliverySales(db);
        if (paid is true) query = query.Where(s => s.DeliverySettlementId != null);
        else if (paid is false) query = query.Where(s => s.DeliverySettlementId == null);
        if (!string.IsNullOrWhiteSpace(courier)) query = query.Where(s => s.Courier == courier);
        if (SmartSearch.PhoneDigits(text) is { } digits)
        {
            var pattern = SmartSearch.LikePattern(digits);
            query = query.Where(s =>
                EF.Functions.Like(s.Customer!.Phone!.Replace(" ", "").Replace("-", "").Replace(".", "").Replace("+", ""), pattern, "\\") ||
                EF.Functions.Like(s.ReceiptNumber, pattern, "\\") ||
                EF.Functions.Like(s.DeliveryReference!, pattern, "\\"));
        }
        else
        {
            foreach (var term in SmartSearch.Terms(text))
            {
                var pattern = SmartSearch.LikePattern(term);
                query = query.Where(s =>
                    EF.Functions.Like(s.ReceiptNumber, pattern, "\\") ||
                    EF.Functions.Like(s.DeliveryReference!, pattern, "\\") ||
                    EF.Functions.Like(s.Courier!, pattern, "\\") ||
                    EF.Functions.Like(s.Notes!, pattern, "\\") ||
                    EF.Functions.Like(s.Customer!.FirstName, pattern, "\\") ||
                    EF.Functions.Like(s.Customer!.LastName, pattern, "\\") ||
                    EF.Functions.Like(s.Customer!.Phone!, pattern, "\\"));
            }
        }

        var rows = await query
            .OrderBy(s => s.DeliverySettlementId == null ? 0 : 1).ThenByDescending(s => s.CreatedAt)
            .Take(take)
            .Select(Row(db))
            .ToListAsync(ct);
        // "Still owed" leaves out orders fully refunded meanwhile (they owe nothing), as the balances do.
        return paid is false ? rows.Where(r => r.Owed > 0).ToList() : rows;
    }

    /// <summary>
    /// The order with this delivery invoice / tracking number (or receipt number), paid or not, for scanning the
    /// company's slips when it pays. Null when nothing matches.
    /// </summary>
    public async Task<DeliveryRow?> FindAsync(string code, CancellationToken ct = default)
    {
        code = code.Trim();
        if (code.Length == 0) return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        return await DeliverySales(db)
            .Where(s => s.DeliveryReference == code || s.ReceiptNumber == code)
            .OrderBy(s => s.DeliverySettlementId == null ? 0 : 1).ThenByDescending(s => s.CreatedAt)
            .Select(Row(db))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>What each delivery company still owes.</summary>
    public async Task<List<CourierBalance>> GetBalancesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await DeliverySales(db).Where(s => s.DeliverySettlementId == null).Select(Row(db)).ToListAsync(ct);
        return rows.Where(r => r.Owed > 0)
            .GroupBy(r => r.Courier ?? "", StringComparer.OrdinalIgnoreCase) // same as the database, which ignores case
            .Select(g => new CourierBalance(g.Key, g.Count(), g.Sum(r => r.Owed)))
            .OrderByDescending(b => b.Owed)
            .ToList();
    }

    /// <summary>
    /// Names for picking a delivery company / driver: the active partners first, then names used on older orders that
    /// aren't saved as partners.
    /// </summary>
    public async Task<List<string>> GetCouriersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var partners = await db.DeliveryPartners.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(ct);
        var known = await db.DeliveryPartners.AsNoTracking().Select(p => p.Name).ToListAsync(ct);
        var used = await db.Sales.AsNoTracking()
            .Where(s => s.Courier != null && s.Courier != "")
            .GroupBy(s => s.Courier!)
            .OrderByDescending(g => g.Max(s => s.CreatedAt))
            .Select(g => g.Key)
            .Take(30)
            .ToListAsync(ct);
        var seen = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        return [.. partners, .. used.Where(seen.Add)];
    }

    // ---- Delivery companies and drivers -----------------------------------------------------

    public async Task<List<DeliveryPartner>> GetPartnersAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.DeliveryPartners.AsNoTracking()
            .Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.Kind).ThenBy(p => p.Name)
            .ToListAsync(ct);
    }

    /// <summary>Adds or updates a partner. Renaming one also renames it on its orders and settlements.</summary>
    public async Task<DeliveryPartner> SavePartnerAsync(DeliveryPartner partner, CancellationToken ct = default)
    {
        var name = partner.Name.Trim();
        if (name.Length == 0) throw new BusinessRuleException(Loc.T("Err.PartnerNameRequired"));
        if (partner.DefaultFee < 0) throw new BusinessRuleException(Loc.T("Err.DeliveryFeeNegative"));

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.DeliveryPartners.AnyAsync(p => p.Name == name && p.Id != partner.Id, ct))
            throw new BusinessRuleException(Loc.T("Err.PartnerExists", name));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entity = partner.Id == 0
            ? db.DeliveryPartners.Add(new DeliveryPartner()).Entity
            : await db.DeliveryPartners.FindAsync([partner.Id], ct) ?? throw new BusinessRuleException(Loc.T("Err.PartnerNotFound"));

        var oldName = entity.Name;
        entity.Name = name;
        entity.Kind = partner.Kind;
        entity.ContactName = QueryHelpers.Clean(partner.ContactName);
        entity.Phone = QueryHelpers.Clean(partner.Phone);
        entity.Phone2 = QueryHelpers.Clean(partner.Phone2);
        entity.Address = QueryHelpers.Clean(partner.Address);
        entity.DefaultFee = partner.DefaultFee is { } fee ? Money.Round(fee) : null;
        entity.Notes = QueryHelpers.Clean(partner.Notes);
        entity.IsActive = partner.IsActive;
        await db.SaveChangesAsync(ct);

        if (partner.Id != 0 && !string.Equals(oldName, name, StringComparison.Ordinal))
        {
            // Balances and filters group by name, so the orders follow the new one.
            await db.Sales.Where(s => s.Courier == oldName).ExecuteUpdateAsync(u => u.SetProperty(s => s.Courier, name), ct);
            await db.DeliverySettlements.Where(s => s.Courier == oldName).ExecuteUpdateAsync(u => u.SetProperty(s => s.Courier, name), ct);
        }
        await tx.CommitAsync(ct);
        return entity;
    }

    /// <summary>Deletes a partner never used on an order; one with orders is deactivated instead (returns false).</summary>
    public async Task<bool> DeletePartnerAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.DeliveryPartners.FindAsync([id], ct);
        if (entity is null) return false;
        if (await db.Sales.AnyAsync(s => s.Courier == entity.Name, ct))
        {
            entity.IsActive = false;
            await db.SaveChangesAsync(ct);
            return false;
        }
        db.DeliveryPartners.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Records money from a delivery company for the chosen orders and marks them paid.</summary>
    public async Task<DeliverySettlement> SettleAsync(SettleDeliveriesRequest request, CancellationToken ct = default)
    {
        var ids = request.SaleIds.Distinct().ToList();
        if (ids.Count == 0) throw new BusinessRuleException(Loc.T("Err.SettleNothing"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? new StoreSettings();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId && u.IsActive, ct)
                   ?? throw new BusinessRuleException(Loc.T("Err.CashierInactive"));
        // A transfer brings no cash into the drawer, so a cashier marking courier money "transferred" could keep cash
        // handed over at the counter without the drawer showing it: managers only.
        if (request.Method == SettlementMethod.Transfer && !Permissions.Has(user.Role, Permission.SettleByTransfer))
            throw new BusinessRuleException(Loc.T("Err.TransferNeedsManager"));

        // Loaded (and checked) first, so the save is refused if another till settles or voids one of them meanwhile.
        var sales = await db.Sales.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        if (sales.Count != ids.Count || sales.Any(s => s.Status != SaleStatus.Completed || s.DeliverySettlementId is not null))
            throw new BusinessRuleException(Loc.T("Err.SettleAlreadyPaid"));
        var rows = await DeliverySales(db).Where(s => ids.Contains(s.Id)).Select(Row(db)).ToListAsync(ct);
        if (rows.Count != ids.Count || rows.Any(r => r.IsPaid)) throw new BusinessRuleException(Loc.T("Err.SettleAlreadyPaid"));

        var expected = rows.Sum(r => r.Owed);
        var settlement = new DeliverySettlement
        {
            CreatedAt = DateTime.Now,
            UserId = request.UserId,
            Method = request.Method,
            Expected = expected,
            Reference = QueryHelpers.Clean(request.Reference),
            Courier = rows.Select(r => r.Courier).Distinct().Count() == 1 ? rows[0].Courier : null,
        };

        switch (request.Method)
        {
            case SettlementMethod.Cash:
                settlement.Received = Money.Round(request.Received ?? expected);
                break;
            case SettlementMethod.CashLbp:
                SalesService.EnsureRate(settings, request.ExchangeRate);
                settlement.ExchangeRate = settings.LbpRate;
                settlement.ReceivedLbp = Math.Round(request.Received ?? Lbp.ToPay(expected, settings.LbpRate, settings.LbpRounding), 0, MidpointRounding.AwayFromZero);
                settlement.Received = Money.Round(settlement.ReceivedLbp / settings.LbpRate);
                break;
            default:
                settlement.Received = Money.Round(request.Received ?? expected);
                break;
        }
        if (settlement.Received < 0 || settlement.ReceivedLbp < 0) throw new BusinessRuleException(Loc.T("Err.PaymentNegative"));

        if (request.Method != SettlementMethod.Transfer)
        {
            // Cash goes into a drawer, so it must be counted at the end of a shift.
            var shift = request.ShiftId is { } shiftId ? await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct) : null;
            if (shift is not { Status: ShiftStatus.Open }) throw new BusinessRuleException(Loc.T("Err.SettleCashNeedsShift"));
            db.Entry(shift).Property(s => s.Status).IsModified = true; // refused if the drawer is closed meanwhile
            settlement.ShiftId = shift.Id;
        }

        foreach (var sale in sales) sale.DeliverySettlement = settlement;
        db.DeliverySettlements.Add(settlement);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another till settled (or voided) one of these orders after they were loaded.
            throw new BusinessRuleException(Loc.T("Err.SettleAlreadyPaid"));
        }
        return settlement;
    }

    // ---- Internals --------------------------------------------------------------------------

    /// <summary>Completed sales with a delivery-company payment.</summary>
    private static IQueryable<Sale> DeliverySales(PosDbContext db) =>
        db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.Payments.Any(p => p.Method == PaymentMethod.Delivery));

    private static System.Linq.Expressions.Expression<Func<Sale, DeliveryRow>> Row(PosDbContext db) => s => new DeliveryRow
    {
        SaleId = s.Id,
        ReceiptNumber = s.ReceiptNumber,
        CreatedAt = s.CreatedAt,
        Channel = s.Channel,
        Courier = s.Courier,
        DeliveryReference = s.DeliveryReference,
        Customer = s.Customer != null ? s.Customer.FirstName + " " + s.Customer.LastName : null,
        Notes = s.Notes,
        Total = s.Total,
        Owed = s.Payments.Where(p => p.Method == PaymentMethod.Delivery).Sum(p => p.Amount)
               - (db.ReturnRefunds.Where(r => r.SaleReturn!.SaleId == s.Id && r.Method == RefundMethod.Delivery).Sum(r => (decimal?)r.Amount) ?? 0),
        SettlementId = s.DeliverySettlementId,
        SettledAt = s.DeliverySettlement != null ? s.DeliverySettlement.CreatedAt : null,
        SettledWith = s.DeliverySettlement != null ? s.DeliverySettlement.Method : null,
    };
}
