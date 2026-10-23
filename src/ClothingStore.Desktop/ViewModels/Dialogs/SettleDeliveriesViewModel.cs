using System.Globalization;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Pricing;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Converters;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

/// <summary>How much the delivery company handed over for the selected orders, and how.</summary>
public sealed partial class SettleDeliveriesViewModel : DialogViewModelBase
{
    private readonly StoreSettings _settings;

    public SettleDeliveriesViewModel(IDialogService dialogs, StoreSettings settings, IReadOnlyList<DeliveryRow> rows, bool hasOpenShift, bool canTransfer = true) : base(dialogs)
    {
        CanTransfer = canTransfer;
        _settings = settings;
        Orders = rows.Count;
        Expected = rows.Sum(r => r.Owed);
        Couriers = string.Join(", ", rows.Select(r => r.Courier ?? Loc.T("Deliveries.NoCourier")).Distinct());
        HasOpenShift = hasOpenShift;
        Method = hasOpenShift || !canTransfer ? SettlementMethod.Cash : SettlementMethod.Transfer;
        ReceivedText = Expected.ToString("0.00", CultureInfo.CurrentCulture);
    }

    public override string Title => Loc.T("Deliveries.ReceiveTitle");
    public int Orders { get; }
    public decimal Expected { get; }
    public string Couriers { get; }
    public bool HasOpenShift { get; }

    /// <summary>Transfers bring no cash into a drawer: managers only.</summary>
    public bool CanTransfer { get; }
    public decimal Rate => _settings.ActiveLbpRate;
    public bool LbpEnabled => Rate > 0;
    public decimal ExpectedLbp => Lbp.ToPay(Expected, Rate, _settings.LbpRounding);
    public string Summary => Loc.T("Deliveries.ReceiveSummary", Orders, Couriers, CurrencyFormat.Format(Expected));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLbp), nameof(AmountLabel), nameof(DifferenceText))]
    public partial SettlementMethod Method { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifferenceText))]
    public partial string ReceivedText { get; set; }

    [ObservableProperty]
    public partial string? Reference { get; set; }

    public bool IsLbp => Method == SettlementMethod.CashLbp;
    public string AmountLabel => IsLbp ? Loc.T("Deliveries.ReceivedLbp") : Loc.T("Deliveries.ReceivedUsd");

    /// <summary>"The company kept $2.00" when less came in than was owed.</summary>
    public string DifferenceText
    {
        get
        {
            if (!TryParse(out var amount)) return "";
            var usd = IsLbp && Rate > 0 ? Money.Round(amount / Rate) : amount;
            var diff = usd - Expected;
            return diff switch
            {
                < 0 => Loc.T("Deliveries.Short", CurrencyFormat.Format(-diff)),
                > 0 => Loc.T("Deliveries.Over", CurrencyFormat.Format(diff)),
                _ => "",
            };
        }
    }

    /// <summary>Dollars, or pounds for LBP cash.</summary>
    public decimal Received { get; private set; }

    partial void OnMethodChanged(SettlementMethod value) =>
        ReceivedText = value == SettlementMethod.CashLbp
            ? ExpectedLbp.ToString("#,0", CultureInfo.CurrentCulture)
            : Expected.ToString("0.00", CultureInfo.CurrentCulture);

    [RelayCommand]
    private void Save()
    {
        if (!TryParse(out var amount) || amount < 0)
        {
            Dialogs.Warning(Loc.T("Payment.EnterAmount"));
            return;
        }
        if (Method != SettlementMethod.Transfer && !HasOpenShift)
        {
            Dialogs.Warning(Loc.T("Err.SettleCashNeedsShift"));
            return;
        }
        Received = amount;
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private bool TryParse(out decimal value) =>
        decimal.TryParse(ReceivedText, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ||
        decimal.TryParse(ReceivedText, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
