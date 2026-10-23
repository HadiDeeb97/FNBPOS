using ClothingStore.Core;
using ClothingStore.Core.Entities;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

public sealed record ShiftSummary
{
    public required int ShiftId { get; init; }

    /// <summary>Who opened the drawer.</summary>
    public required string Cashier { get; init; }

    /// <summary>The PC (till) the drawer belongs to.</summary>
    public string? Till { get; init; }

    /// <summary>Who counted and closed it, when that wasn't the opener.</summary>
    public string? ClosedBy { get; init; }

    /// <summary>Cashiers the drawer was handed over to, in order.</summary>
    public IReadOnlyList<HandoverRow> Handovers { get; init; } = [];
    public required DateTime OpenedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public decimal OpeningFloat { get; init; }
    public int SalesCount { get; init; }
    public int VoidedCount { get; init; }
    public int ItemsSold { get; init; }
    public decimal GrossSales { get; init; }
    public decimal Discounts { get; init; }
    public decimal Tax { get; init; }
    public decimal TotalSales { get; init; }
    public IReadOnlyDictionary<PaymentMethod, decimal> Payments { get; init; } = new Dictionary<PaymentMethod, decimal>();
    public int ReturnsCount { get; init; }
    public IReadOnlyDictionary<RefundMethod, decimal> Refunds { get; init; } = new Dictionary<RefundMethod, decimal>();
    public decimal TotalRefunds { get; init; }
    public decimal PayIns { get; init; }
    public decimal PayOuts { get; init; }

    /// <summary>Drawer opened with the "Open drawer" button, without a sale.</summary>
    public int NoSaleOpenings { get; init; }
    public decimal ExpectedCash { get; init; }
    public decimal? CountedCash { get; init; }
    public decimal? Variance => CountedCash - ExpectedCash;
    public IReadOnlyList<CashMovement> CashMovements { get; init; } = [];

    /// <summary>Dollars the sales added to the drawer (received minus change).</summary>
    public decimal CashSales { get; init; }
    public decimal CashRefunds => Refunds.GetValueOrDefault(RefundMethod.Cash);

    // Lebanese pounds in the drawer, counted separately from dollars.
    public decimal OpeningFloatLbp { get; init; }
    public decimal CashSalesLbp { get; init; }
    public decimal CashRefundsLbp { get; init; }
    public decimal PayInsLbp { get; init; }
    public decimal PayOutsLbp { get; init; }
    public decimal ExpectedCashLbp { get; init; }
    public decimal? CountedCashLbp { get; init; }
    public decimal? VarianceLbp => CountedCashLbp - ExpectedCashLbp;

    /// <summary>Money from delivery companies put into this drawer (dollars, and pounds).</summary>
    public decimal DeliveryCash { get; init; }
    public decimal DeliveryCashLbp { get; init; }

    /// <summary>Whether pounds went through this drawer (or LBP is switched on), so LBP lines are worth showing.</summary>
    public bool ShowLbp { get; init; }
}

public sealed record HandoverRow(DateTime At, string From, string To);

/// <summary>The drawer of a till for a user: none open, theirs, or another cashier's still open there.</summary>
public sealed record TillShift(Shift? Shift, bool IsMine)
{
    public bool IsOtherCashiers => Shift is not null && !IsMine;
}

public class ShiftService(IDbContextFactory<PosDbContext> factory)
{
    /// <summary>
    /// The open drawer of this till, and whether <paramref name="userId"/> runs it. An open shift of this user from
    /// before tills were tracked is attached to this till.
    /// </summary>
    public async Task<TillShift> GetTillShiftAsync(int userId, string tillId, string? tillName = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var open = await OpenShifts(db).FirstOrDefaultAsync(s => s.TillId == tillId, ct);
        if (open is null)
        {
            var legacy = await db.Shifts.FirstOrDefaultAsync(
                s => s.Status == ShiftStatus.Open && s.TillId == null && (s.CurrentUserId ?? s.UserId) == userId, ct);
            if (legacy is null) return new TillShift(null, false);
            legacy.TillId = tillId;
            legacy.TillName = tillName;
            await db.SaveChangesAsync(ct);
            return new TillShift(await OpenShifts(db).FirstAsync(s => s.Id == legacy.Id, ct), true);
        }
        return new TillShift(open, open.RunningUserId == userId);
    }

