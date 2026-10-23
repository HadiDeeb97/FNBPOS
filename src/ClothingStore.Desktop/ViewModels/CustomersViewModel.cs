using ClothingStore.Core.Entities;
using ClothingStore.Core.Security;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Converters;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.ViewModels;

public sealed partial class CustomersViewModel(IDialogService dialogs, CustomerService customers, Session session)
    : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.Customers");
    public bool CanAdjustCredit => session.Can(Permission.AdjustStoreCredit);
    public bool CanSeeSpending => session.Can(Permission.ViewCustomerSpending);

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool IncludeInactive { get; set; }

    /// <summary>States for the filter; the first entry ("All states") has Id 0.</summary>
    [ObservableProperty]
    public partial List<Region> Regions { get; set; } = [];

    [ObservableProperty]
    public partial Region? RegionFilter { get; set; }

    partial void OnRegionFilterChanged(Region? value) => _ = SearchAsync();

    [ObservableProperty]
    public partial List<Customer> Customers { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(AdjustCreditCommand))]
    [NotifyPropertyChangedFor(nameof(LifetimeSpend), nameof(VisitCount))]
    public partial Customer? SelectedCustomer { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LifetimeSpend), nameof(VisitCount))]
    public partial List<Sale> History { get; set; } = [];

    /// <summary>All completed purchases, net of refunds (from the list query, so not limited to the history shown).</summary>
    public decimal LifetimeSpend => CanSeeSpending ? SelectedCustomer?.TotalSpent ?? 0 : 0;
    public int VisitCount => SelectedCustomer?.Visits ?? 0;

    public async Task OnNavigatedToAsync()
    {
        try
        {
            var all = new Region { Id = 0, Name = Loc.T("Customers.AllStates"), NameAr = Loc.T("Customers.AllStates") };
            Regions = [all, .. await customers.GetRegionsAsync()];
            RegionFilter ??= all;
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Customers.SearchFailed"), ex);
        }
        await SearchAsync();
    }

    private readonly LatestSearch _search = new();

    partial void OnSearchTextChanged(string value) => _ = SearchAsync(immediately: false);
    partial void OnIncludeInactiveChanged(bool value) => _ = SearchAsync();

    async partial void OnSelectedCustomerChanged(Customer? value)
    {
        try
        {
            var history = value is null ? [] : await customers.GetPurchaseHistoryAsync(value.Id);
            if (ReferenceEquals(SelectedCustomer, value)) History = history; // a slower answer for another customer is dropped
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Customers.HistoryFailed"), ex);
        }
    }

    private async Task SearchAsync(bool immediately = true)
    {
        var selectedId = SelectedCustomer?.Id;
        var text = SearchText;
        var includeInactive = IncludeInactive;
        int? regionId = RegionFilter is { Id: > 0 } r ? r.Id : null;
        try
        {
            Func<CancellationToken, Task<List<Customer>>> load = ct => customers.SearchAsync(text, includeInactive, max: 500, regionId, ct);
            Action<List<Customer>> apply = found =>
            {
                Customers = found;
                SelectedCustomer = Customers.FirstOrDefault(c => c.Id == selectedId) ?? Customers.FirstOrDefault();
            };
            await (immediately ? _search.RunNowAsync(load, apply) : _search.RunAsync(load, apply));
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Customers.SearchFailed"), ex);
        }
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        var editor = new CustomerEditorViewModel(Dialogs, customers);
        if (!Dialogs.ShowDialog(editor)) return;
        await SearchAsync();
        SelectedCustomer = Customers.FirstOrDefault(c => c.Id == editor.Saved?.Id) ?? SelectedCustomer;
    }

    private bool HasSelection() => SelectedCustomer is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        if (SelectedCustomer is null) return;
        if (Dialogs.ShowDialog(new CustomerEditorViewModel(Dialogs, customers, SelectedCustomer))) await SearchAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task AdjustCreditAsync()
    {
        if (SelectedCustomer is not { } customer) return;
        if (!CanAdjustCredit)
        {
            Dialogs.Warning(Loc.T("Customers.OnlyManagersCredit"));
            return;
        }
        var amount = Dialogs.PromptDecimal(Loc.T("Customers.AdjustCredit"),
            Loc.T("Customers.AdjustCreditPrompt", customer.FullName, CurrencyFormat.Format(customer.StoreCredit)));
        if (amount is null or 0) return;
        if (await RunAsync(() => customers.AdjustStoreCreditAsync(customer.Id, amount.Value, session.User.Id)))
        {
            Dialogs.Toast(Loc.T("Customers.CreditAdjusted", customer.FullName));
            await SearchAsync();
        }
    }

    [RelayCommand]
    private void Export()
    {
        var path = Dialogs.SaveFile(Loc.T("Customers.ExportTitle"), Loc.T("Common.CsvFilter"), "customers.csv");
        if (path is null) return;
        try
        {
            var spending = CanSeeSpending;
            CsvExporter.Write(path,
                [Loc.T("Customers.FirstName"), Loc.T("Customers.LastName"), Loc.T("Common.Phone"), Loc.T("Common.Email"),
                    Loc.T("Customers.State"), Loc.T("Customers.Address"), .. (spending ? [Loc.T("Customers.TotalSpent")] : Array.Empty<string>()),
                    Loc.T("Customers.Visits"), Loc.T("Customers.LastVisit"),
                    Loc.T("Common.Points"), Loc.T("Customers.StoreCredit"), Loc.T("Customers.Since"), Loc.T("Common.Active")],
                Customers.Select(c => (object?[])
                [
                    c.FirstName, c.LastName, c.Phone, c.Email, c.Region?.DisplayName, c.Address, .. (spending ? [c.TotalSpent] : Array.Empty<object?>()),
                    c.Visits, c.LastVisit, c.LoyaltyPoints, c.StoreCredit, c.CreatedAt, c.IsActive,
                ]));
            Dialogs.Toast(Loc.T("Common.Exported", Customers.Count));
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.ExportFailed"), ex);
        }
    }
}
