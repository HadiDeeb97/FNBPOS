using ClothingStore.Core;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Converters;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.ViewModels;

/// <summary>One bar of a simple bar chart (height already scaled to the chart).</summary>
public sealed record ChartBar(string Label, double Height, string Tooltip);

public sealed partial class ReportsViewModel(
    IDialogService dialogs, ReportService reports, SettingsService settings, PrintService print, DeliveryService deliveries)
    : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.Reports");

    [ObservableProperty]
    public partial DateTime From { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    public partial DateTime To { get; set; } = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DailyBars), nameof(HourlyBars), nameof(SalesChangeText), nameof(TransactionsChangeText),
        nameof(ProfitChangeText), nameof(DeliveryReceived), nameof(DeliveryKept))]
    public partial SalesReport? Report { get; set; }

    [ObservableProperty]
    public partial StockAlerts? Alerts { get; set; }

    /// <summary>What delivery companies still owe right now (not limited to the period).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeliveryOwed))]
    public partial List<CourierBalance> DeliveryBalances { get; set; } = [];

    public decimal DeliveryOwed => DeliveryBalances.Sum(b => b.Owed);
    public decimal DeliveryReceived => Report?.Settlements.Sum(s => s.Received) ?? 0;

    /// <summary>Kept by the companies (fees) or paid short, in the period.</summary>
    public decimal DeliveryKept => -(Report?.Settlements.Sum(s => s.Difference) ?? 0);

    private const double ChartHeight = 150;

    public List<ChartBar> DailyBars => Bars(Report?.ByDay.Select(d => (d.Date.ToString("dd/MM"), d.Total, $"{d.Date:ddd d MMM}: {CurrencyFormat.Format(d.Total)} · {d.Transactions}")));

    public List<ChartBar> HourlyBars => Bars(Report?.ByHour.Select(h => ($"{h.Hour:00}", h.Total, $"{h.Hour:00}:00–{h.Hour + 1:00}:00: {CurrencyFormat.Format(h.Total)} · {h.Transactions}")));

    public string SalesChangeText => ChangeText(Report?.SalesChangePercent);
    public string TransactionsChangeText => ChangeText(Report?.TransactionsChangePercent);
    public string ProfitChangeText => ChangeText(Report?.ProfitChangePercent);

    private static string ChangeText(decimal? percent) => percent switch
    {
        null => Loc.T("Reports.NoComparison"),
        >= 0 => Loc.T("Reports.ChangeUp", percent.Value.ToString("0.#")),
        _ => Loc.T("Reports.ChangeDown", Math.Abs(percent.Value).ToString("0.#")),
    };

    private static List<ChartBar> Bars(IEnumerable<(string Label, decimal Value, string Tooltip)>? points)
    {
        var list = points?.ToList() ?? [];
        var max = list.Count == 0 ? 0 : list.Max(p => p.Value);
        return list.Select(p => new ChartBar(p.Label, max <= 0 ? 0 : Math.Max(2, (double)(p.Value / max) * ChartHeight), p.Tooltip)).ToList();
    }

    [ObservableProperty]
    public partial InventoryValuation? Valuation { get; set; }

    public Task OnNavigatedToAsync() => LoadAsync();

    /// <summary>The dates the figures on screen are for.</summary>
    [ObservableProperty]
    public partial string? PeriodText { get; set; }

    private bool _reloadPending;
    private bool _settingRange;

    // Picking a date reloads straight away (no need to press Run).
    partial void OnFromChanged(DateTime value)
    {
        if (!_settingRange) _ = LoadAsync();
    }

    partial void OnToChanged(DateTime value)
    {
        if (!_settingRange) _ = LoadAsync();
    }

    [RelayCommand]
    private Task RunReportAsync() => LoadAsync();

    /// <summary>
    /// Loads the report for From–To. A request made while one is loading (a quick-range click, a date change) runs
    /// right after it, so the figures always match the dates shown.
    /// </summary>
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            _reloadPending = true;
            return;
        }
        do
        {
            _reloadPending = false;
            var from = From.Date;
            var to = To.Date;
            if (to < from) (from, to) = (to, from);
            await RunAsync(async () =>
            {
                // The four parts don't depend on each other: load them side by side.
                var report = reports.GetSalesReportAsync(from, to.AddDays(1));
                var valuation = reports.GetInventoryValuationAsync();
                var alerts = reports.GetStockAlertsAsync(from, to.AddDays(1));
                var balances = deliveries.GetBalancesAsync();
                await Task.WhenAll(report, valuation, alerts, balances);
                Report = report.Result;
                Valuation = valuation.Result;
                Alerts = alerts.Result;
                DeliveryBalances = balances.Result;
                PeriodText = from == to
                    ? Loc.T("Reports.PeriodDay", from.ToString("dddd d MMMM yyyy"))
                    : Loc.T("Reports.PeriodRange", from.ToString("d MMM yyyy"), to.ToString("d MMM yyyy"));
            });
        }
        while (_reloadPending);
    }

    [RelayCommand]
    private Task QuickRangeAsync(string range)
    {
        var today = DateTime.Today;
        _settingRange = true;
        try
        {
            (From, To) = range switch
            {
                "today" => (today, today),
                "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
                "week" => (today.AddDays(-((7 + (int)today.DayOfWeek - 1) % 7)), today),
                "lastmonth" => (new DateTime(today.Year, today.Month, 1).AddMonths(-1), new DateTime(today.Year, today.Month, 1).AddDays(-1)),
                "year" => (new DateTime(today.Year, 1, 1), today),
                _ => (new DateTime(today.Year, today.Month, 1), today),
            };
        }
        finally
        {
            _settingRange = false;
        }
        return LoadAsync();
    }

    [RelayCommand]
    private void Export(string section)
    {
        if (Report is null) return;
        var path = Dialogs.SaveFile(Loc.T("Reports.ExportTitle"), Loc.T("Common.CsvFilter"), $"{section}_{From:yyyyMMdd}_{To:yyyyMMdd}.csv");
        if (path is null) return;
        try
        {
            switch (section)
            {
                case "daily":
                    CsvExporter.Write(path, [Loc.T("Common.Date"), Loc.T("Reports.Transactions"), Loc.T("Common.Total")],
                        Report.ByDay.Select(d => new object?[] { d.Date.ToString("yyyy-MM-dd"), d.Transactions, d.Total }));
                    break;
                case "payments":
                    CsvExporter.Write(path, [Loc.T("Reports.Method"), Loc.T("Reports.Count"), Loc.T("Common.Amount")], Report.ByPaymentMethod.Select(x => new object?[] { x.Name, x.Count, x.Amount }));
                    break;
                case "channels":
                    CsvExporter.Write(path, [Loc.T("History.Source"), Loc.T("Reports.Sales"), Loc.T("Common.Items"), Loc.T("Common.Total")], Report.ByChannel.Select(x => new object?[] { x.Name, x.Count, x.Quantity, x.Amount }));
                    break;
                case "customers":
                    CsvExporter.Write(path, [Loc.T("Common.Customer"), Loc.T("Reports.Sales"), Loc.T("Common.Items"), Loc.T("Common.Total")], Report.TopCustomers.Select(x => new object?[] { x.Name, x.Count, x.Quantity, x.Amount }));
                    break;
                case "hours":
                    CsvExporter.Write(path, [Loc.T("Reports.Hour"), Loc.T("Reports.Transactions"), Loc.T("Common.Total")], Report.ByHour.Select(h => new object?[] { $"{h.Hour:00}:00", h.Transactions, h.Total }));
                    break;
                case "returns":
                    CsvExporter.Write(path, [Loc.T("Common.Item"), Loc.T("Reports.Units"), Loc.T("Reports.Refunded")], Report.TopReturned.Select(x => new object?[] { x.Name, x.Quantity, x.Amount }));
                    break;
                case "settlements":
                    CsvExporter.Write(path, [Loc.T("Common.Date"), Loc.T("Deliveries.Company"), Loc.T("Reports.Method"), Loc.T("Reports.Orders"), Loc.T("Reports.Expected"), Loc.T("Reports.Received"), Loc.T("Reports.Difference")],
                        Report.Settlements.Select(x => new object?[] { x.Date, x.Courier, Loc.EnumText(x.Method), x.Orders, x.Expected, x.Received, x.Difference }));
                    break;
                case "cash":
                    CsvExporter.Write(path, [Loc.T("Common.Date"), Loc.T("Common.User"), Loc.T("Reports.Type"), Loc.T("Reports.Currency"), Loc.T("Common.Amount"), Loc.T("Common.Reason")],
                        Report.CashMovements.Select(x => new object?[] { x.Date, x.User, Loc.EnumText(x.Type), Loc.EnumText(x.Currency), x.Amount, x.Reason }));
                    break;
                case "shifts":
                    CsvExporter.Write(path, [Loc.T("Shift.ClosedCol"), Loc.T("Common.Cashier"), Loc.T("Shift.ExpectedCol"), Loc.T("Shift.CountedCol"), Loc.T("Shift.VarianceCol"), Loc.T("Shift.VarianceLbpCol")],
                        Report.ShiftCloses.Select(x => new object?[] { x.ClosedAt, x.Cashier, x.Expected, x.Counted, x.Variance, x.VarianceLbp }));
                    break;
                case "lowstock" or "slow":
                    var stock = section == "lowstock" ? Alerts?.LowStock ?? [] : Alerts?.SlowMovers ?? [];
                    CsvExporter.Write(path, [Loc.T("Common.Item"), Loc.T("Labels.Variant"), Loc.T("Common.Sku"), Loc.T("Common.Stock"), Loc.T("Reports.ReorderAt"), Loc.T("Reports.ValueAtCost"), Loc.T("Reports.LastSold")],
                        stock.Select(x => new object?[] { x.Product, x.Variant, x.Sku, x.Stock, x.ReorderLevel, x.CostValue, x.LastSold }));
                    break;
                case "cashiers":
                    CsvExporter.Write(path, [Loc.T("Common.Cashier"), Loc.T("Reports.Transactions"), Loc.T("Common.Items"), Loc.T("Common.Total")], Report.ByCashier.Select(x => new object?[] { x.Name, x.Count, x.Quantity, x.Amount }));
                    break;
                default:
                    var rows = section switch
                    {
                        "categories" => Report.ByCategory,
                        "sizes" => Report.BySize,
                        _ => Report.TopProducts,
                    };
                    CsvExporter.Write(path, [Loc.T("Common.Name"), Loc.T("Reports.Units"), Loc.T("Reports.NetSales"), Loc.T("Reports.Cost"), Loc.T("Reports.Profit"), Loc.T("Reports.Margin")],
                        rows.Select(x => new object?[] { x.Name, x.Quantity, x.Amount, x.Cost, x.Profit, x.MarginPercent }));
                    break;
            }
            Dialogs.Toast(Loc.T("Reports.Exported"));
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.ExportFailed"), ex);
        }
    }

    [RelayCommand]
    private void PrintSummary()
    {
        if (Report is not { } r) return;
        string M(decimal v) => CurrencyFormat.Format(v);
        string Row(string l, string v) => $"{l,-28}{v,14}";
        var lines = new List<string>
        {
            settings.Current.StoreName.ToUpperInvariant(),
            Loc.T("Reports.SummaryHeader", r.From, r.To.AddDays(-1)),
            new('-', 42),
            Row(Loc.T("Reports.Transactions"), r.Transactions.ToString()),
            Row(Loc.T("Reports.ItemsSold"), r.ItemsSold.ToString()),
            Row(Loc.T("Reports.GrossSales"), M(r.GrossSales)),
            Row(Loc.T("Common.Discounts"), M(-r.Discounts)),
            Row(Loc.T("Reports.TaxCollected"), M(r.Tax)),
            Row(Loc.T("Reports.TotalSalesInclTax"), M(r.TotalSales)),
            Row(Loc.T("Shift.Refunds"), M(-r.Refunds)),
            Row(Loc.T("Reports.NetRevenueExTax"), M(r.NetRevenue)),
            Row(Loc.T("Reports.CostOfGoods"), M(r.CostOfGoods)),
            Row(Loc.T("Reports.GrossProfit"), M(r.GrossProfit)),
            Row(Loc.T("Reports.Margin"), $"{r.MarginPercent:0.0}%"),
            Row(Loc.T("Reports.AverageBasket"), M(r.AverageBasket)),
            new('-', 42),
            Loc.T("Common.Payments"),
        };
        lines.AddRange(r.ByPaymentMethod.Select(p => Row("  " + p.Name, M(p.Amount))));
        if (r.ByChannel.Any(c => c.Name != Loc.EnumText(Core.SalesChannel.InStore)))
        {
            lines.Add(new string('-', 42));
            lines.Add(Loc.T("Reports.Channels"));
            lines.AddRange(r.ByChannel.Select(c => Row($"  {c.Name} ({c.Count})", M(c.Amount))));
            lines.Add(Row("  " + Loc.T("History.DeliveryFee"), M(r.DeliveryFees)));
        }
        if (r.ReturnsCount > 0)
        {
            lines.Add(new string('-', 42));
            lines.Add(Loc.T("Reports.Returns"));
            lines.AddRange(r.RefundsByMethod.Select(m => Row($"  {m.Name} ({m.Count})", M(-m.Amount))));
        }
        if (r.Settlements.Count > 0 || DeliveryOwed > 0)
        {
            lines.Add(new string('-', 42));
            lines.Add(Loc.T("Reports.Deliveries"));
            lines.Add(Row("  " + Loc.T("Reports.DeliveryReceived"), M(DeliveryReceived)));
            lines.Add(Row("  " + Loc.T("Reports.DeliveryKept"), M(DeliveryKept)));
            lines.Add(Row("  " + Loc.T("Reports.DeliveryOwedNow"), M(DeliveryOwed)));
        }
        lines.Add(new string('-', 42));
        lines.Add(Loc.T("Reports.TopCategories"));
        lines.AddRange(r.ByCategory.Take(10).Select(c => Row($"  {c.Name} ({c.Quantity})", M(c.Amount))));
        lines.Add(new string('-', 42));
        lines.Add(Loc.T("Reports.TopProducts"));
        lines.AddRange(r.TopProducts.Take(10).Select(p => Row($"  {(p.Name.Length > 20 ? p.Name[..20] : p.Name)} ({p.Quantity})", M(p.Amount))));
        Dialogs.ShowDialog(new TextPreviewViewModel(Dialogs, print, Loc.T("Reports.SalesSummary"), lines));
    }
}
