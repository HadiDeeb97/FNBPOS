using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Pricing;
using ClothingStore.Data.OnlineReports;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace ClothingStore.Tests;

/// <summary>
/// Days of random shop activity on two tills through the real services (sales with every kind of payment, discounts,
/// pounds, held carts, online orders, returns, voids, courier money, purchase orders, stock counts, cash in and out,
/// hand-overs), checking after each day that everything adds up: stock against its history, each drawer against the
/// money that went through it, customers' credit and points, couriers' balances, and the reports against the receipts.
/// </summary>
public class ShopSimulationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task Random_days_of_business_keep_every_figure_in_balance(int seed, bool pricesIncludeTax)
    {
        await using var db = await TestDatabase.CreateAsync(s =>
        {
            s.PricesIncludeTax = pricesIncludeTax;
            s.TaxRate = pricesIncludeTax ? 11m : 10m;
            s.LoyaltyPointValue = 0.05m;
        });
        var sim = await Shop.CreateAsync(db, seed, output);
        for (var day = 1; day <= 4; day++)
        {
            await sim.RunDayAsync(day, operations: 70);
            await sim.CheckEverythingAsync($"day {day}");
        }
        output.WriteLine(sim.Stats());
        Assert.Empty(sim.Problems);
    }

    /// <summary>Many more shops when POS_SIMULATE is set to a number of seeds (slow; for checking before a release).</summary>
    [Fact]
    public async Task Many_random_shops_when_asked()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("POS_SIMULATE"), out var shops)) return;
        var problems = new List<string>();
        for (var seed = 100; seed < 100 + shops; seed++)
        {
            var includeTax = seed % 2 == 0;
            await using var db = await TestDatabase.CreateAsync(s =>
            {
                s.PricesIncludeTax = includeTax;
                s.TaxRate = seed % 3 == 0 ? 0m : includeTax ? 11m : 10m;
                s.LoyaltyPointValue = 0.05m;
                s.LbpRounding = seed % 4 == 0 ? 5_000 : 1_000;
            });
            var sim = await Shop.CreateAsync(db, seed, output);
            for (var day = 1; day <= 3; day++)
            {
                await sim.RunDayAsync(day, operations: 120);
                await sim.CheckEverythingAsync($"seed {seed} day {day}");
            }
            output.WriteLine($"seed {seed}: {sim.Problems.Count} problems; {sim.Stats()}");
            problems.AddRange(sim.Problems.Select(p => $"seed {seed}: {p}"));
        }
        Assert.Empty(problems);
    }

    private sealed class Shop
    {
        private readonly TestDatabase _db;
        private readonly Random _rng;
        private readonly ITestOutputHelper _out;
        private StoreSettings _settings = null!;
        private User _cashier2 = null!;
        private readonly List<ProductVariant> _variants = [];
        private readonly List<Customer> _customers = [];
        private Supplier _supplier = null!;
        private readonly string[] _couriers = ["Wakilni", "Toters Express"];

        // What the shop should look like, worked out from what the simulation did.
        private readonly Dictionary<int, int> _stock = [];
        private readonly Dictionary<int, decimal> _credit = [];
        private readonly Dictionary<int, int> _points = [];
        private readonly Dictionary<int, Drawer> _drawers = [];
        private readonly List<int> _saleIds = [];
        private readonly Dictionary<int, (decimal Usd, decimal Lbp, int ShiftId)> _saleCash = [];
        private readonly Dictionary<string, int> _counts = [];
        public List<string> Problems { get; } = [];

        private sealed class Drawer
        {
            public decimal Usd;
            public decimal Lbp;
        }

        private sealed class Till(string id)
        {
            public string Id { get; } = id;
            public Shift Shift = null!;
            public int UserId;
        }

        private readonly Till[] _tills = [new("till-1"), new("till-2")];

        private Shop(TestDatabase db, int seed, ITestOutputHelper output)
        {
            _db = db;
            _rng = new Random(seed);
            _out = output;
        }

        public static async Task<Shop> CreateAsync(TestDatabase db, int seed, ITestOutputHelper output)
        {
            var shop = new Shop(db, seed, output);
            await shop.SetUpAsync();
            return shop;
        }

        private async Task SetUpAsync()
        {
            _settings = await _db.Settings.GetAsync();
            _cashier2 = await _db.Users.SaveAsync(new User { Username = "cashier2", FullName = "Second Cashier", Role = UserRole.Cashier }, "secret9", _db.Admin.Id);
            _supplier = await _db.Suppliers.SaveAsync(new Supplier { Name = "Main Supplier" });

            string[] names = ["Basic Tee", "Slim Jeans", "Abaya", "Summer Dress", "Hoodie", "Scarf", "Linen Shirt", "Kids Shorts", "جلابية مطرّزة", "Leather Belt"];
            decimal[] prices = [12.99m, 39.5m, 65m, 29.99m, 24.75m, 9.5m, 33.33m, 14m, 48.25m, 18.8m];
            for (var i = 0; i < names.Length; i++)
            {
                var category = await _db.Categories.SaveAsync(new Category { Name = $"Category {i % 4}-{Guid.NewGuid():N}"[..20] });
                string[] sizes = i % 3 == 0 ? ["S", "M", "L", "XL"] : ["S", "M", "L"];
                var product = await _db.Products.SaveAsync(new Product
                {
                    Name = names[i], CategoryId = category.Id, Price = prices[i], Cost = Math.Round(prices[i] * 0.45m, 2),
                    Variants = sizes.Select((s, k) => new ProductVariant
                    {
                        Size = s, Color = k % 2 == 0 ? "Black" : "White", StockQuantity = 15 + _rng.Next(10),
                        PriceOverride = i == 4 && s == "XL" ? 27.5m : null,
                    }).ToList(),
                }, _db.Admin.Id);
                _variants.AddRange(product.Variants);
            }
            foreach (var v in _variants) _stock[v.Id] = v.StockQuantity;

            for (var i = 0; i < 12; i++)
            {
                var c = await _db.Customers.SaveAsync(new Customer { FirstName = $"Customer{i}", LastName = "Test", Phone = $"03{i:000000}" });
                _customers.Add(c);
                _credit[c.Id] = 0;
                _points[c.Id] = 0;
                if (i % 3 == 0)
                {
                    await _db.Customers.AdjustStoreCreditAsync(c.Id, 40m, _db.Manager.Id);
                    _credit[c.Id] = 40m;
                }
            }
        }

        private void Count(string what) => _counts[what] = _counts.GetValueOrDefault(what) + 1;

        private void Problem(string text)
        {
            Problems.Add(text);
            _out.WriteLine("PROBLEM: " + text);
        }

        public string Stats() => string.Join(", ", _counts.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"));

        private decimal Rate => _settings.LbpRate;

        // ---- A day ----------------------------------------------------------------------------------------------

        public async Task RunDayAsync(int day, int operations)
        {
            _tills[0].UserId = _db.Cashier.Id;
            _tills[1].UserId = _cashier2.Id;
            foreach (var till in _tills)
            {
                var floatUsd = 100m + _rng.Next(50);
                var floatLbp = 1_000_000m * _rng.Next(0, 4);
                till.Shift = await _db.Shifts.OpenShiftAsync(till.UserId, floatUsd, floatLbp, till.Id, till.Id.ToUpperInvariant());
                _drawers[till.Shift.Id] = new Drawer { Usd = floatUsd, Lbp = floatLbp };
            }

            for (var i = 0; i < operations; i++)
            {
                // Now and then both tills act at the same moment.
                if (_rng.Next(8) == 0)
                {
                    await Task.WhenAll(SellAsync(_tills[0]), SellAsync(_tills[1]));
                    continue;
                }
                var till = _tills[_rng.Next(2)];
                var roll = _rng.Next(100);
                if (roll < 55) await SellAsync(till);
                else if (roll < 67) await ReturnAsync(till);
                else if (roll < 71) await VoidAsync(till);
                else if (roll < 75) await HoldAndResumeAsync(till);
                else if (roll < 80) await CashMovementAsync(till);
                else if (roll < 85) await SettleAsync(till);
                else if (roll < 88) await ReceiveStockAsync();
                else if (roll < 91) await CountStockAsync();
                else if (roll < 93) await HandOverAsync();
                else await SellAsync(till, online: true);
            }

            // Close both drawers, counting exactly what should be there.
            foreach (var till in _tills)
            {
                var drawer = _drawers[till.Shift.Id];
                var summary = await _db.Shifts.GetSummaryAsync(till.Shift.Id);
                if (summary.ExpectedCash != drawer.Usd)
                    Problem($"day {day} {till.Id}: drawer expects ${summary.ExpectedCash}, the money that went through it is ${drawer.Usd}");
                if (summary.ExpectedCashLbp != drawer.Lbp)
                    Problem($"day {day} {till.Id}: drawer expects {summary.ExpectedCashLbp} LBP, the money that went through it is {drawer.Lbp} LBP");
                var closed = await _db.Shifts.CloseShiftAsync(till.Shift.Id, drawer.Usd, null, drawer.Lbp, till.UserId);
                if (closed.Variance != 0 || closed.VarianceLbp is not (null or 0))
                    Problem($"day {day} {till.Id}: counted exactly but over/short {closed.Variance} / {closed.VarianceLbp} LBP");
            }
        }

        // ---- Sales --------------------------------------------------------------------------------------------------

        private List<CheckoutLine> RandomCart()
        {
            var lines = new List<CheckoutLine>();
            var wanted = 1 + _rng.Next(4);
            foreach (var v in _variants.OrderBy(_ => _rng.Next()).Take(wanted))
            {
                var available = _stock[v.Id];
                if (available <= 0) continue;
                var qty = Math.Min(available, 1 + _rng.Next(3));
                var discount = _rng.Next(6) switch
                {
                    0 => (DiscountType.Percent, (decimal)_rng.Next(1, 11)),
                    1 => (DiscountType.Amount, Math.Round((decimal)_rng.NextDouble() * 2m, 2)),
                    _ => (DiscountType.None, 0m),
                };
                // Now and then the cashier types another price: higher, a little lower, or below cost.
                decimal? typed = _rng.Next(7) == 0 ? Math.Round(Price(v.Id) * (decimal)(0.5 + _rng.NextDouble() * 0.9), 2) : null;
                lines.Add(new CheckoutLine(v.Id, qty, discount.Item1, discount.Item2, typed));
            }
            return lines;
        }

        private decimal Price(int variantId)
        {
            var v = _variants.First(x => x.Id == variantId);
            return v.PriceOverride ?? _productPrices.GetValueOrDefault(v.ProductId, 0m);
        }

        private Dictionary<int, decimal>? _productPricesCache;
        private Dictionary<int, decimal> _productPrices => _productPricesCache ??= LoadPrices();

        private Dictionary<int, decimal> LoadPrices()
        {
            using var ctx = _db.Factory.CreateDbContext();
            return ctx.Products.AsNoTracking().ToDictionary(p => p.Id, p => p.Price);
        }

        private async Task SellAsync(Till till, bool online = false, IReadOnlyList<CheckoutLine>? cart = null, int? customerId = null)
        {
            var lines = cart ?? RandomCart();
            if (lines.Count == 0) return;
            var cartDiscount = _rng.Next(5) == 0 ? (DiscountType.Percent, 5m) : (DiscountType.None, 0m);
            var bigDiscount = _rng.Next(25) == 0; // needs a manager's approval
            if (bigDiscount) cartDiscount = (DiscountType.Percent, 20m);

            var totals = CartCalculator.Calculate(
                lines.Select(l => new CartLineInput(l.UnitPrice ?? Price(l.VariantId), l.Quantity, l.DiscountType, l.DiscountValue)).ToList(),
                cartDiscount.Item1, cartDiscount.Item2, _settings.TaxRate, _settings.PricesIncludeTax);
            var fee = online ? 3m : 0m;
            var total = totals.Total + fee;

            var customer = customerId is { } cid ? _customers.First(c => c.Id == cid)
                : online || _rng.Next(2) == 0 ? _customers[_rng.Next(_customers.Count)] : null;
            var payments = new List<PaymentInput>();
            decimal tenderUsd = 0, tenderLbp = 0;
            var changeIn = ChangeCurrency.Usd;
            var mode = online ? "delivery" : PickMode(customer, total);
            switch (mode)
            {
                case "card":
                    payments.Add(new PaymentInput(PaymentMethod.Card, total));
                    break;
                case "wallet":
                    payments.Add(new PaymentInput(PaymentMethod.MobileWallet, total));
                    break;
                case "cash":
                    tenderUsd = Math.Ceiling(total) + _rng.Next(0, 3) * 5;
                    break;
                case "lbp":
                    tenderLbp = Math.Ceiling(total * Rate / 100_000m) * 100_000m;
                    changeIn = _rng.Next(2) == 0 ? ChangeCurrency.Usd : ChangeCurrency.Lbp;
                    break;
                case "split":
                    var card = Math.Round(total * 0.4m, 2);
                    payments.Add(new PaymentInput(PaymentMethod.Card, card));
                    tenderUsd = Math.Ceiling(total - card);
                    break;
                case "credit":
                    var credit = Math.Min(_credit[customer!.Id], total);
                    payments.Add(new PaymentInput(PaymentMethod.StoreCredit, credit));
                    if (total - credit > 0) payments.Add(new PaymentInput(PaymentMethod.Card, total - credit));
                    break;
                case "points":
                    var points = Math.Min(_points[customer!.Id], (int)Math.Floor(total / _settings.LoyaltyPointValue));
                    var value = Math.Round(points * _settings.LoyaltyPointValue, 2);
                    payments.Add(new PaymentInput(PaymentMethod.LoyaltyPoints, value));
                    if (total - value > 0) payments.Add(new PaymentInput(PaymentMethod.Card, total - value));
                    break;
                case "delivery":
                    payments.Add(new PaymentInput(PaymentMethod.Delivery, total));
                    break;
            }
            if (tenderUsd > 0) payments.Add(new PaymentInput(PaymentMethod.Cash, tenderUsd));
            if (tenderLbp > 0) payments.Add(new PaymentInput(PaymentMethod.CashLbp, tenderLbp));

            Sale sale;
            try
            {
                sale = await _db.Sales.CompleteSaleAsync(new CheckoutRequest
                {
                    UserId = till.UserId, ShiftId = till.Shift.Id, CustomerId = customer?.Id, Lines = lines,
                    CartDiscountType = cartDiscount.Item1, CartDiscountValue = cartDiscount.Item2, Payments = payments,
                    ChangeIn = changeIn, ExchangeRate = Rate,
                    ApprovedByUserId = bigDiscount || (lines.Any(l => l.UnitPrice != null) && _rng.Next(2) == 0) ? _db.Manager.Id : null,
                    Channel = online ? SalesChannel.WhatsApp : SalesChannel.InStore, DeliveryFee = fee,
                    Courier = online ? _couriers[_rng.Next(2)] : null,
                });
            }
            catch (BusinessRuleException ex)
            {
                if (TillClash(ex)) Problem("sale refused because the other till saved at the same moment: " + ex.Message);
                Count("sale refused: " + ex.Message);
                return;
            }
            Count(online ? "online sale" : $"sale ({mode})");

            lock (_saleIds)
            {
                _saleIds.Add(sale.Id);
                foreach (var l in lines) _stock[l.VariantId] -= l.Quantity;
                if (customer is not null)
                {
                    _credit[customer.Id] -= sale.Payments.Where(p => p.Method == PaymentMethod.StoreCredit).Sum(p => p.Amount);
                    _points[customer.Id] += sale.LoyaltyPointsEarned - sale.LoyaltyPointsRedeemed;
                }
                var cashUsd = tenderUsd - sale.ChangeGiven;
                var cashLbp = tenderLbp - sale.ChangeGivenLbp;
                _drawers[till.Shift.Id].Usd += cashUsd;
                _drawers[till.Shift.Id].Lbp += cashLbp;
                _saleCash[sale.Id] = (cashUsd, cashLbp, till.Shift.Id);
            }

            // The sale on its own: paid in full, nothing more, what was asked for.
            if (sale.Payments.Sum(p => p.Amount) != sale.Total)
                Problem($"{sale.ReceiptNumber}: payments {sale.Payments.Sum(p => p.Amount)} but total {sale.Total}");
            if (sale.Total != total)
                Problem($"{sale.ReceiptNumber}: total {sale.Total}, the cart works out to {total}");
            foreach (var line in lines)
            {
                var saved = sale.Lines.Single(l => l.ProductVariantId == line.VariantId);
                var shelf = Price(line.VariantId);
                var charged = line.UnitPrice ?? shelf;
                if (saved.UnitPrice != charged || saved.ListPrice != (charged != shelf ? shelf : null))
                    Problem($"{sale.ReceiptNumber}: {saved.Sku} charged {saved.UnitPrice} (shelf {saved.ListPrice}), typed {line.UnitPrice}, shelf {shelf}");
            }
            if (!sale.Lines.Select(l => l.ProductVariantId).SequenceEqual(lines.Select(l => l.VariantId)))
                Problem($"{sale.ReceiptNumber}: the receipt lists the items in another order than they were rung up");
            if (sale.Lines.Sum(l => l.LineTotal) + sale.DeliveryFee != sale.Total)
                Problem($"{sale.ReceiptNumber}: lines {sale.Lines.Sum(l => l.LineTotal)} + fee {sale.DeliveryFee} ≠ total {sale.Total}");
            if (sale.CashTendered != tenderUsd || sale.CashTenderedLbp != tenderLbp)
                Problem($"{sale.ReceiptNumber}: tendered ${tenderUsd} + {tenderLbp} LBP recorded as ${sale.CashTendered} + {sale.CashTenderedLbp} LBP");
            var cashPaid = sale.Payments.Where(p => p.Method is PaymentMethod.Cash or PaymentMethod.CashLbp).Sum(p => p.Amount);
            var keptInDrawer = tenderUsd - sale.ChangeGiven + (tenderLbp - sale.ChangeGivenLbp) / Rate;
            if (Math.Abs(keptInDrawer - cashPaid) > _settings.LbpRounding / Rate + 0.01m)
                Problem($"{sale.ReceiptNumber}: cash kept {keptInDrawer:F4} vs cash payments {cashPaid} (change ${sale.ChangeGiven} + {sale.ChangeGivenLbp} LBP)");
            if (sale.ChangeGiven < 0 || sale.ChangeGivenLbp < 0)
                Problem($"{sale.ReceiptNumber}: negative change");
        }

        private static bool TillClash(BusinessRuleException ex) =>
            new[] { "Err.ConcurrentSale", "Err.ReceiptNumberTaken", "Err.ConcurrentReturn", "Err.ReturnNumberTaken" }
                .Any(key => ex.Message == ClothingStore.Core.Localization.Loc.T(key));

        private string PickMode(Customer? customer, decimal total)
        {
            var options = new List<string> { "card", "cash", "cash", "lbp", "split", "wallet" };
            if (customer is not null && _credit[customer.Id] > 0) options.Add("credit");
            if (customer is not null && _points[customer.Id] >= 20) options.Add("points");
            return options[_rng.Next(options.Count)];
        }

        private async Task HoldAndResumeAsync(Till till)
        {
            var lines = RandomCart();
            if (lines.Count == 0) return;
            var customer = _customers[_rng.Next(_customers.Count)];
            var held = await _db.Sales.HoldAsync("", till.UserId,
                new HeldCart(lines.Select(l => new HeldCartLine(l.VariantId, l.Quantity, l.DiscountType, l.DiscountValue)).ToList(),
                    DiscountType.None, 0, customer.Id));
            var cart = await _db.Sales.ResumeAsync(held.Id);
            Count("held and resumed");
            await SellAsync(till, cart: cart.Lines.Select(l => new CheckoutLine(l.VariantId, l.Quantity, l.DiscountType, l.DiscountValue)).ToList(),
                customerId: cart.CustomerId);
        }

        // ---- Returns and voids ---------------------------------------------------------------------------------------

        private async Task ReturnAsync(Till till)
        {
            if (_saleIds.Count == 0) return;
            var sale = await _db.Sales.GetAsync(_saleIds[_rng.Next(_saleIds.Count)]);
            if (sale is null || sale.Status != SaleStatus.Completed) return;
            var lines = sale.Lines.Where(l => l.ReturnableQuantity > 0).OrderBy(_ => _rng.Next()).Take(1 + _rng.Next(2))
                .Select(l => new ReturnLineRequest(l.Id, 1 + _rng.Next(l.ReturnableQuantity), Restock: _rng.Next(6) != 0)).ToList();
            if (lines.Count == 0) return;
            var to = sale.CustomerId is not null && _rng.Next(3) == 0 ? RefundDestination.StoreCredit
                : _rng.Next(6) == 0 ? RefundDestination.Cash : RefundDestination.OriginalPayment;
            // Cash back only in a currency the drawer has enough of.
            var drawer = _drawers[till.Shift.Id];
            var currency = _rng.Next(3) == 0 ? CashCurrency.Lbp : CashCurrency.Usd;
            var plan = await _db.Returns.PlanAsync(sale.Id, lines, to, currency);
            if (plan.CashOutLbp > drawer.Lbp) currency = CashCurrency.Usd;
            plan = await _db.Returns.PlanAsync(sale.Id, lines, to, currency);
            if (plan.CashOut > drawer.Usd || plan.CashOutLbp > drawer.Lbp) return;
            SaleReturn ret;
            try
            {
                ret = await _db.Returns.ProcessReturnAsync(new ReturnRequest
                {
                    SaleId = sale.Id, UserId = till.UserId, ShiftId = till.Shift.Id, RefundTo = to, Lines = lines,
                    ApprovedByUserId = _db.Manager.Id, CashCurrency = currency,
                    ExchangeRate = Rate, Reason = "Changed mind",
                });
            }
            catch (BusinessRuleException ex)
            {
                if (TillClash(ex)) Problem("return refused because the other till saved at the same moment: " + ex.Message);
                Count("return refused: " + ex.Message);
                return;
            }
            Count($"return ({to})");

            foreach (var l in lines.Where(l => l.Restock)) _stock[sale.Lines.First(x => x.Id == l.SaleLineId).ProductVariantId] += l.Quantity;
            _drawers[till.Shift.Id].Usd -= ret.Refunds.Where(r => r.Method == RefundMethod.Cash).Sum(r => r.Amount);
            _drawers[till.Shift.Id].Lbp -= ret.Refunds.Where(r => r.Method == RefundMethod.CashLbp).Sum(r => r.AmountLbp);
            if (sale.CustomerId is { } cid)
            {
                _credit[cid] += ret.Refunds.Where(r => r.Method == RefundMethod.StoreCredit).Sum(r => r.Amount);
                _points[cid] += ret.LoyaltyPointsRestored - ret.LoyaltyPointsRemoved;
            }
            if (ret.Refunds.Sum(r => r.Amount) != ret.TotalRefund)
                Problem($"{ret.ReturnNumber}: refunds {ret.Refunds.Sum(r => r.Amount)} but refund total {ret.TotalRefund}");
            if (ret.TotalRefund != ret.Lines.Sum(l => l.RefundAmount))
                Problem($"{ret.ReturnNumber}: lines {ret.Lines.Sum(l => l.RefundAmount)} but refund total {ret.TotalRefund}");
        }

        private async Task VoidAsync(Till till)
        {
            var open = _saleIds.Where(id => _saleCash.TryGetValue(id, out var c) && c.ShiftId == till.Shift.Id).ToList();
            if (open.Count == 0) return;
            var sale = await _db.Sales.GetAsync(open[_rng.Next(open.Count)]);
            if (sale is null || sale.Status != SaleStatus.Completed || sale.Lines.Any(l => l.ReturnedQuantity > 0) || sale.DeliverySettlementId is not null) return;
            await _db.Sales.VoidSaleAsync(sale.Id, _db.Manager.Id, "Wrong item");
            Count("void");
            foreach (var l in sale.Lines) _stock[l.ProductVariantId] += l.Quantity;
            var cash = _saleCash[sale.Id];
            _drawers[cash.ShiftId].Usd -= cash.Usd;
            _drawers[cash.ShiftId].Lbp -= cash.Lbp;
            if (sale.CustomerId is { } cid)
            {
                _credit[cid] += sale.Payments.Where(p => p.Method == PaymentMethod.StoreCredit).Sum(p => p.Amount);
                _points[cid] += sale.LoyaltyPointsRedeemed;
                _points[cid] = Math.Max(0, _points[cid] - sale.LoyaltyPointsEarned);
            }
        }

        // ---- Drawer, couriers, stock ----------------------------------------------------------------------------------

        private async Task CashMovementAsync(Till till)
        {
            var lbp = _rng.Next(4) == 0;
            var payIn = _rng.Next(2) == 0;
            var amount = lbp ? 500_000m : 5m + _rng.Next(20);
            var drawer = _drawers[till.Shift.Id];
            if (!payIn && (lbp ? drawer.Lbp : drawer.Usd) < amount) return;
            await _db.Shifts.AddCashMovementAsync(till.Shift.Id, payIn ? CashMovementType.PayIn : CashMovementType.PayOut, amount,
                payIn ? "Change float" : "Lunch", till.UserId, lbp ? CashCurrency.Lbp : CashCurrency.Usd);
            Count(payIn ? "pay in" : "pay out");
            var sign = payIn ? 1 : -1;
            if (lbp) drawer.Lbp += sign * amount;
            else drawer.Usd += sign * amount;
        }

        private async Task SettleAsync(Till till)
        {
            var owed = (await _db.Deliveries.GetAsync(false)).Where(r => !r.IsPaid && r.Owed > 0).ToList();
            if (owed.Count == 0) return;
            var courier = owed[_rng.Next(owed.Count)].Courier;
            var rows = owed.Where(r => r.Courier == courier).ToList();
            var method = _rng.Next(3) switch { 0 => SettlementMethod.Transfer, 1 => SettlementMethod.CashLbp, _ => SettlementMethod.Cash };
            var settlement = await _db.Deliveries.SettleAsync(new SettleDeliveriesRequest
            {
                SaleIds = rows.Select(r => r.SaleId).ToList(), UserId = method == SettlementMethod.Transfer ? _db.Manager.Id : till.UserId,
                ShiftId = method == SettlementMethod.Transfer ? null : till.Shift.Id, Method = method, ExchangeRate = Rate,
            });
            Count($"courier paid ({method})");
            if (settlement.Expected != rows.Sum(r => r.Owed))
                Problem($"settlement expected {settlement.Expected}, the orders owe {rows.Sum(r => r.Owed)}");
            if (method == SettlementMethod.Cash) _drawers[till.Shift.Id].Usd += settlement.Received;
            if (method == SettlementMethod.CashLbp) _drawers[till.Shift.Id].Lbp += settlement.ReceivedLbp;
        }

        private async Task ReceiveStockAsync()
        {
            var picks = _variants.OrderBy(_ => _rng.Next()).Take(3).ToList();
            var order = await _db.PurchaseOrders.SaveAsync(new PurchaseOrder
            {
                SupplierId = _supplier.Id,
                Lines = picks.Select(v => new PurchaseOrderLine { ProductVariantId = v.Id, QuantityOrdered = 10, UnitCost = 7.5m }).ToList(),
            }, _db.Manager.Id);
            await _db.PurchaseOrders.MarkOrderedAsync(order.Id);
            var saved = (await _db.PurchaseOrders.GetAsync(order.Id))!;
            var received = saved.Lines.ToDictionary(l => l.Id, _ => 4 + _rng.Next(7));
            await _db.PurchaseOrders.ReceiveAsync(order.Id, received, _db.Manager.Id);
            foreach (var l in saved.Lines) _stock[l.ProductVariantId] += received[l.Id];
            Count("stock received");
        }

        private async Task CountStockAsync()
        {
            var v = _variants[_rng.Next(_variants.Count)];
            var counted = Math.Max(0, _stock[v.Id] + _rng.Next(-2, 3));
            await _db.Inventory.SetCountedStockAsync(v.Id, counted, "Shelf count", _db.Manager.Id);
            _stock[v.Id] = counted;
            Count("stock counted");
        }

        private async Task HandOverAsync()
        {
            // The manager takes over till 2 for a while, then hands it back.
            var till = _tills[1];
            if (till.UserId == _db.Manager.Id)
            {
                await _db.Shifts.TakeOverAsync(till.Shift.Id, _cashier2.Id);
                till.UserId = _cashier2.Id;
            }
            else
            {
                await _db.Shifts.TakeOverAsync(till.Shift.Id, _db.Manager.Id);
                till.UserId = _db.Manager.Id;
            }
            Count("hand-over");
        }

        // ---- The checks ------------------------------------------------------------------------------------------------

        public async Task CheckEverythingAsync(string when)
        {
            await using var ctx = await _db.Factory.CreateDbContextAsync();

            // Stock: what the shop did, and each item's movement history.
            var variants = await ctx.ProductVariants.AsNoTracking().ToListAsync();
            var movements = await ctx.StockMovements.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
            foreach (var v in variants.Where(v => _stock.ContainsKey(v.Id)))
            {
                if (v.StockQuantity != _stock[v.Id]) Problem($"{when}: {v.Sku} stock {v.StockQuantity}, should be {_stock[v.Id]}");
                var history = movements.Where(m => m.ProductVariantId == v.Id).ToList();
                if (history.Sum(m => m.QuantityChange) != v.StockQuantity)
                    Problem($"{when}: {v.Sku} movements add up to {history.Sum(m => m.QuantityChange)}, stock is {v.StockQuantity}");
                var running = 0;
                foreach (var m in history)
                {
                    running += m.QuantityChange;
                    if (m.QuantityAfter != running) { Problem($"{when}: {v.Sku} movement {m.Id} says {m.QuantityAfter} after, running total {running}"); break; }
                }
                if (v.StockQuantity < 0) Problem($"{when}: {v.Sku} negative stock {v.StockQuantity}");
            }

            // Customers: store credit and points.
            var customers = await ctx.Customers.AsNoTracking().ToListAsync();
            foreach (var c in customers.Where(c => _credit.ContainsKey(c.Id)))
            {
                if (c.StoreCredit != _credit[c.Id]) Problem($"{when}: {c.FirstName} credit {c.StoreCredit}, should be {_credit[c.Id]}");
                if (c.LoyaltyPoints != _points[c.Id]) Problem($"{when}: {c.FirstName} points {c.LoyaltyPoints}, should be {_points[c.Id]}");
                if (c.StoreCredit < 0 || c.LoyaltyPoints < 0) Problem($"{when}: {c.FirstName} negative credit or points");
            }

            // Every sale and return on its own.
            var sales = await ctx.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Payments).AsSplitQuery().ToListAsync();
            var returns = await ctx.Returns.AsNoTracking().Include(r => r.Lines).Include(r => r.Refunds).AsSplitQuery().ToListAsync();
            foreach (var s in sales)
            {
                var returnedQty = returns.SelectMany(r => r.Lines).Where(rl => s.Lines.Any(l => l.Id == rl.SaleLineId)).GroupBy(rl => rl.SaleLineId)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
                foreach (var l in s.Lines)
                    if (l.ReturnedQuantity != returnedQty.GetValueOrDefault(l.Id) || l.ReturnedQuantity > l.Quantity)
                        Problem($"{when}: {s.ReceiptNumber} line {l.Sku} returned {l.ReturnedQuantity} of {l.Quantity}, returns say {returnedQty.GetValueOrDefault(l.Id)}");
                var refunded = returns.Where(r => r.SaleId == s.Id).Sum(r => r.TotalRefund);
                if (refunded > s.Total) Problem($"{when}: {s.ReceiptNumber} refunded {refunded} of {s.Total}");
                var bySource = returns.Where(r => r.SaleId == s.Id).SelectMany(r => r.Refunds).GroupBy(r => r.Source).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
                foreach (var (method, amount) in bySource)
                    if (amount > s.Payments.Where(p => p.Method == method).Sum(p => p.Amount))
                        Problem($"{when}: {s.ReceiptNumber} refunded {amount} from {method}, only {s.Payments.Where(p => p.Method == method).Sum(p => p.Amount)} paid that way");
            }

            // Receipt and return numbers are unique.
            if (sales.Select(s => s.ReceiptNumber).Distinct().Count() != sales.Count) Problem($"{when}: duplicate receipt numbers");
            if (returns.Select(r => r.ReturnNumber).Distinct().Count() != returns.Count) Problem($"{when}: duplicate return numbers");

            // Couriers: what they owe.
            var balances = await _db.Deliveries.GetBalancesAsync();
            var settledIds = sales.Where(s => s.DeliverySettlementId != null).Select(s => s.Id).ToHashSet();
            var owed = sales.Where(s => s.Status == SaleStatus.Completed && !settledIds.Contains(s.Id))
                .Sum(s => s.Payments.Where(p => p.Method == PaymentMethod.Delivery).Sum(p => p.Amount)
                          - returns.Where(r => r.SaleId == s.Id).SelectMany(r => r.Refunds).Where(r => r.Method == RefundMethod.Delivery).Sum(r => r.Amount));
            if (balances.Sum(b => b.Owed) != owed) Problem($"{when}: couriers owe {balances.Sum(b => b.Owed)} by the Deliveries page, {owed} by the sales");

            // The reports against the receipts.
            var today = DateTime.Today;
            var report = await _db.Reports.GetSalesReportAsync(today, today.AddDays(1));
            var completed = sales.Where(s => s.Status == SaleStatus.Completed).ToList();
            void Same(string what, decimal shown, decimal actual)
            {
                if (shown != actual) Problem($"{when}: report {what} {shown}, receipts say {actual}");
            }
            Same("total sales", report.TotalSales, completed.Sum(s => s.Total));
            Same("transactions", report.Transactions, completed.Count);
            Same("voided", report.VoidedCount, sales.Count - completed.Count);
            Same("tax", report.Tax, completed.Sum(s => s.TaxTotal));
            Same("discounts", report.Discounts, completed.Sum(s => s.DiscountTotal));
            Same("items sold", report.ItemsSold, completed.Sum(s => s.Lines.Sum(l => l.Quantity)));
            Same("refunds", report.Refunds, returns.Sum(r => r.TotalRefund));
            Same("returns", report.ReturnsCount, returns.Count);
            Same("delivery fees", report.DeliveryFees, completed.Sum(s => s.DeliveryFee));
            Same("payments", report.ByPaymentMethod.Sum(p => p.Amount), completed.Sum(s => s.Payments.Sum(p => p.Amount)));
            Same("by channel", report.ByChannel.Sum(p => p.Amount), report.TotalSales);
            Same("by cashier", report.ByCashier.Sum(p => p.Amount), report.TotalSales);
            Same("by day", report.ByDay.Sum(p => p.Total), report.TotalSales);
            Same("by hour", report.ByHour.Sum(p => p.Total), report.TotalSales);
            var netItems = completed.Sum(s => s.Lines.Sum(l => l.LineTotal - l.TaxAmount))
                           - returns.Sum(r => r.TotalRefund - r.TaxRefund);
            Same("by category (net of tax and returns)", report.ByCategory.Sum(c => c.Amount), netItems);
            Same("by category items", report.ByCategory.Sum(c => c.Quantity),
                completed.Sum(s => s.Lines.Sum(l => l.Quantity)) - returns.Sum(r => r.Lines.Sum(l => l.Quantity)));
            var cost = completed.Sum(s => s.Lines.Sum(l => l.UnitCost * l.Quantity))
                       - returns.SelectMany(r => r.Lines).Where(l => l.Restocked).Sum(l => l.Quantity * sales.SelectMany(s => s.Lines).First(x => x.Id == l.SaleLineId).UnitCost);
            Same("cost of goods", report.CostOfGoods, cost);

            // Sales history totals.
            var summary = await _db.Sales.GetSummaryAsync(today, today.AddDays(1));
            Same("history count", summary.Count, sales.Count);
            Same("history refunded", summary.Refunded, returns.Sum(r => r.TotalRefund));

            // Drawers: every closed drawer's sales add up to the day.
            var shifts = await ctx.Shifts.AsNoTracking().ToListAsync();
            decimal shiftSales = 0;
            foreach (var shift in shifts) shiftSales += (await _db.Shifts.GetSummaryAsync(shift.Id)).TotalSales;
            Same("drawers' sales", shiftSales, completed.Sum(s => s.Total));

            // The website gets the same numbers, and the item list the same stock.
            var builder = new OnlineReportsBuilder(_db.Factory, _db.Reports, _db.Shifts);
            var online = await builder.PeriodAsync("today", today);
            Same("website total", online.Totals.TotalSales, report.TotalSales);
            Same("website profit", online.Totals.GrossProfit, report.GrossProfit);
            var catalog = await builder.CatalogAsync(today);
            var listed = catalog.SelectMany(p => p.Products).SelectMany(p => p.V).ToDictionary(v => (string)v[2]!, v => (int)v[4]!);
            foreach (var v in variants.Where(v => v.IsActive))
                if (listed.GetValueOrDefault(v.Sku, int.MinValue) != v.StockQuantity)
                    Problem($"{when}: website item list shows {v.Sku} with {listed.GetValueOrDefault(v.Sku, int.MinValue)}, stock is {v.StockQuantity}");
            await builder.LiveAsync();
            await builder.StockAsync(today);
        }
    }
}