    /// <summary>Every open drawer, on every till (for managers).</summary>
    public async Task<List<Shift>> GetOpenShiftsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await OpenShifts(db).OrderBy(s => s.OpenedAt).ToListAsync(ct);
    }

    /// <summary>
    /// <paramref name="userId"/> continues the open drawer without counting it (a hand-over). Their sales go into it from
    /// now on; the hand-over is recorded and printed on the Z report.
    /// </summary>
    public async Task<Shift> TakeOverAsync(int shiftId, int userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct) ?? throw new BusinessRuleException(Loc.T("Err.ShiftNotFound"));
        if (shift.Status != ShiftStatus.Open) throw new BusinessRuleException(Loc.T("Err.ShiftAlreadyClosed"));
        if (shift.RunningUserId != userId)
        {
            await EnsureNoOtherDrawerAsync(db, userId, shiftId, ct);
            db.ShiftHandovers.Add(new ShiftHandover { ShiftId = shift.Id, FromUserId = shift.RunningUserId, ToUserId = userId, At = DateTime.Now });
            shift.CurrentUserId = userId;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(Loc.T("Err.ShiftChanged"));
            }
        }
        return await OpenShifts(db).FirstAsync(s => s.Id == shiftId, ct);
    }

    private static IQueryable<Shift> OpenShifts(PosDbContext db) =>
        db.Shifts.AsNoTracking().Include(s => s.User).Include(s => s.CurrentUser).Where(s => s.Status == ShiftStatus.Open);

    /// <summary>A cashier runs one drawer at a time.</summary>
    private static async Task EnsureNoOtherDrawerAsync(PosDbContext db, int userId, int? exceptShiftId, CancellationToken ct)
    {
        var other = await db.Shifts.AsNoTracking()
            .Where(s => s.Status == ShiftStatus.Open && (s.CurrentUserId ?? s.UserId) == userId && s.Id != exceptShiftId)
            .Select(s => new { s.TillName })
            .FirstOrDefaultAsync(ct);
        if (other is not null)
            throw new BusinessRuleException(other.TillName is { Length: > 0 } till
                ? Loc.T("Err.ShiftOpenOnTill", till)
                : Loc.T("Err.ShiftAlreadyOpen"));
    }

    public async Task<Shift?> GetOpenShiftAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await OpenShifts(db).FirstOrDefaultAsync(s => (s.CurrentUserId ?? s.UserId) == userId, ct);
    }

    /// <summary>
    /// Opens a drawer on <paramref name="tillId"/> (this PC). Refused while another drawer is open on the same till:
    /// continue it (<see cref="TakeOverAsync"/>) or count and close it first.
    /// </summary>
    public async Task<Shift> OpenShiftAsync(
        int userId, decimal openingFloat, decimal openingFloatLbp = 0, string? tillId = null, string? tillName = null, CancellationToken ct = default)
    {
        if (openingFloat < 0 || openingFloatLbp < 0) throw new BusinessRuleException(Loc.T("Err.FloatNegative"));
        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureNoOtherDrawerAsync(db, userId, null, ct);
        if (tillId is not null)
        {
            var open = await OpenShifts(db).FirstOrDefaultAsync(s => s.TillId == tillId, ct);
            if (open is not null)
                throw new BusinessRuleException(Loc.T("Err.TillDrawerOpen", (open.CurrentUser ?? open.User)?.FullName ?? "?"));
        }

        var shift = new Shift
        {
            UserId = userId,
            CurrentUserId = userId,
            TillId = tillId,
            TillName = tillName,
            OpeningFloat = Money.Round(openingFloat),
            OpeningFloatLbp = RoundLbp(openingFloatLbp),
            OpenedAt = DateTime.Now,
            Status = ShiftStatus.Open,
        };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync(ct);
        return shift;
    }

    /// <summary>Counts an "Open drawer" (no sale) on the open shift; false when the shift isn't open.</summary>
    public async Task<bool> RecordNoSaleOpeningAsync(int shiftId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Shifts.Where(s => s.Id == shiftId && s.Status == ShiftStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NoSaleOpenings, x => x.NoSaleOpenings + 1), ct) == 1;
    }

    public async Task<CashMovement> AddCashMovementAsync(
        int shiftId, CashMovementType type, decimal amount, string reason, int userId, CashCurrency currency = CashCurrency.Usd,
        CancellationToken ct = default)
    {
        amount = currency == CashCurrency.Lbp ? RoundLbp(amount) : Money.Round(amount);
        if (amount <= 0) throw new BusinessRuleException(Loc.T("Err.AmountPositive"));
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException(Loc.T("Err.EnterReason"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var shift = await db.Shifts.FindAsync([shiftId], ct) ?? throw new BusinessRuleException(Loc.T("Err.ShiftNotFound"));
        if (shift.Status != ShiftStatus.Open) throw new BusinessRuleException(Loc.T("Err.ShiftClosed"));
        db.Entry(shift).Property(s => s.Status).IsModified = true; // refused if the drawer is closed meanwhile

        var movement = new CashMovement
        {
            ShiftId = shiftId, Type = type, Amount = amount, Currency = currency, Reason = reason.Trim(), UserId = userId, CreatedAt = DateTime.Now,
        };
        db.CashMovements.Add(movement);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.ShiftClosed"));
        }
        return movement;
    }

    public async Task<ShiftSummary> GetSummaryAsync(int shiftId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await BuildSummaryAsync(db, shiftId, ct);
    }

    public async Task<ShiftSummary> CloseShiftAsync(
        int shiftId, decimal countedCash, string? notes, decimal? countedCashLbp = null, int? closedByUserId = null, CancellationToken ct = default)
    {
        if (countedCash < 0 || countedCashLbp < 0) throw new BusinessRuleException(Loc.T("Err.CountedCashNegative"));
        await using var db = await factory.CreateDbContextAsync(ct);
        var shift = await db.Shifts.FindAsync([shiftId], ct) ?? throw new BusinessRuleException(Loc.T("Err.ShiftNotFound"));
        if (shift.Status != ShiftStatus.Open) throw new BusinessRuleException(Loc.T("Err.ShiftAlreadyClosed"));

        var summary = await BuildSummaryAsync(db, shiftId, ct);
        shift.ExpectedCash = summary.ExpectedCash;
        shift.CountedCash = Money.Round(countedCash);
        shift.ExpectedCashLbp = summary.ExpectedCashLbp;
        // Nothing counted in pounds means none were expected or the drawer has none.
        shift.CountedCashLbp = countedCashLbp is { } lbp ? RoundLbp(lbp) : summary.ShowLbp ? 0 : null;
        shift.Notes = QueryHelpers.Clean(notes);
        shift.ClosedAt = DateTime.Now;
        shift.ClosedByUserId = closedByUserId;
        shift.Status = ShiftStatus.Closed;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.ShiftChanged")); // closed or taken over at the same moment elsewhere
        }

        return await BuildSummaryAsync(db, shiftId, ct);
    }

    public async Task<List<Shift>> GetShiftsAsync(DateTime from, DateTime to, int? userId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Shifts.AsNoTracking().Include(s => s.User).Include(s => s.CurrentUser)
            .Where(s => s.OpenedAt >= from && s.OpenedAt < to || s.Status == ShiftStatus.Open);
        if (userId is not null)
            query = query.Where(s => s.UserId == userId || s.CurrentUserId == userId || s.Handovers.Any(h => h.ToUserId == userId));
        return await query.OrderByDescending(s => s.OpenedAt).ToListAsync(ct);
    }

    private static async Task<ShiftSummary> BuildSummaryAsync(PosDbContext db, int shiftId, CancellationToken ct)
    {
        var shift = await db.Shifts.AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.ClosedBy)
            .Include(s => s.CashMovements)
            .Include(s => s.Handovers).ThenInclude(h => h.FromUser)
            .Include(s => s.Handovers).ThenInclude(h => h.ToUser)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == shiftId, ct)
            ?? throw new BusinessRuleException(Loc.T("Err.ShiftNotFound"));

        var sales = await db.Sales.AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .AsSplitQuery()
            .Where(s => s.ShiftId == shiftId)
            .ToListAsync(ct);
        var completed = sales.Where(s => s.Status == SaleStatus.Completed).ToList();

        var returns = await db.Returns.AsNoTracking().Include(r => r.Refunds).Where(r => r.ShiftId == shiftId).ToListAsync(ct);

        var payments = completed.SelectMany(s => s.Payments)
            .GroupBy(p => p.Method)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var refunds = returns.SelectMany(r => r.Refunds).GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        decimal Movements(CashMovementType type, CashCurrency currency) =>
            shift.CashMovements.Where(m => m.Type == type && m.Currency == currency).Sum(m => m.Amount);

        // What physically went in and out of the drawer, per currency.
        var cashSales = completed.Sum(s => s.CashTendered - s.ChangeGiven);
        var cashSalesLbp = completed.Sum(s => s.CashTenderedLbp - s.ChangeGivenLbp);
        var refundsLbp = returns.SelectMany(r => r.Refunds).Where(r => r.Method == RefundMethod.CashLbp).Sum(r => r.AmountLbp);

        var payIns = Movements(CashMovementType.PayIn, CashCurrency.Usd);
        var payOuts = Movements(CashMovementType.PayOut, CashCurrency.Usd);
        var payInsLbp = Movements(CashMovementType.PayIn, CashCurrency.Lbp);
        var payOutsLbp = Movements(CashMovementType.PayOut, CashCurrency.Lbp);

        var settlements = await db.DeliverySettlements.AsNoTracking()
            .Where(d => d.ShiftId == shiftId)
            .Select(d => new { d.Method, d.Received, d.ReceivedLbp })
            .ToListAsync(ct);
        var deliveryCash = settlements.Where(d => d.Method == SettlementMethod.Cash).Sum(d => d.Received);
        var deliveryCashLbp = settlements.Where(d => d.Method == SettlementMethod.CashLbp).Sum(d => d.ReceivedLbp);

        var expected = shift.OpeningFloat + cashSales + deliveryCash + payIns - payOuts - refunds.GetValueOrDefault(RefundMethod.Cash);
        var expectedLbp = shift.OpeningFloatLbp + cashSalesLbp + deliveryCashLbp + payInsLbp - payOutsLbp - refundsLbp;
        var lbpEnabled = await db.Settings.AsNoTracking().Select(s => s.LbpEnabled).FirstOrDefaultAsync(ct);
        var showLbp = lbpEnabled || shift.OpeningFloatLbp != 0 || cashSalesLbp != 0 || refundsLbp != 0 || deliveryCashLbp != 0
                      || payInsLbp != 0 || payOutsLbp != 0 || shift.CountedCashLbp is not null;

        return new ShiftSummary
        {
            ShiftId = shift.Id,
            Cashier = shift.User?.FullName ?? "",
            Till = shift.TillName,
            ClosedBy = shift.ClosedByUserId is { } closer && closer != shift.UserId ? shift.ClosedBy?.FullName : null,
            Handovers = shift.Handovers.OrderBy(h => h.At)
                .Select(h => new HandoverRow(h.At, h.FromUser?.FullName ?? "?", h.ToUser?.FullName ?? "?")).ToList(),
            OpenedAt = shift.OpenedAt,
            ClosedAt = shift.ClosedAt,
            OpeningFloat = shift.OpeningFloat,
            NoSaleOpenings = shift.NoSaleOpenings,
            SalesCount = completed.Count,
            VoidedCount = sales.Count - completed.Count,
            ItemsSold = completed.Sum(s => s.Lines.Sum(l => l.Quantity)),
            GrossSales = completed.Sum(s => s.Subtotal),
            Discounts = completed.Sum(s => s.DiscountTotal),
            Tax = completed.Sum(s => s.TaxTotal),
            TotalSales = completed.Sum(s => s.Total),
            Payments = payments,
            ReturnsCount = returns.Count,
            Refunds = refunds,
            TotalRefunds = returns.Sum(r => r.TotalRefund),
            PayIns = payIns,
            PayOuts = payOuts,
            ExpectedCash = shift.Status == ShiftStatus.Closed ? shift.ExpectedCash : expected,
            CountedCash = shift.CountedCash,
            CashMovements = shift.CashMovements.OrderBy(m => m.CreatedAt).ToList(),
            CashSales = cashSales,
            DeliveryCash = deliveryCash,
            DeliveryCashLbp = deliveryCashLbp,
            OpeningFloatLbp = shift.OpeningFloatLbp,
            CashSalesLbp = cashSalesLbp,
            CashRefundsLbp = refundsLbp,
            PayInsLbp = payInsLbp,
            PayOutsLbp = payOutsLbp,
            ExpectedCashLbp = shift.Status == ShiftStatus.Closed ? shift.ExpectedCashLbp : expectedLbp,
            CountedCashLbp = shift.CountedCashLbp,
            ShowLbp = showLbp,
        };
    }

    private static decimal RoundLbp(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);
}
