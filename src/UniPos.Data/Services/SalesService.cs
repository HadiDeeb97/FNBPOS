using System.Text.Json;
using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Menu;
using UniPos.Core.Pricing;
using UniPos.Core.Security;
using UniPos.Core.Text;
using Microsoft.EntityFrameworkCore;
using UniPos.Core.Localization;

namespace UniPos.Data.Services;

public class SalesService(IDbContextFactory<PosDbContext> factory)
{
    /// <summary>Most sales the history screen lists at once (newest first).</summary>
    public const int HistoryLimit = 1000;

    public Task<Sale> CompleteSaleAsync(CheckoutRequest request, CancellationToken ct = default) =>
        QueryHelpers.RetryOnTillConflictAsync(() => CompleteOnceAsync(request, ct), ct);

    private async Task<Sale> CompleteOnceAsync(CheckoutRequest request, CancellationToken ct)
    {
        if (request.Lines.Count == 0) throw new BusinessRuleException(Loc.T("Err.CartEmpty"));
        if (request.Lines.Any(l => l.Quantity <= 0)) throw new BusinessRuleException(Loc.T("Err.QtyAtLeastOne"));
        if (request.DeliveryFee < 0) throw new BusinessRuleException(Loc.T("Err.DeliveryFeeNegative"));
        if (request.DeliveryFee > 0 && request.Channel == SalesChannel.InStore)
            throw new BusinessRuleException(Loc.T("Err.DeliveryFeeInStore"));
        if (request.Channel != SalesChannel.InStore && request.CustomerId is null)
            throw new BusinessRuleException(Loc.T("Err.OnlineNeedsCustomer"));
        if (request.Channel == SalesChannel.InStore && request.Payments.Any(p => p.Method == PaymentMethod.Delivery && p.Amount > 0))
            throw new BusinessRuleException(Loc.T("Err.DeliveryPaymentInStore"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? new StoreSettings();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId && u.IsActive, ct)
                   ?? throw new BusinessRuleException(Loc.T("Err.CashierInactive"));
        if (request.Channel != SalesChannel.InStore && !(await FeatureService.ReadAsync(db, request.TillId, ct)).Delivery)
            throw new BusinessRuleException(Loc.T("Err.DeliveryOff"));

        Shift? shift = null;
        if (request.ShiftId is { } shiftId)
        {
            shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct);
            if (shift is null || shift.Status != ShiftStatus.Open)
                throw new BusinessRuleException(Loc.T("Err.ShiftClosedSell"));
            // Saved with the sale, so a drawer closed meanwhile (its Z report already counted) refuses this sale.
            db.Entry(shift).Property(s => s.Status).IsModified = true;
        }

        Customer? customer = null;
        if (request.CustomerId is { } customerId)
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
                       ?? throw new BusinessRuleException(Loc.T("Err.CustomerNotFound"));

        // Load the items with their options (tracked: their stock changes) and price the cart with server-side prices.
        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await ProductService.WithOptions(db.Products)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var priced = new List<PricedLine>();
        foreach (var line in request.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var p) || !p.IsActive)
                throw new BusinessRuleException(Loc.T("Err.ItemUnavailable"));
            if (p.SoldOut) throw new BusinessRuleException(Loc.T("Err.ItemSoldOut", p.Name));
            if (ModifierRules.Problem(p, line.PickedOptions) is { } problem) throw new BusinessRuleException(problem);
            var options = ModifierRules.Ordered(p, line.PickedOptions);
            priced.Add(new PricedLine(line, p, options, ModifierRules.Price(p, options), ModifierRules.Cost(p, options)));
        }

        // The price charged: the menu price, or one typed at the till (to the cent, and more than nothing).
        if (request.Lines.Any(l => l.UnitPrice is { } typed && Money.Round(typed) <= 0))
            throw new BusinessRuleException(Loc.T("Err.PriceMustBePositive"));

        var totals = CartCalculator.Calculate(
            priced.Select(l => new CartLineInput(l.Charged, l.Request.Quantity, l.Request.DiscountType, l.Request.DiscountValue)).ToList(),
            request.CartDiscountType, request.CartDiscountValue, settings.TaxRate, settings.PricesIncludeTax);

        await EnforcePriceRulesAsync(db, user, request, totals, priced, settings, ct);

        // Payments.
        var sale = new Sale
        {
            CreatedAt = DateTime.Now,
            UserId = user.Id,
            CustomerId = customer?.Id,
            ShiftId = shift?.Id,
            TaxRate = settings.TaxRate,
            PricesIncludeTax = settings.PricesIncludeTax,
            Subtotal = totals.Subtotal,
            DiscountTotal = totals.DiscountTotal,
            TaxTotal = totals.TaxTotal,
            Total = totals.Total + Money.Round(request.DeliveryFee),
            DeliveryFee = Money.Round(request.DeliveryFee),
            Channel = request.Channel,
            Courier = request.Channel == SalesChannel.InStore ? null : QueryHelpers.Clean(request.Courier),
            DeliveryReference = request.Channel == SalesChannel.InStore ? null : QueryHelpers.Clean(request.DeliveryReference)?.ToUpperInvariant(),
            CartDiscountType = request.CartDiscountType,
            CartDiscountValue = request.CartDiscountType == DiscountType.None ? 0 : request.CartDiscountValue,
            Notes = QueryHelpers.Clean(request.Notes),
            Status = SaleStatus.Completed,
        };
        ApplyPayments(sale, request.Payments, customer, settings, request.ChangeIn, request.ExchangeRate, request.GiveChangeUsd, request.GiveChangeLbp);

        // Lines + stock.
        sale.ReceiptNumber = await NextReceiptNumberAsync(db, settings.ReceiptPrefix, sale.CreatedAt, ct);
        for (var i = 0; i < priced.Count; i++)
        {
            var l = priced[i];
            var r = totals.Lines[i];
            sale.Lines.Add(new SaleLine
            {
                ProductId = l.Product.Id,
                ProductName = l.Product.Name,
                Options = ModifierRules.Describe(l.Options),
                Sku = l.Product.Sku,
                CategoryName = l.Product.Category?.Name,
                UnitPrice = l.Charged,
                ListPrice = l.Charged != l.MenuPrice ? l.MenuPrice : null,
                UnitCost = l.Cost,
                Quantity = l.Request.Quantity,
                DiscountAmount = r.Discount,
                TaxAmount = r.Tax,
                LineTotal = r.Total,
                Modifiers = l.Options.Select(o => new SaleLineModifier
                {
                    ModifierId = o.Id, GroupName = o.ModifierGroup!.Name, Name = o.Name, Price = o.Price, Cost = o.Cost,
                }).ToList(),
            });
            StockLedger.Apply(db, l.Product, -l.Request.Quantity, StockMovementType.Sale, user.Id, sale.ReceiptNumber,
                allowNegative: settings.AllowNegativeStock);
        }

        db.Sales.Add(sale);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new TillConflictException(Loc.T("Err.ConcurrentSale"));
        }
        catch (DbUpdateException ex) when (QueryHelpers.IsUniqueViolation(ex))
        {
            throw new TillConflictException(Loc.T("Err.ReceiptNumberTaken"));
        }
        return (await GetAsync(sale.Id, ct))!;
    }

    /// <summary>
    /// A cashier needs a manager's approval to give more than the discount limit off the shelf prices (typed prices and
    /// discounts together), or to sell anything below cost.
    /// </summary>
    private static async Task EnforcePriceRulesAsync(PosDbContext db, User user, CheckoutRequest request, CartTotals totals,
        IReadOnlyList<PricedLine> lines, StoreSettings settings, CancellationToken ct)
    {
        if (Permissions.Has(user.Role, Permission.OverrideDiscountLimit)) return;

        var reduction = PriceRules.ReductionPercent(lines.Select(l => (l.MenuPrice, l.Request.Quantity)).ToList(), totals);
        var belowCost = lines
            .Where((l, i) => PriceRules.IsBelowCost(totals.Lines[i], l.Request.Quantity, l.Cost))
            .Select(l => l.Product.Name)
            .ToList();
        if (reduction <= settings.MaxCashierDiscountPercent && belowCost.Count == 0) return;

        if (request.ApprovedByUserId is { } approverId)
        {
            var approver = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == approverId && u.IsActive, ct);
            if (approver is not null && Permissions.Has(approver.Role, Permission.OverrideDiscountLimit)) return;
        }

        throw new BusinessRuleException(belowCost.Count > 0
            ? Loc.T("Err.BelowCostNeedsApproval", string.Join(", ", belowCost))
            : Loc.T("Err.DiscountNeedsApproval", settings.MaxCashierDiscountPercent));
    }

    internal static void ApplyPayments(
        Sale sale, IReadOnlyList<PaymentInput> payments, Customer? customer, StoreSettings settings,
        ChangeCurrency changeIn = ChangeCurrency.Usd, decimal shownRate = 0, decimal? giveUsd = null, decimal? giveLbp = null)
    {
        if (giveUsd is not null && giveLbp is not null) giveLbp = null; // one side is fixed, the other worked out
        if (payments.Any(p => p.Amount < 0)) throw new BusinessRuleException(Loc.T("Err.PaymentNegative"));

        var nonCash = payments.Where(p => !IsCash(p.Method) && p.Amount > 0).ToList();
        var nonCashTotal = nonCash.Sum(p => Money.Round(p.Amount));
        if (nonCashTotal > sale.Total)
            throw new BusinessRuleException(Loc.T("Err.NonCashExceeds"));

        var tender = new CashTender(
            Money.Round(payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount)),
            Math.Round(payments.Where(p => p.Method == PaymentMethod.CashLbp).Sum(p => p.Amount), 0, MidpointRounding.AwayFromZero),
            changeIn, giveUsd, giveLbp);

        var rate = settings.ActiveLbpRate;
        sale.ExchangeRate = rate;
        if (tender.Lbp > 0 || changeIn != ChangeCurrency.Usd || tender.HasSplit) EnsureRate(settings, shownRate);

        var cashDue = sale.Total - nonCashTotal;
        // Cash handed over when other payments already cover the sale would all be "change": refuse it rather than
        // record cash in and out of the drawer that never happened.
        if (cashDue <= 0 && (tender.Usd > 0 || tender.Lbp > 0))
            throw new BusinessRuleException(Loc.T("Err.CashNotNeeded"));
        var cash = CashSettlement.Calculate(cashDue, tender, rate, settings.LbpRounding);
        if (!cash.IsCovered)
            throw new BusinessRuleException(Loc.T("Err.PaymentShort", cash.ShortUsd));
        // Pounds handed back (also the cents of dollar change) must be at the rate the cashier was shown.
        if (cash.ChangeLbp > 0) EnsureRate(settings, shownRate);

        foreach (var p in nonCash)
        {
            var amount = Money.Round(p.Amount);
            switch (p.Method)
            {
                case PaymentMethod.StoreCredit:
                    if (customer is null) throw new BusinessRuleException(Loc.T("Err.CreditNeedsCustomer"));
                    if (customer.StoreCredit < amount)
                        throw new BusinessRuleException(Loc.T("Err.CustomerCreditOnly", customer.StoreCredit));
                    customer.StoreCredit -= amount;
                    break;

                case PaymentMethod.LoyaltyPoints:
                    if (customer is null) throw new BusinessRuleException(Loc.T("Err.PointsNeedCustomer"));
                    if (settings.LoyaltyPointValue <= 0) throw new BusinessRuleException(Loc.T("Err.LoyaltyDisabled"));
                    break;
            }
            sale.Payments.Add(new Payment { Method = p.Method, Amount = amount, Reference = QueryHelpers.Clean(p.Reference) });
        }

        // Points are worked out once over all points payments: rounding each one up separately could need more
        // points than the customer has (two payments of $0.01 at 3 cents a point would take 2 points, not 1).
        var pointsValue = nonCash.Where(p => p.Method == PaymentMethod.LoyaltyPoints).Sum(p => Money.Round(p.Amount));
        if (pointsValue > 0 && customer is not null)
        {
            var points = (int)Math.Ceiling(pointsValue / settings.LoyaltyPointValue);
            if (customer.LoyaltyPoints < points)
                throw new BusinessRuleException(Loc.T("Err.CustomerPointsOnly", customer.LoyaltyPoints));
            customer.LoyaltyPoints -= points;
            sale.LoyaltyPointsRedeemed += points;
        }

        if (cash.AppliedUsd > 0)
            sale.Payments.Add(new Payment { Method = PaymentMethod.Cash, Amount = cash.AppliedUsd });
        if (cash.AppliedFromLbp > 0)
            sale.Payments.Add(new Payment { Method = PaymentMethod.CashLbp, Amount = cash.AppliedFromLbp });

        sale.CashTendered = tender.Usd;
        sale.ChangeGiven = cash.ChangeUsd;
        sale.CashTenderedLbp = tender.Lbp;
        sale.ChangeGivenLbp = cash.ChangeLbp;

        if (customer is not null)
        {
            var earningSpend = sale.Payments
                .Where(p => EarnsPoints(p.Method))
                .Sum(p => p.Amount);
            sale.LoyaltyPointsEarned = (int)Math.Floor(earningSpend * settings.LoyaltyPointsPerUnit);
            customer.LoyaltyPoints += sale.LoyaltyPointsEarned;
        }
    }

    /// <summary>A cart line with its item and options as they are now, and what one costs the customer and the store.</summary>
    private sealed record PricedLine(CheckoutLine Request, Product Product, List<Modifier> Options, decimal MenuPrice, decimal Cost)
    {
        /// <summary>The menu price, or the one typed at the till.</summary>
        public decimal Charged => Request.UnitPrice is { } typed ? Money.Round(typed) : MenuPrice;
    }

    internal static bool IsCash(PaymentMethod method) => method is PaymentMethod.Cash or PaymentMethod.CashLbp;

    /// <summary>Money that earns (and, when refunded, takes back) loyalty points.</summary>
    internal static bool EarnsPoints(PaymentMethod method) =>
        method is PaymentMethod.Cash or PaymentMethod.CashLbp or PaymentMethod.Card or PaymentMethod.MobileWallet or PaymentMethod.Delivery;

    /// <summary>Refuses pounds when LBP is off, or when the rate changed after the till showed it to the customer.</summary>
    internal static void EnsureRate(StoreSettings settings, decimal shownRate)
    {
        if (settings.ActiveLbpRate <= 0) throw new BusinessRuleException(Loc.T("Err.LbpDisabled"));
        if (shownRate != settings.LbpRate)
            throw new BusinessRuleException(Loc.T("Err.RateChanged", settings.LbpRate.ToString("N0")));
    }

    internal static async Task<string> NextReceiptNumberAsync(PosDbContext db, string prefix, DateTime date, CancellationToken ct)
    {
        var stem = QueryHelpers.DayStem(prefix, date);
        var last = await QueryHelpers.Highest(db.Sales.Where(s => s.ReceiptNumber.StartsWith(stem)).Select(s => s.ReceiptNumber))
            .FirstOrDefaultAsync(ct);
        // A deleted sale's number is never given again: its paper receipt may still be out there.
        var deleted = await db.AppState.AsNoTracking().Where(s => s.Key == DeletedReceiptKey + stem).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return QueryHelpers.NextNumber(prefix, date, QueryHelpers.Later(last, deleted));
    }

    /// <summary>AppState key (plus the day's stem, "R20261026-") holding the highest receipt number deleted that day.</summary>
    internal const string DeletedReceiptKey = "DeletedReceipt:";

    public async Task<Sale?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await SaleQuery(db).FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Sale?> GetByReceiptAsync(string receiptNumber, CancellationToken ct = default)
    {
        var number = receiptNumber.Trim().ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await SaleQuery(db).FirstOrDefaultAsync(s => s.ReceiptNumber == number, ct);
    }

    /// <summary>
    /// Sales for the history list, newest first: with cashier, customer, payments, item count and amount returned, but
    /// not their lines (thousands of rows nobody looks at); <see cref="GetLinesAsync"/> loads them for the sale opened.
    /// </summary>
    /// <param name="online">null = all sales, true = online orders only, false = in-store only.</param>
    public async Task<List<Sale>> SearchAsync(
        DateTime from, DateTime to, string? text = null, bool includeVoided = true, bool? online = null,
        SaleListFilter filter = SaleListFilter.All, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filtered(db, from, to, text, online)
            .Include(s => s.User)
            .Include(s => s.Customer)
            .Include(s => s.Payments)
            .AsSplitQuery();

        if (!includeVoided && filter == SaleListFilter.All) filter = SaleListFilter.Completed;
        query = filter switch
        {
            SaleListFilter.Completed => query.Where(s => s.Status == SaleStatus.Completed),
            SaleListFilter.WithReturns => query.Where(s => s.Status == SaleStatus.Completed && db.Returns.Any(r => r.SaleId == s.Id)),
            SaleListFilter.Voided => query.Where(s => s.Status == SaleStatus.Voided),
            _ => query,
        };

        var list = await query.OrderByDescending(s => s.CreatedAt).Take(HistoryLimit).ToListAsync(ct);

        // What came back on each listed sale, so the list can show it and what the sale is worth after it.
        var ids = list.Select(s => s.Id).ToList();
        var refunded = await db.Returns.AsNoTracking()
            .Where(r => ids.Contains(r.SaleId))
            .GroupBy(r => r.SaleId)
            .Select(g => new { g.Key, Amount = g.Sum(r => r.TotalRefund) })
            .ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
        var items = await db.SaleLines.AsNoTracking()
            .Where(l => ids.Contains(l.SaleId))
            .GroupBy(l => l.SaleId)
            .Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, ct);
        foreach (var sale in list)
        {
            sale.RefundedTotal = refunded.GetValueOrDefault(sale.Id);
            sale.ItemCount = items.GetValueOrDefault(sale.Id);
        }
        return list;
    }

    /// <summary>The lines of one sale, as they are now (returned quantities included).</summary>
    public async Task<List<SaleLine>> GetLinesAsync(int saleId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SaleLines.AsNoTracking().Where(l => l.SaleId == saleId).OrderBy(l => l.Id).ToListAsync(ct);
    }

    /// <summary>
    /// Totals for the history screen's summary over the same dates, search and source as the list (every status, and
    /// not limited to the sales listed).
    /// </summary>
    public async Task<SalesSummary> GetSummaryAsync(DateTime from, DateTime to, string? text = null, bool? online = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filtered(db, from, to, text, online);
        var byStatus = await query
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(s => s.Total) })
            .ToListAsync(ct);
        var completed = query.Where(s => s.Status == SaleStatus.Completed);
        var returned = await completed.CountAsync(s => db.Returns.Any(r => r.SaleId == s.Id), ct);
        var refunded = await db.Returns.Where(r => completed.Any(s => s.Id == r.SaleId)).SumAsync(r => (decimal?)r.TotalRefund, ct) ?? 0;

        var voided = byStatus.FirstOrDefault(x => x.Status == SaleStatus.Voided);
        return new SalesSummary(
            byStatus.Sum(x => x.Count), byStatus.Sum(x => x.Total),
            voided?.Count ?? 0, voided?.Total ?? 0,
            returned, refunded);
    }

    /// <summary>Sales in the dates, from the chosen source, matching the search text (any status).</summary>
    private static IQueryable<Sale> Filtered(PosDbContext db, DateTime from, DateTime to, string? text, bool? online)
    {
        var query = db.Sales.AsNoTracking().Where(s => s.CreatedAt >= from && s.CreatedAt < to);
        if (online is true) query = query.Where(s => s.Channel != SalesChannel.InStore);
        else if (online is false) query = query.Where(s => s.Channel == SalesChannel.InStore);
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
                    EF.Functions.Like(s.Customer!.Phone!, pattern, "\\") ||
                    EF.Functions.Like(s.User!.FullName, pattern, "\\") ||
                    s.Lines.Any(l => EF.Functions.Like(l.ProductName, pattern, "\\") || EF.Functions.Like(l.Sku, pattern, "\\")));
            }
        }
        return query;
    }

    /// <summary>Voids a sale that has no returns: restocks items and reverses credit/points.</summary>
    public async Task VoidSaleAsync(int saleId, int userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException(Loc.T("Err.VoidReasonRequired"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || !Permissions.Has(user.Role, Permission.VoidSales))
            throw new BusinessRuleException(Loc.T("Err.OnlyManagersVoid"));

        var sale = await db.Sales
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Include(s => s.Payments)
            .Include(s => s.Customer)
            .Include(s => s.Shift)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw new BusinessRuleException(Loc.T("Err.SaleNotFound"));

        if (sale.Status == SaleStatus.Voided) throw new BusinessRuleException(Loc.T("Err.AlreadyVoided"));
        if (sale.Lines.Any(l => l.ReturnedQuantity > 0))
            throw new BusinessRuleException(Loc.T("Err.ReturnedCantVoid"));
        if (sale.Shift is { Status: ShiftStatus.Closed })
            throw new BusinessRuleException(Loc.T("Err.VoidShiftClosed"));
        if (sale.DeliverySettlementId is not null)
            throw new BusinessRuleException(Loc.T("Err.VoidSettledDelivery"));

        foreach (var line in sale.Lines)
        {
            StockLedger.Apply(db, line.Product!, line.Quantity, StockMovementType.Void, userId, sale.ReceiptNumber, "Sale voided");
            // Checked on save, so a return taken at another till meanwhile stops the void.
            db.Entry(line).Property(l => l.ReturnedQuantity).IsModified = true;
        }

        if (sale.Customer is { } customer)
        {
            customer.StoreCredit += sale.Payments.Where(p => p.Method == PaymentMethod.StoreCredit).Sum(p => p.Amount);
            customer.LoyaltyPoints += sale.LoyaltyPointsRedeemed;
            customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - sale.LoyaltyPointsEarned);
        }

        sale.Status = SaleStatus.Voided;
        sale.VoidedAt = DateTime.Now;
        sale.VoidedByUserId = userId;
        sale.VoidReason = reason.Trim();
        await SaveAsync(db, ct);
    }

    /// <summary>
    /// Erases a sale for good: its stock goes back on the shelf, store credit and points spent or earned on it are
    /// reversed, and the sale, its lines and payments are removed, so it is gone from history, the shift and reports.
    /// Only while its shift is open and nothing was returned or settled by a delivery company (as for a void). A sale
    /// voided earlier just disappears (its stock and balances were put back by the void). The stock history keeps the
    /// sale's movement and the one putting it back, so the stock still adds up; the receipt number is never reused.
    /// </summary>
    public Task DeleteSaleAsync(int saleId, int userId, CancellationToken ct = default) =>
        QueryHelpers.RetryOnTillConflictAsync(async () =>
        {
            await DeleteOnceAsync(saleId, userId, ct);
            return true;
        }, ct);

    private async Task DeleteOnceAsync(int saleId, int userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || !Permissions.Has(user.Role, Permission.DeleteSales))
            throw new BusinessRuleException(Loc.T("Err.OnlyManagersDelete"));

        var sale = await db.Sales
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Include(s => s.Payments)
            .Include(s => s.Customer)
            .Include(s => s.Shift)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw new BusinessRuleException(Loc.T("Err.SaleNotFound"));

        if (sale.Lines.Any(l => l.ReturnedQuantity > 0) || await db.Returns.AnyAsync(r => r.SaleId == saleId, ct))
            throw new BusinessRuleException(Loc.T("Err.ReturnedCantDelete"));
        if (sale.Shift is { Status: ShiftStatus.Closed })
            throw new BusinessRuleException(Loc.T("Err.DeleteShiftClosed"));
        if (sale.DeliverySettlementId is not null)
            throw new BusinessRuleException(Loc.T("Err.DeleteSettledDelivery"));

        if (sale.Status != SaleStatus.Voided)
        {
            foreach (var line in sale.Lines)
                StockLedger.Apply(db, line.Product!, line.Quantity, StockMovementType.SaleDeleted, userId, sale.ReceiptNumber, "Sale deleted");
            if (sale.Customer is { } customer)
            {
                customer.StoreCredit += sale.Payments.Where(p => p.Method == PaymentMethod.StoreCredit).Sum(p => p.Amount);
                customer.LoyaltyPoints += sale.LoyaltyPointsRedeemed;
                customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - sale.LoyaltyPointsEarned);
            }
        }

        // Keep the day's highest deleted number, so the next sale doesn't take this one.
        var dash = sale.ReceiptNumber.LastIndexOf('-');
        if (dash > 0)
        {
            var key = DeletedReceiptKey + sale.ReceiptNumber[..(dash + 1)];
            var row = await db.AppState.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (row is null) db.AppState.Add(new AppState { Key = key, Value = sale.ReceiptNumber });
            else row.Value = QueryHelpers.Later(row.Value, sale.ReceiptNumber)!;
        }

        db.Sales.Remove(sale); // its lines and payments go with it
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A return, a settlement or another delete reached this sale first: look at it again from the start.
            throw new TillConflictException(Loc.T("Err.ConcurrentSale"));
        }
        catch (DbUpdateException ex) when (QueryHelpers.IsUniqueViolation(ex))
        {
            throw new TillConflictException(Loc.T("Err.ConcurrentSale")); // another till deleted a sale of the same day
        }
    }

    // ---- Held (parked) carts -------------------------------------------------------------

    public async Task<HeldSale> HoldAsync(string label, int userId, HeldCart cart, CancellationToken ct = default)
    {
        if (cart.Lines.Count == 0) throw new BusinessRuleException(Loc.T("Err.HoldEmpty"));
        await using var db = await factory.CreateDbContextAsync(ct);
        var held = new HeldSale
        {
            Label = string.IsNullOrWhiteSpace(label) ? $"Held {DateTime.Now:HH:mm}" : label.Trim(),
            UserId = userId,
            CustomerId = cart.CustomerId,
            CreatedAt = DateTime.Now,
            Payload = JsonSerializer.Serialize(cart),
        };
        db.HeldSales.Add(held);
        await db.SaveChangesAsync(ct);
        return held;
    }

    public async Task<List<HeldSale>> GetHeldAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.HeldSales.AsNoTracking().OrderBy(h => h.CreatedAt).ToListAsync(ct);
    }

    /// <summary>Returns the cart and deletes the held record.</summary>
    /// <summary>Reads a held cart without taking it off hold (to load its items before <see cref="ResumeAsync"/>).</summary>
    public async Task<HeldCart> PeekHeldAsync(int heldSaleId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var payload = await db.HeldSales.AsNoTracking().Where(h => h.Id == heldSaleId).Select(h => h.Payload).FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException(Loc.T("Err.HeldNotFound"));
        return JsonSerializer.Deserialize<HeldCart>(payload) ?? throw new BusinessRuleException(Loc.T("Err.HeldCorrupt"));
    }

    /// <summary>Takes a cart off hold and returns it; fails if another till already resumed it.</summary>
    public async Task<HeldCart> ResumeAsync(int heldSaleId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var held = await db.HeldSales.FindAsync([heldSaleId], ct) ?? throw new BusinessRuleException(Loc.T("Err.HeldNotFound"));
        var cart = JsonSerializer.Deserialize<HeldCart>(held.Payload) ?? throw new BusinessRuleException(Loc.T("Err.HeldCorrupt"));
        db.HeldSales.Remove(held);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.HeldNotFound")); // another till resumed it a moment earlier
        }
        return cart;
    }

    public async Task DeleteHeldAsync(int heldSaleId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.HeldSales.Where(h => h.Id == heldSaleId).ExecuteDeleteAsync(ct);
    }

    /// <summary>A sale with its lines and payments in the order they were added (the receipt's order).</summary>
    internal static IQueryable<Sale> SaleQuery(PosDbContext db) =>
        db.Sales.AsNoTracking()
            .Include(s => s.Lines.OrderBy(l => l.Id))
            .Include(s => s.Payments.OrderBy(p => p.Id))
            .Include(s => s.Customer)
            .Include(s => s.User)
            .AsSplitQuery();

    internal static async Task SaveAsync(PosDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.ConcurrentSale"));
        }
        catch (DbUpdateException ex) when (QueryHelpers.IsUniqueViolation(ex))
        {
            throw new BusinessRuleException(Loc.T("Err.ReceiptNumberTaken"));
        }
    }
}
