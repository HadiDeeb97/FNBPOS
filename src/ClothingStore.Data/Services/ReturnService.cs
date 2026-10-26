using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Pricing;
using ClothingStore.Core.Security;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

/// <summary>
/// Refunds items from a previous sale. The money goes back the way it was paid (see <see cref="RefundAllocator"/>).
/// Exchanges are handled as a return to store credit followed by a new sale paid with that credit.
/// </summary>
public class ReturnService(IDbContextFactory<PosDbContext> factory)
{
    /// <summary>Works out what returning these items would refund and how, without changing anything.</summary>
    public async Task<RefundPlan> PlanAsync(
        int saleId, IReadOnlyList<ReturnLineRequest> lines, RefundDestination refundTo, CashCurrency? cashCurrency = null,
        bool customerPaidCourier = false, CancellationToken ct = default)
    {
        var requested = Selected(lines);
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? new StoreSettings();
        var state = await LoadAsync(db, saleId, ct);
        var total = PriceLines(state, requested).Sum(l => l.Refund);
        var shares = Shares(total, state, refundTo, cashCurrency, settings, customerPaidCourier);
        return new RefundPlan(total, shares, settings.ActiveLbpRate, settings.LbpRounding);
    }

    /// <summary>
    /// Allocates the refund; then the cash parts are paid in the chosen currency (dollars when LBP is off).
    /// The delivery-company part is only "owed less" when the courier hasn't collected the money: if the company
    /// already paid the store, or the customer already paid the courier (who still owes the store all of it), the
    /// customer gets it back in cash at the counter.
    /// </summary>
    private static IReadOnlyList<RefundShare> Shares(
        decimal total, SaleState state, RefundDestination refundTo, CashCurrency? cashCurrency, StoreSettings settings,
        bool customerPaidCourier)
    {
        var shares = RefundAllocator.Allocate(total, state.Remaining(), refundTo);
        if (state.Sale.DeliverySettlementId is not null || customerPaidCourier)
            shares = shares.Select(s => s.Method == RefundMethod.Delivery ? s with { Method = RefundMethod.Cash } : s).ToList();
        return RefundAllocator.InCurrency(shares, settings.ActiveLbpRate > 0 ? cashCurrency : CashCurrency.Usd);
    }

    public Task<SaleReturn> ProcessReturnAsync(ReturnRequest request, CancellationToken ct = default) =>
        QueryHelpers.RetryOnTillConflictAsync(() => ProcessOnceAsync(request, ct), ct);

    private async Task<SaleReturn> ProcessOnceAsync(ReturnRequest request, CancellationToken ct)
    {
        var requested = Selected(request.Lines);

        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? new StoreSettings();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId && u.IsActive, ct)
                   ?? throw new BusinessRuleException(Loc.T("Err.UserNotFound"));

        var state = await LoadAsync(db, request.SaleId, ct);
        var sale = state.Sale;

        var age = DateTime.Now - sale.CreatedAt;
        if (settings.ReturnWindowDays > 0 && age.TotalDays > settings.ReturnWindowDays &&
            !await IsApprovedAsync(db, user, request.ApprovedByUserId, Permission.OverrideDiscountLimit, ct))
            throw new BusinessRuleException(Loc.T("Err.ReturnWindow", settings.ReturnWindowDays));

