using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Receipts;
using ClothingStore.Core.Security;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels;

/// <summary>Which sales to list: everything, in-store only, or online orders only.</summary>
public enum SaleSourceFilter
{
    All,
    InStore,
    Online,
}

public sealed partial class SalesHistoryViewModel(
    IDialogService dialogs, SalesService sales, SettingsService settings, Session session,
    PrintService print, INavigationService navigation) : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.SalesHistory");
    public bool CanVoid => session.Can(Permission.VoidSales);
    public bool CanReturn => session.Can(Permission.ProcessReturns);

    [ObservableProperty]
    public partial DateTime From { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial DateTime To { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    /// <summary>All, completed only, those with returns, or voided only (the summary boxes set it too).</summary>
    [ObservableProperty]
    public partial SaleListFilter Filter { get; set; } = SaleListFilter.All;

    /// <summary>Totals for the dates, search and source: sales, voided, returns and what's left (whatever the filter).</summary>
    [ObservableProperty]
    public partial SalesSummary Summary { get; set; } = new(0, 0, 0, 0, 0, 0);

    [ObservableProperty]
    public partial SaleSourceFilter Source { get; set; } = SaleSourceFilter.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnlineSummary), nameof(IsCapped), nameof(HasFooter))]
    public partial List<Sale> Sales { get; set; } = [];

    /// <summary>Only the newest sales are listed (and counted in the totals): the dates or search should be narrowed.</summary>
    public bool IsCapped => Sales.Count >= SalesService.HistoryLimit;

    /// <summary>The note under the list: online orders and/or "only the newest are shown".</summary>
    public bool HasFooter => IsCapped || OnlineSummary.Length > 0;
    public string CappedText => Loc.T("History.Capped", SalesService.HistoryLimit);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReprintCommand), nameof(VoidCommand), nameof(ReturnCommand))]
    public partial Sale? SelectedSale { get; set; }

    /// <summary>Shown over the list while the latest sales are being fetched (changing dates, search or filters).</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    private int _loads;
    private readonly LatestSearch _lines = new(TimeSpan.Zero);

    /// <summary>The list comes without items (much quicker): the sale opened gets its items now.</summary>
    partial void OnSelectedSaleChanged(Sale? value)
    {
        if (value is { Lines.Count: 0 }) _ = LoadLinesAsync(value);
    }

    /// <summary>A quick refresh shows nothing; only one still running after a moment covers the list.</summary>
    private async Task ShowLoadingIfSlowAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        if (_loads > 0) IsLoading = true;
    }

    private async Task LoadLinesAsync(Sale sale)
    {
        try
        {
            await _lines.RunNowAsync(ct => sales.GetLinesAsync(sale.Id, ct), lines =>
            {
                sale.Lines = lines;
                if (ReferenceEquals(SelectedSale, sale)) OnPropertyChanged(nameof(SelectedSale)); // the items table reads SelectedSale.Lines
            });
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("History.SearchFailed"), ex);
        }
    }

    /// <summary>"Online orders: 3 · $120.00" under the totals (empty when there are none).</summary>
    public string OnlineSummary
    {
        get
        {
            var online = Sales.Where(s => s.Status == SaleStatus.Completed && s.Channel != SalesChannel.InStore).ToList();
            return online.Count == 0 ? "" : Loc.T("History.OnlineSummary", online.Count, Converters.CurrencyFormat.Format(online.Sum(s => s.Total)));
        }
    }

    private readonly LatestSearch _search = new();

    public Task OnNavigatedToAsync() => SearchAsync();

    partial void OnSearchTextChanged(string value) => _ = LoadAsync(immediately: false);
    partial void OnFilterChanged(SaleListFilter value) => _ = SearchAsync();

    [RelayCommand]
    private void ShowOnly(SaleListFilter filter) => Filter = filter;
    partial void OnSourceChanged(SaleSourceFilter value) => _ = SearchAsync();
    partial void OnFromChanged(DateTime value) => _ = LoadAsync(immediately: false);
    partial void OnToChanged(DateTime value) => _ = LoadAsync(immediately: false);

    [RelayCommand]
    private Task SearchAsync() => LoadAsync(immediately: true);

    private async Task LoadAsync(bool immediately)
    {
        var (from, to) = From <= To ? (From.Date, To.Date) : (To.Date, From.Date);
        var text = SearchText;
        var filter = Filter;
        bool? online = Source switch { SaleSourceFilter.Online => true, SaleSourceFilter.InStore => false, _ => null };
        try
        {
            Func<CancellationToken, Task<(List<Sale> Found, SalesSummary Summary)>> load = async ct =>
            {
                // Counted, so an overtaken search finishing doesn't hide the indicator while the newest is still loading.
                _loads++;
                _ = ShowLoadingIfSlowAsync();
                try
                {
                    return (await sales.SearchAsync(from, to.AddDays(1), text, online: online, filter: filter, ct: ct),
                            await sales.GetSummaryAsync(from, to.AddDays(1), text, online, ct));
                }
                finally
                {
                    if (--_loads == 0) IsLoading = false;
                }
            };
            Action<(List<Sale> Found, SalesSummary Summary)> apply = result =>
            {
                Sales = result.Found;
                Summary = result.Summary;
                SelectedSale = Sales.FirstOrDefault();
            };
            await (immediately ? _search.RunNowAsync(load, apply) : _search.RunAsync(load, apply));
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("History.SearchFailed"), ex);
        }
    }

    [RelayCommand]
    private Task QuickRangeAsync(string range)
    {
        (From, To) = range switch
        {
            "yesterday" => (DateTime.Today.AddDays(-1), DateTime.Today.AddDays(-1)),
            "week" => (DateTime.Today.AddDays(-6), DateTime.Today),
            "month" => (new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today),
            _ => (DateTime.Today, DateTime.Today),
        };
        return SearchAsync();
    }

    private bool HasSelection() => SelectedSale is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ReprintAsync()
    {
        if (SelectedSale is not { } sale) return;
        if (sale.Lines.Count == 0 && !await RunAsync(async () => sale.Lines = await sales.GetLinesAsync(sale.Id))) return;
        var doc = ReceiptBuilder.FromSale(sale, settings.Current, isCopy: true);
        Dialogs.ShowDialog(new TextPreviewViewModel(Dialogs, print, Loc.T("History.ReceiptTitle", sale.ReceiptNumber),
            ReceiptFormatter.Format(doc, settings.Current.ReceiptWidth), logo: ReceiptLogo.For(settings.Current)));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task VoidAsync()
    {
        if (SelectedSale is not { } sale) return;
        if (!CanVoid)
        {
            Dialogs.Warning(Loc.T("History.OnlyManagersVoid"));
            return;
        }
        if (sale.Status == SaleStatus.Voided)
        {
            Dialogs.Warning(Loc.T("History.AlreadyVoided"));
            return;
        }
        var reason = Dialogs.Prompt(Loc.T("History.VoidSale"), Loc.T("History.VoidPrompt", sale.ReceiptNumber));
        if (reason is null) return;

        if (await RunAsync(() => sales.VoidSaleAsync(sale.Id, session.User.Id, reason)))
        {
            Dialogs.Toast(Loc.T("History.Voided", sale.ReceiptNumber));
            await SearchAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ReturnAsync()
    {
        if (SelectedSale is not { } sale) return;
        await navigation.NavigateToAsync<ReturnsViewModel>(vm => vm.LoadReceiptAsync(sale.ReceiptNumber));
    }

    [RelayCommand]
    private void Export()
    {
        var path = Dialogs.SaveFile(Loc.T("History.ExportTitle"), Loc.T("Common.CsvFilter"), $"sales_{From:yyyyMMdd}_{To:yyyyMMdd}.csv");
        if (path is null) return;
        try
        {
            CsvExporter.Write(path,
                [Loc.T("Common.Receipt"), Loc.T("Common.Date"), Loc.T("Common.Status"), Loc.T("History.Source"), Loc.T("Common.Cashier"), Loc.T("Common.Customer"),
                    Loc.T("Common.Items"), Loc.T("Common.Subtotal"), Loc.T("Common.Discount"), Loc.T("Common.Tax"), Loc.T("History.DeliveryFee"), Loc.T("Common.Total"),
                    Loc.T("History.ReturnedAmount"), Loc.T("History.Net"), Loc.T("Common.Payments")],
                Sales.Select(s => new object?[]
                {
                    s.ReceiptNumber, s.CreatedAt, Loc.EnumText(s.Status), Loc.EnumText(s.Channel), s.User?.FullName, s.Customer?.FullName, s.ItemCount,
                    s.Subtotal, s.DiscountTotal, s.TaxTotal, s.DeliveryFee, s.Total, s.RefundedTotal, s.NetTotal,
                    string.Join(" + ", s.Payments.Select(p => $"{Loc.EnumText(p.Method)} {p.Amount:0.00}")),
                }));
            Dialogs.Toast(Loc.T("Common.Exported", Sales.Count));
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.ExportFailed"), ex);
        }
    }
}
