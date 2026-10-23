using System.Collections.ObjectModel;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Receipts;
using ClothingStore.Core.Security;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.ViewModels;

public sealed partial class ReturnLineViewModel(SaleLine line) : ObservableObject
{
    public SaleLine Line { get; } = line;
    public decimal UnitRefund => Line.Quantity == 0 ? 0 : Money.Round(Line.LineTotal / Line.Quantity);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedRefund))]
    public partial int ReturnQuantity { get; set; }

    [ObservableProperty]
    public partial bool Restock { get; set; } = true;

    public decimal EstimatedRefund => UnitRefund * ReturnQuantity;

    partial void OnReturnQuantityChanged(int value)
    {
        if (value < 0) ReturnQuantity = 0;
        else if (value > Line.ReturnableQuantity) ReturnQuantity = Line.ReturnableQuantity;
    }
}

/// <summary>Currency for the cash part of a refund.</summary>
public enum RefundCashChoice
{
    /// <summary>Dollars back as dollars, pounds back as pounds.</summary>
    AsPaid,
    Usd,
    Lbp,
}

/// <summary>Refunds or exchanges: look up a receipt, choose items, refund to the original payment or store credit.</summary>
public sealed partial class ReturnsViewModel(
    IDialogService dialogs, SalesService sales, ReturnService returns, SettingsService settings,
    UserService users, Session session, PrintService print) : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.Returns");

    public string[] ReasonPresets { get; } =
        [Loc.T("Returns.Reason.WrongSize"), Loc.T("Returns.Reason.ChangedMind"), Loc.T("Returns.Reason.Defective"),
         Loc.T("Returns.Reason.NotAsDescribed"), Loc.T("Returns.Reason.Gift"), Loc.T("Returns.Reason.Exchange")];

    [ObservableProperty]
    public partial string ReceiptNumber { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSale), nameof(CanRefundToCredit), nameof(IsOutsideWindow), nameof(WindowMessage),
        nameof(PaidWith), nameof(CanRefundInCash), nameof(HasUnpaidDelivery))]
    public partial Sale? Sale { get; set; }

    /// <summary>Paid through a delivery company that hasn't paid the store yet: ask who has the money.</summary>
    public bool HasUnpaidDelivery => Sale is { DeliverySettlementId: null } s && s.Payments.Any(p => p.Method == PaymentMethod.Delivery);

    /// <summary>
    /// The customer already paid the courier: refunded at the counter, the courier still owes it all. Off: the order
    /// came back unpaid, so the courier just owes less.
    /// </summary>
    [ObservableProperty]
    public partial bool CustomerPaidCourier { get; set; }

    public ObservableCollection<ReturnLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    public partial RefundDestination RefundTo { get; set; } = RefundDestination.OriginalPayment;

    [ObservableProperty]
    public partial RefundCashChoice RefundCashIn { get; set; } = RefundCashChoice.AsPaid;

    public bool ShowLbp => settings.Current.ActiveLbpRate > 0;

    private CashCurrency? RefundCurrency => RefundCashIn switch
    {
        RefundCashChoice.Usd => CashCurrency.Usd,
        RefundCashChoice.Lbp => CashCurrency.Lbp,
        _ => null,
    };

    [ObservableProperty]
    public partial string? Reason { get; set; }

    [ObservableProperty]
    public partial decimal EstimatedRefund { get; private set; }

    public bool HasSale => Sale is not null;
    public bool CanRefundToCredit => Sale?.CustomerId is not null;

    /// <summary>Only offered when part of the sale was paid by card or wallet.</summary>
    public bool CanRefundInCash => Sale?.Payments.Any(p => p.Method is PaymentMethod.Card or PaymentMethod.MobileWallet) == true;

    public string PaidWith => Sale is null ? "" : Loc.T("Returns.PaidWith", string.Join(", ", Sale.Payments
        .GroupBy(p => p.Method)
        .Select(g => $"{Loc.EnumText(g.Key)} {Converters.CurrencyFormat.Format(g.Sum(p => p.Amount))}")));

    public bool IsOutsideWindow =>
        Sale is not null && settings.Current.ReturnWindowDays > 0 &&
        (DateTime.Now - Sale.CreatedAt).TotalDays > settings.Current.ReturnWindowDays;

    public string WindowMessage => IsOutsideWindow
        ? Loc.T("Returns.OutsideWindow", settings.Current.ReturnWindowDays)
        : "";

    /// <summary>
    /// A receipt still open from the last visit is loaded again: items may have been returned or the sale voided since,
    /// and refunding from the old figures would be wrong.
    /// </summary>
    public async Task OnNavigatedToAsync()
    {
        if (Sale is not { } shown) return;
        try
        {
            var sale = await sales.GetByReceiptAsync(shown.ReceiptNumber);
            if (sale is null || sale.Status == SaleStatus.Voided) Clear();
            else Show(sale);
        }
        catch (Exception ex)
        {
            Clear();
            ReportLoadError(Loc.T("Returns.LoadFailed"), ex);
        }
    }

    public async Task LoadReceiptAsync(string receiptNumber)
    {
        ReceiptNumber = receiptNumber;
        await FindAsync();
    }

    [RelayCommand]
    private Task FindAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(ReceiptNumber)) return;
        var sale = await sales.GetByReceiptAsync(ReceiptNumber);
        if (sale is null)
        {
            Clear();
            Dialogs.Warning(Loc.T("Returns.NotFound", ReceiptNumber.Trim().ToUpperInvariant()));
            return;
        }
        if (sale.Status == SaleStatus.Voided)
        {
            Clear();
            Dialogs.Warning(Loc.T("Returns.Voided"));
            return;
        }
        Show(sale);
    });

    private void Show(Sale sale)
    {
        foreach (var l in Lines) l.PropertyChanged -= OnLineChanged;
        Lines.Clear();
        Sale = sale;
        foreach (var line in sale.Lines)
        {
            var vm = new ReturnLineViewModel(line);
            vm.PropertyChanged += OnLineChanged;
            Lines.Add(vm);
        }
        RefundTo = RefundDestination.OriginalPayment;
        RefundCashIn = RefundCashChoice.AsPaid;
        CustomerPaidCourier = false;
        OnPropertyChanged(nameof(ShowLbp));
        Reason = null;
        UpdateTotal();
    }

    private void OnLineChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateTotal();

    private void UpdateTotal() => EstimatedRefund = Lines.Sum(l => l.EstimatedRefund);

    [RelayCommand]
    private void ReturnAll()
    {
        foreach (var line in Lines) line.ReturnQuantity = line.Line.ReturnableQuantity;
    }

    [RelayCommand]
    private void Clear()
    {
        foreach (var l in Lines) l.PropertyChanged -= OnLineChanged;
        Lines.Clear();
        Sale = null;
        Reason = null;
        EstimatedRefund = 0;
    }

    [RelayCommand]
    private async Task ProcessAsync()
    {
        if (Sale is not { } sale) return;
        var selected = Lines.Where(l => l.ReturnQuantity > 0).ToList();
        if (selected.Count == 0)
        {
            Dialogs.Warning(Loc.T("Returns.SetQuantity"));
            return;
        }
        if (RefundTo == RefundDestination.StoreCredit && !CanRefundToCredit)
        {
            Dialogs.Warning(Loc.T("Returns.CreditNeedsCustomer"));
            return;
        }

        var lines = selected.Select(l => new ReturnLineRequest(l.Line.Id, l.ReturnQuantity, l.Restock)).ToList();
        RefundPlan? plan = null;
        if (!await RunAsync(async () => plan = await returns.PlanAsync(sale.Id, lines, RefundTo, RefundCurrency, HasUnpaidDelivery && CustomerPaidCourier)) || plan is null) return;

        if ((plan.CashOut > 0 || plan.CashOutLbp > 0) && !session.HasOpenShift)
        {
            Dialogs.Warning(Loc.T("Returns.OpenShiftForCash"));
            return;
        }

        int? approvedBy = null;
        var needsWindowApproval = IsOutsideWindow && !session.Can(Permission.OverrideDiscountLimit);
        var needsCashApproval = plan.NeedsCashOverride && !session.Can(Permission.OverrideRefundMethod);
        if (needsWindowApproval || needsCashApproval)
        {
            var reasons = new List<string>();
            if (needsWindowApproval) reasons.Add(WindowMessage);
            if (needsCashApproval) reasons.Add(Loc.T("Returns.CashOverrideNeedsApproval"));
            var approval = new ManagerApprovalViewModel(Dialogs, users, string.Join("\n", reasons),
                needsCashApproval ? Permission.OverrideRefundMethod : Permission.OverrideDiscountLimit);
            if (!Dialogs.ShowDialog(approval) || approval.ApprovedBy is null) return;
            approvedBy = approval.ApprovedBy.Id;
        }

        var breakdown = plan.Shares
            .GroupBy(s => s.Method)
            .OrderBy(g => g.Key)
            .Select(g => g.Key == RefundMethod.CashLbp
                ? $"  {Loc.EnumText(g.Key)}: {Converters.CurrencyFormat.Lbp(plan.CashOutLbp)} ({Converters.CurrencyFormat.Format(g.Sum(s => s.Amount))})"
                : $"  {Loc.EnumText(g.Key)}: {Converters.CurrencyFormat.Format(g.Sum(s => s.Amount))}");
        if (!Dialogs.Confirm(Loc.T("Returns.Confirm", Converters.CurrencyFormat.Format(plan.Total), string.Join("\n", breakdown))))
            return;

        SaleReturn? result = null;
        var ok = await RunAsync(async () =>
        {
            result = await returns.ProcessReturnAsync(new ReturnRequest
            {
                SaleId = sale.Id,
                UserId = session.User.Id,
                ShiftId = session.CurrentShift?.Id,
                RefundTo = RefundTo,
                Reason = Reason,
                ApprovedByUserId = approvedBy,
                Lines = lines,
                CashCurrency = RefundCurrency,
                ExchangeRate = plan.Rate,
                CustomerPaidCourier = HasUnpaidDelivery && CustomerPaidCourier,
            });
        });
        if (!ok || result is null)
        {
            try { await settings.RefreshCurrencyAsync(); } catch { /* checked again on the next try */ }
            return;
        }

        if (result.Refunds.Any(r => r.Method is RefundMethod.Cash or RefundMethod.CashLbp)) CashDrawer.OpenFor(DrawerReason.CashRefund);
        var doc = ReceiptBuilder.FromReturn(result, sale, settings.Current, session.User.FullName);
        Dialogs.ShowDialog(new TextPreviewViewModel(Dialogs, print, Loc.T("Returns.RefundTitle", result.ReturnNumber),
            ReceiptFormatter.Format(doc, settings.Current.ReceiptWidth), copies: LocalPreferences.Current.ReceiptCopies,
            logo: ReceiptLogo.For(settings.Current)));

        if (result.Refunds.Any(r => r.Method == RefundMethod.StoreCredit))
            Dialogs.Toast(Loc.T("Returns.CreditAdded"));

        await FindAsync(); // refresh remaining returnable quantities
    }
}
