using ClothingStore.Core.Security;
using ClothingStore.Core;
using ClothingStore.Core.Localization;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Converters;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels;

public enum DeliveryFilter
{
    Owed,
    Paid,
    All,
}

/// <summary>One order in the Deliveries list, with a tick box for "the company paid this one".</summary>
public sealed partial class DeliveryItemViewModel(DeliveryRow row, bool isChecked, Action<DeliveryItemViewModel> changed) : ObservableObject
{
    public DeliveryRow Row { get; } = row;

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked && !row.IsPaid;

    public bool CanCheck => !Row.IsPaid;

    partial void OnIsCheckedChanged(bool value)
    {
        if (value && Row.IsPaid) IsChecked = false;
        else changed(this);
    }
}

/// <summary>
/// Online orders paid through a delivery company: what each company still owes, and recording its payments
/// (cash into the drawer, or a transfer). Orders are ticked by hand or by scanning the invoice barcode on the
/// company's slip; ticks are kept while filtering or searching.
/// </summary>
public sealed partial class DeliveriesViewModel(
    IDialogService dialogs, DeliveryService deliveries, SettingsService settings, Session session, INavigationService navigation)
    : ViewModelBase(dialogs), IPageViewModel
{
    private readonly LatestSearch _search = new();

    /// <summary>Ticked orders by sale id (kept across refreshes and filters).</summary>
    private readonly Dictionary<int, DeliveryRow> _checked = [];

    public string Title => Loc.T("Nav.Deliveries");

    [ObservableProperty]
    public partial DeliveryFilter Filter { get; set; } = DeliveryFilter.Owed;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    /// <summary>Scanner / keyboard box: an invoice number or receipt number ticks that order.</summary>
    [ObservableProperty]
    public partial string ScanText { get; set; } = "";

    /// <summary>Result of the last scan, shown under the box.</summary>
    [ObservableProperty]
    public partial string? ScanMessage { get; set; }

    [ObservableProperty]
    public partial bool ScanFailed { get; set; }

    /// <summary>Company filter; null = every company.</summary>
    [ObservableProperty]
    public partial string? Courier { get; set; }

    [ObservableProperty]
    public partial List<string> Couriers { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial List<DeliveryItemViewModel> Rows { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalOwed), nameof(HasBalances))]
    public partial List<CourierBalance> Balances { get; set; } = [];

    public bool HasRows => Rows.Count > 0;
    public bool HasBalances => Balances.Count > 0;
    public decimal TotalOwed => Balances.Sum(b => b.Owed);

    public string SelectionText => _checked.Count == 0
        ? Loc.T("Deliveries.SelectHint")
        : Loc.T("Deliveries.Selected", _checked.Count, CurrencyFormat.Format(_checked.Values.Sum(r => r.Owed)));

    public async Task OnNavigatedToAsync() => await LoadAsync();

    partial void OnFilterChanged(DeliveryFilter value) => _ = LoadAsync();
    partial void OnCourierChanged(string? value) => _ = LoadAsync();
    partial void OnSearchTextChanged(string value) => _ = LoadAsync(immediately: false);

    // Parameterless on purpose: a command with a bool parameter is disabled when the button passes none.
    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    public bool CanReturn => session.Can(Permission.ProcessReturns);

    /// <summary>Opens Returns with this order loaded (for an order that came back, or a customer returning items).</summary>
    [RelayCommand]
    private Task ReturnAsync(DeliveryItemViewModel? item) =>
        item is null || !CanReturn
            ? Task.CompletedTask
            : navigation.NavigateToAsync<ReturnsViewModel>(vm => vm.LoadReceiptAsync(item.Row.ReceiptNumber));

    /// <summary>Delivery companies and drivers with their details.</summary>
    [RelayCommand]
    private async Task ManagePartnersAsync()
    {
        if (Dialogs.ShowDialog(new DeliveryPartnersViewModel(Dialogs, deliveries)))
            await LoadAsync();
    }

    private async Task LoadAsync(bool immediately = true)
    {
        bool? paid = Filter switch { DeliveryFilter.Owed => false, DeliveryFilter.Paid => true, _ => null };
        var courier = string.IsNullOrWhiteSpace(Courier) ? null : Courier;
        var text = SearchText;
        try
        {
            Func<CancellationToken, Task<(List<DeliveryRow>, List<CourierBalance>, List<string>)>> load = async ct =>
                (await deliveries.GetAsync(paid, courier, text, ct: ct), await deliveries.GetBalancesAsync(ct), await deliveries.GetCouriersAsync(ct));
            Action<(List<DeliveryRow> Rows, List<CourierBalance> Balances, List<string> Couriers)> apply = r =>
            {
                Rows = r.Rows.Select(Item).ToList();
                Balances = r.Balances;
                Couriers = r.Couriers;
            };
            await (immediately ? _search.RunNowAsync(load, apply) : _search.RunAsync(load, apply));
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.SomethingWentWrong"), ex);
        }
    }

    private DeliveryItemViewModel Item(DeliveryRow row)
    {
        // A ticked order keeps its tick; its stored row is refreshed (e.g. owed less after a return).
        var isChecked = _checked.ContainsKey(row.SaleId) && !row.IsPaid;
        if (isChecked) _checked[row.SaleId] = row;
        else _checked.Remove(row.SaleId);
        return new DeliveryItemViewModel(row, isChecked, OnItemChecked);
    }

    private void OnItemChecked(DeliveryItemViewModel item)
    {
        if (item.IsChecked) _checked[item.Row.SaleId] = item.Row;
        else _checked.Remove(item.Row.SaleId);
        OnPropertyChanged(nameof(SelectionText));
        ReceivePaymentCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Enter in the scan box (a barcode scanner sends Enter after the code).</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        var code = ScanText.Trim();
        ScanText = "";
        if (code.Length == 0) return;

        var item = Rows.FirstOrDefault(r =>
            string.Equals(r.Row.DeliveryReference, code, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Row.ReceiptNumber, code, StringComparison.OrdinalIgnoreCase));

        DeliveryRow? row = item?.Row;
        if (row is null)
        {
            try
            {
                row = await deliveries.FindAsync(code);
            }
            catch (Exception ex)
            {
                Dialogs.Error(Loc.T("Common.SomethingWentWrong"), ex);
                return;
            }
        }

        if (row is null)
        {
            Report(Loc.T("Deliveries.ScanNotFound", code), failed: true);
            return;
        }
        if (row.IsPaid)
        {
            Report(Loc.T("Deliveries.ScanAlreadyPaid", code, row.SettledAt?.ToString("d") ?? ""), failed: true);
            return;
        }

        if (item is null)
        {
            // Not in the list (filtered out): tick it anyway, then show everything that is owed so it can be seen.
            _checked[row.SaleId] = row;
            SearchText = "";
            Courier = null;
            Filter = DeliveryFilter.Owed;
            await RefreshAsync();
        }
        else
        {
            item.IsChecked = true;
        }
        OnPropertyChanged(nameof(SelectionText));
        ReceivePaymentCommand.NotifyCanExecuteChanged();
        Report(Loc.T("Deliveries.ScanAdded", code, row.Courier ?? Loc.T("Deliveries.NoCourier"), CurrencyFormat.Format(row.Owed)), failed: false);
    }

    private void Report(string message, bool failed)
    {
        ScanMessage = message;
        ScanFailed = failed;
    }

    [RelayCommand]
    private void CheckAllShown()
    {
        foreach (var item in Rows.Where(r => r.CanCheck)) item.IsChecked = true;
    }

    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var item in Rows) item.IsChecked = false;
        _checked.Clear();
        ScanMessage = null;
        OnPropertyChanged(nameof(SelectionText));
        ReceivePaymentCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ShowCourier(CourierBalance balance)
    {
        Filter = DeliveryFilter.Owed;
        Courier = balance.Courier.Length == 0 ? null : balance.Courier;
    }

    [RelayCommand]
    private void ClearCourier() => Courier = null;

    private bool CanReceive() => _checked.Count > 0;

    /// <summary>The delivery company paid for the ticked orders.</summary>
    [RelayCommand(CanExecute = nameof(CanReceive))]
    private async Task ReceivePaymentAsync()
    {
        var rows = _checked.Values.ToList();
        if (rows.Count == 0) return;
        var dialog = new SettleDeliveriesViewModel(Dialogs, settings.Current, rows, session.HasOpenShift, session.Can(Permission.SettleByTransfer));
        if (!Dialogs.ShowDialog(dialog)) return;

        var ok = await RunAsync(() => deliveries.SettleAsync(new SettleDeliveriesRequest
        {
            SaleIds = rows.Select(r => r.SaleId).ToList(),
            UserId = session.User.Id,
            ShiftId = session.CurrentShift?.Id,
            Method = dialog.Method,
            Received = dialog.Received,
            ExchangeRate = dialog.Rate,
            Reference = dialog.Reference,
        }));
        if (!ok)
        {
            try { await settings.RefreshCurrencyAsync(); } catch { /* checked again next time */ }
            return;
        }
        if (dialog.Method != SettlementMethod.Transfer) CashDrawer.OpenFor(DrawerReason.CashMovement); // courier cash goes in
        _checked.Clear();
        ScanMessage = null;
        OnPropertyChanged(nameof(SelectionText));
        ReceivePaymentCommand.NotifyCanExecuteChanged();
        Dialogs.Toast(Loc.T("Deliveries.Recorded", rows.Count));
        await RefreshAsync();
    }
}