        Shift? shift = null;
        if (request.ShiftId is { } shiftId)
        {
            shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct);
            if (shift is not { Status: ShiftStatus.Open }) throw new BusinessRuleException(Loc.T("Err.ShiftClosed"));
            db.Entry(shift).Property(s => s.Status).IsModified = true; // refused if the drawer is closed meanwhile
        }

        var priced = PriceLines(state, requested);
        var total = priced.Sum(l => l.Refund);
        var shares = Shares(total, state, request.RefundTo, request.CashCurrency, settings, request.CustomerPaidCourier);
        var paysLbp = shares.Any(s => s.Method == RefundMethod.CashLbp);
        if (paysLbp) SalesService.EnsureRate(settings, request.ExchangeRate);

        if (shares.Any(s => s.Method == RefundMethod.StoreCredit) && sale.Customer is null)
            throw new BusinessRuleException(Loc.T("Err.CreditRefundNeedsCustomer"));
        if (shares.Any(s => s.IsCash) && shift is null)
            throw new BusinessRuleException(Loc.T("Err.CashRefundNeedsShift"));
        if (shares.Any(s => s.IsCashOverride) &&
            !await IsApprovedAsync(db, user, request.ApprovedByUserId, Permission.OverrideRefundMethod, ct))
            throw new BusinessRuleException(Loc.T("Err.CashOverrideNeedsApproval"));

        var now = DateTime.Now;
        var ret = new SaleReturn
        {
            CreatedAt = now,
            SaleId = sale.Id,
            UserId = user.Id,
            CustomerId = sale.CustomerId,
            ShiftId = shift?.Id,
            Reason = QueryHelpers.Clean(request.Reason),
            ReturnNumber = await NextReturnNumberAsync(db, now, ct),
            TotalRefund = total,
            TaxRefund = priced.Sum(l => l.Tax),
            ExchangeRate = paysLbp ? settings.LbpRate : 0,
            Refunds = shares.Select(s => new SaleReturnRefund
            {
                Source = s.Source,
                Method = s.Method,
                Amount = s.Amount,
                AmountLbp = s.Method == RefundMethod.CashLbp ? Lbp.ToGive(s.Amount, settings.LbpRate, settings.LbpRounding) : 0,
            }).ToList(),
        };

        foreach (var (line, req, refund, tax) in priced)
        {
            line.ReturnedQuantity += req.Quantity;
            ret.Lines.Add(new SaleReturnLine
            {
                SaleLineId = line.Id,
                Quantity = req.Quantity,
                RefundAmount = refund,
                TaxAmount = tax,
                Restocked = req.Restock,
            });

            if (req.Restock)
                StockLedger.Apply(db, line.ProductVariant!, req.Quantity, StockMovementType.Return, user.Id, ret.ReturnNumber, QueryHelpers.Clean(request.Reason));
        }

        if (sale.Customer is { } customer)
        {
            customer.StoreCredit += shares.Where(s => s.Method == RefundMethod.StoreCredit).Sum(s => s.Amount);

            // Points paid with come back; points earned on the refunded money are taken back.
            ret.LoyaltyPointsRestored = PointsToMove(state, shares, sale.LoyaltyPointsRedeemed, state.PointsRestoredBefore,
                m => m == PaymentMethod.LoyaltyPoints);
            ret.LoyaltyPointsRemoved = PointsToMove(state, shares, sale.LoyaltyPointsEarned, state.PointsRemovedBefore,
                SalesService.EarnsPoints);
            // Points already spent can't be taken back: record only what actually came off the balance.
            ret.LoyaltyPointsRemoved = Math.Min(ret.LoyaltyPointsRemoved, customer.LoyaltyPoints + ret.LoyaltyPointsRestored);
            customer.LoyaltyPoints += ret.LoyaltyPointsRestored - ret.LoyaltyPointsRemoved;
        }

        db.Returns.Add(ret);
        // Checked on save: fails if the sale was voided, or its delivery money settled, at another till meanwhile.
        db.Entry(sale).Property(s => s.Status).IsModified = true;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new TillConflictException(Loc.T("Err.ConcurrentReturn"));
        }
        catch (DbUpdateException ex) when (QueryHelpers.IsUniqueViolation(ex))
        {
            throw new TillConflictException(Loc.T("Err.ReturnNumberTaken"));
        }
        return ret;
    }

    private sealed record SaleState(
        Sale Sale,
        Dictionary<int, (decimal Refund, decimal Tax)> LineReturns,
        Dictionary<PaymentMethod, decimal> Paid,
        Dictionary<PaymentMethod, decimal> RefundedBefore,
        int PointsRestoredBefore,
        int PointsRemovedBefore)
    {
        /// <summary>What is left to refund on each tender of the sale.</summary>
        public Dictionary<PaymentMethod, decimal> Remaining() =>
            Paid.ToDictionary(p => p.Key, p => p.Value - RefundedBefore.GetValueOrDefault(p.Key));
    }

    private sealed record PricedLine(SaleLine Line, ReturnLineRequest Request, decimal Refund, decimal Tax);

    private static List<ReturnLineRequest> Selected(IReadOnlyList<ReturnLineRequest> lines)
    {
        var requested = lines.Where(l => l.Quantity > 0).ToList();
        return requested.Count == 0 ? throw new BusinessRuleException(Loc.T("Err.ChooseReturnItem")) : requested;
    }

    private static async Task<SaleState> LoadAsync(PosDbContext db, int saleId, CancellationToken ct)
    {
        var sale = await db.Sales
            .Include(s => s.Lines).ThenInclude(l => l.ProductVariant)
            .Include(s => s.Payments)
            .Include(s => s.Customer)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw new BusinessRuleException(Loc.T("Err.SaleNotFound"));

        if (sale.Status != SaleStatus.Completed) throw new BusinessRuleException(Loc.T("Err.VoidedNoReturn"));

        var lineReturns = (await db.ReturnLines
                .Where(rl => rl.SaleLine!.SaleId == sale.Id)
                .Select(rl => new { rl.SaleLineId, rl.RefundAmount, rl.TaxAmount })
                .ToListAsync(ct))
            .GroupBy(x => x.SaleLineId)
            .ToDictionary(g => g.Key, g => (Refund: g.Sum(x => x.RefundAmount), Tax: g.Sum(x => x.TaxAmount)));

        var refundedBefore = (await db.ReturnRefunds
                .Where(r => r.SaleReturn!.SaleId == sale.Id)
                .Select(r => new { r.Source, r.Amount })
                .ToListAsync(ct))
            .GroupBy(r => r.Source)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        var points = await db.Returns
            .Where(r => r.SaleId == sale.Id)
            .Select(r => new { r.LoyaltyPointsRestored, r.LoyaltyPointsRemoved })
            .ToListAsync(ct);

        return new SaleState(
            sale,
            lineReturns,
            sale.Payments.GroupBy(p => p.Method).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount)),
            refundedBefore,
            points.Sum(p => p.LoyaltyPointsRestored),
            points.Sum(p => p.LoyaltyPointsRemoved));
    }

    private static List<PricedLine> PriceLines(SaleState state, IReadOnlyList<ReturnLineRequest> requested)
    {
        var priced = new List<PricedLine>();
        foreach (var req in requested)
        {
            var line = state.Sale.Lines.FirstOrDefault(l => l.Id == req.SaleLineId)
                       ?? throw new BusinessRuleException(Loc.T("Err.ItemNotInSale"));
            var alreadyRequested = priced.Where(p => p.Line.Id == line.Id).Sum(p => p.Request.Quantity);
            if (req.Quantity + alreadyRequested > line.ReturnableQuantity)
                throw new BusinessRuleException(Loc.T("Err.OnlyReturnable", line.ReturnableQuantity, line.ProductName));

            decimal refund, tax;
            if (req.Quantity + alreadyRequested == line.ReturnableQuantity)
            {
                // Last units: refund whatever is left so the line reconciles to the cent.
                state.LineReturns.TryGetValue(line.Id, out var prev);
                refund = line.LineTotal - prev.Refund - priced.Where(p => p.Line.Id == line.Id).Sum(p => p.Refund);
                tax = line.TaxAmount - prev.Tax - priced.Where(p => p.Line.Id == line.Id).Sum(p => p.Tax);
            }
            else
            {
                refund = Money.Round(line.LineTotal * req.Quantity / line.Quantity);
                tax = Money.Round(line.TaxAmount * req.Quantity / line.Quantity);
            }
            priced.Add(new PricedLine(line, req, refund, tax));
        }
        return priced;
    }

    /// <summary>
    /// Points to give back (or take back) on this return: the share of <paramref name="totalPoints"/> matching how much
    /// of the relevant tenders has now been refunded, minus what earlier returns already moved.
    /// </summary>
    private static int PointsToMove(SaleState state, IReadOnlyList<RefundShare> shares, int totalPoints, int movedBefore,
        Func<PaymentMethod, bool> tender)
    {
        var paid = state.Paid.Where(p => tender(p.Key)).Sum(p => p.Value);
        var refunded = state.RefundedBefore.Where(r => tender(r.Key)).Sum(r => r.Value)
                       + shares.Where(s => tender(s.Source)).Sum(s => s.Amount);
        return Math.Max(0, RefundAllocator.ProportionalPoints(totalPoints, refunded, paid) - movedBefore);
    }

    private static async Task<bool> IsApprovedAsync(PosDbContext db, User user, int? approverId, Permission permission, CancellationToken ct)
    {
        if (Permissions.Has(user.Role, permission)) return true;
        if (approverId is null) return false;
        var approver = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == approverId && u.IsActive, ct);
        return approver is not null && Permissions.Has(approver.Role, permission);
    }

    public async Task<List<SaleReturn>> SearchAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Returns.AsNoTracking()
            .Include(r => r.Sale)
            .Include(r => r.User)
            .Include(r => r.Customer)
            .Include(r => r.Lines).ThenInclude(l => l.SaleLine)
            .Include(r => r.Refunds)
            .AsSplitQuery()
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<SaleReturn?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Returns.AsNoTracking()
            .Include(r => r.Sale)
            .Include(r => r.User)
            .Include(r => r.Customer)
            .Include(r => r.Lines).ThenInclude(l => l.SaleLine)
            .Include(r => r.Refunds)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    private static async Task<string> NextReturnNumberAsync(PosDbContext db, DateTime date, CancellationToken ct)
    {
        const string prefix = "RT";
        var stem = QueryHelpers.DayStem(prefix, date);
        var last = await QueryHelpers.Highest(db.Returns.Where(r => r.ReturnNumber.StartsWith(stem)).Select(r => r.ReturnNumber))
            .FirstOrDefaultAsync(ct);
        return QueryHelpers.NextNumber(prefix, date, last);
    }
}
