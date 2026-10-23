using System.Collections.ObjectModel;
using System.Globalization;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Pricing;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Converters;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

public sealed partial class DiscountViewModel(IDialogService dialogs, string title, decimal baseAmount, DiscountType type, decimal value)
    : DialogViewModelBase(dialogs)
{
    public override string Title { get; } = title;
    public decimal BaseAmount { get; } = baseAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    public partial DiscountType DiscountType { get; set; } = type == DiscountType.None ? DiscountType.Percent : type;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    public partial string ValueText { get; set; } = value == 0 ? "" : value.ToString("0.##");

    public decimal Value { get; private set; }

    public string Preview => TryParse(out var v)
        ? Loc.T("Discount.Preview", CurrencyFormat.Format(Core.Pricing.CartCalculator.ResolveDiscount(BaseAmount, DiscountType, v)), CurrencyFormat.Format(BaseAmount))
        : "";

    public decimal[] QuickPercents { get; } = [5, 10, 15, 20, 25, 50];

    [RelayCommand]
    private void QuickPercent(decimal percent)
    {
        DiscountType = DiscountType.Percent;
        ValueText = percent.ToString("0.##");
    }

    [RelayCommand]
    private void Apply()
    {
        if (!TryParse(out var v) || v < 0 || (DiscountType == DiscountType.Percent && v > 100))
        {
            Dialogs.Warning(Loc.T("Discount.Invalid"));
            return;
        }
        Value = v;
        if (v == 0) DiscountType = DiscountType.None;
        Close(true);
    }

    [RelayCommand]
    private void Remove()
    {
        DiscountType = DiscountType.None;
        Value = 0;
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private bool TryParse(out decimal v) =>
        decimal.TryParse(ValueText, NumberStyles.Number, CultureInfo.CurrentCulture, out v) ||
        decimal.TryParse(ValueText, NumberStyles.Number, CultureInfo.InvariantCulture, out v);
}

public sealed partial class TenderViewModel(PaymentMethod method, decimal amount, string? reference) : ObservableObject
{
    public PaymentMethod Method { get; } = method;
    public decimal Amount { get; } = amount;
    public string? Reference { get; } = reference;
}

public sealed record QuickCash(string Label, decimal Amount);

/// <summary>
/// Tender screen. Cash is typed straight into a dollar box and a pound box (customers often pay with both);
/// card, wallet, store credit and points are added as separate tenders. Change can go back in dollars, pounds or both.
/// </summary>
public sealed partial class PaymentViewModel : DialogViewModelBase
{
    private readonly StoreSettings _settings;
    private CashSettlement _cash = new();

    public PaymentViewModel(IDialogService dialogs, decimal total, Customer? customer, StoreSettings settings, bool allowDelivery = false) : base(dialogs)
    {
        AllowDelivery = allowDelivery;
        Total = total;
        Customer = customer;
        _settings = settings;
        Rate = settings.ActiveLbpRate;
        Rounding = Math.Max(1, settings.LbpRounding);
        ChangeIn = LbpEnabled ? ChangeCurrency.Mixed : ChangeCurrency.Usd;
        Tenders.CollectionChanged += (_, _) => OnTendersChanged();
        OnTendersChanged();
    }

    public override string Title => Loc.T("Payment.Title");
    public decimal Total { get; }

    /// <summary>Online orders can be paid through the delivery company, which pays the store later.</summary>
    public bool AllowDelivery { get; }
    public Customer? Customer { get; }

    /// <summary>LBP per dollar shown to the customer; sent with the sale so a rate change in between is caught.</summary>
    public decimal Rate { get; }
    public int Rounding { get; }
    public bool LbpEnabled => Rate > 0;
    public decimal TotalLbp => Lbp.ToPay(Total, Rate, Rounding);
    public string RateText => LbpEnabled ? Loc.T("Rate.Short", Rate.ToString("N0")) : "";

    /// <summary>Card, wallet, store credit and points.</summary>
    public ObservableCollection<TenderViewModel> Tenders { get; } = [];
    public decimal NonCashPaid => Tenders.Sum(t => t.Amount);
    public decimal CashDue => Math.Max(0, Total - NonCashPaid);
    public decimal CashDueLbp => Lbp.ToPay(CashDue, Rate, Rounding);

    // ---- Cash ------------------------------------------------------------------------------

    [ObservableProperty]
    public partial string UsdText { get; set; } = "";

    [ObservableProperty]
    public partial string LbpText { get; set; } = "";

    [ObservableProperty]
    public partial ChangeCurrency ChangeIn { get; set; }

    public decimal TenderedUsd => ParseOrZero(UsdText);
    public decimal TenderedLbp => Math.Round(ParseOrZero(LbpText), 0, MidpointRounding.AwayFromZero);

    /// <summary>What the pounds typed are worth in dollars, under the LBP box.</summary>
    public string LbpWorth => LbpEnabled && TenderedLbp > 0 ? "≈ " + CurrencyFormat.Format(Money.Round(TenderedLbp / Rate)) : "";

    public bool IsFullyPaid => _cash.IsCovered;
    public decimal RemainingUsd => _cash.ShortUsd;
    public decimal RemainingLbp => _cash.ShortLbp;
    public bool HasRemaining => !_cash.IsCovered;
    public decimal ChangeUsd => _cash.ChangeUsd;
    public decimal ChangeLbp => _cash.ChangeLbp;
    public bool HasChange => _cash.IsCovered && (ChangeUsd > 0 || ChangeLbp > 0);

    /// <summary>
    /// Change currency to send with the sale. When no pounds change hands it is plain dollars (same result), so a rate
    /// change on another till doesn't stop a dollar-only sale.
    /// </summary>
    public ChangeCurrency EffectiveChangeIn => LbpEnabled && (TenderedLbp > 0 || ChangeLbp > 0) ? ChangeIn : ChangeCurrency.Usd;

    /// <summary>"$28.00 + 44,000 LBP", or just one of them.</summary>
    public string ChangeText => FormatPair(ChangeUsd, ChangeLbp);
    public string RemainingText => LbpEnabled
        ? Loc.T("Payment.RemainingPair", CurrencyFormat.Format(RemainingUsd), CurrencyFormat.Lbp(RemainingLbp))
        : CurrencyFormat.Format(RemainingUsd);

    [ObservableProperty]
    public partial List<QuickCash> QuickUsd { get; set; } = [];

    [ObservableProperty]
    public partial List<QuickCash> QuickLbp { get; set; } = [];

    partial void OnUsdTextChanged(string value) => RefreshCash();
    partial void OnLbpTextChanged(string value) => RefreshCash();
    partial void OnChangeInChanged(ChangeCurrency value) => RefreshCash();

    // ---- Cashier's own split of the change ("I only have $20") -----------------------------
    // Typing dollars to give back works out the rest in pounds, and the other way round. Empty: change follows "Change in".

    private enum SplitSide { None, Usd, Lbp }
    private SplitSide _split;
    private bool _fillingSplit;

    [ObservableProperty]
    public partial string GiveUsdText { get; set; } = "";

    [ObservableProperty]
    public partial string GiveLbpText { get; set; } = "";

    /// <summary>Why the split typed can't be used (more than the change, cents...); empty when fine.</summary>
    [ObservableProperty]
    public partial string SplitError { get; set; } = "";

    /// <summary>Dollars the cashier chose to give back, sent with the sale (null when not splitting).</summary>
    public decimal? GiveChangeUsd => LbpEnabled && _split == SplitSide.Usd ? ParseOrZero(GiveUsdText) : null;

    /// <summary>Pounds the cashier chose to give back, sent with the sale (null when not splitting).</summary>
    public decimal? GiveChangeLbp => LbpEnabled && _split == SplitSide.Lbp ? Math.Round(ParseOrZero(GiveLbpText), 0, MidpointRounding.AwayFromZero) : null;

    /// <summary>Greyed hints in the split boxes: the change as it would be given without a split.</summary>
    public string GiveUsdHint => ChangeUsd.ToString("0", CultureInfo.CurrentCulture);
    public string GiveLbpHint => ChangeLbp.ToString("N0", CultureInfo.CurrentCulture);

    partial void OnGiveUsdTextChanged(string value) => OnSplitTyped(SplitSide.Usd, value);
    partial void OnGiveLbpTextChanged(string value) => OnSplitTyped(SplitSide.Lbp, value);

    private void OnSplitTyped(SplitSide side, string value)
    {
        if (_fillingSplit) return;
        if (!string.IsNullOrWhiteSpace(value)) _split = side;
        else if (_split == side)
        {
            _split = SplitSide.None;
            FillSplit("", ""); // the other box only showed the worked-out amount
        }
        RefreshCash();
    }

    /// <summary>Shows the worked-out side of the split (or clears both boxes) without counting as typing.</summary>
    private void FillSplit(string usd, string lbp)
    {
        _fillingSplit = true;
        try
        {
            GiveUsdText = usd;
            GiveLbpText = lbp;
        }
        finally
        {
            _fillingSplit = false;
        }
    }

    [RelayCommand]
    private void SetUsd(QuickCash option) => UsdText = option.Amount.ToString("0.##", CultureInfo.CurrentCulture);

    [RelayCommand]
    private void SetLbp(QuickCash option) => LbpText = option.Amount.ToString("#,0", CultureInfo.CurrentCulture);

    [RelayCommand]
    private void ClearCash()
    {
        _split = SplitSide.None;
        FillSplit("", "");
        UsdText = "";
        LbpText = "";
    }

    // ---- Card, wallet, credit, points ------------------------------------------------------

    public decimal StoreCreditAvailable => Customer?.StoreCredit ?? 0;
    public decimal LoyaltyValueAvailable => Customer is null ? 0 : Money.Round(Math.Floor(Customer.LoyaltyPoints * _settings.LoyaltyPointValue * 100) / 100);
    public bool CanUseStoreCredit => StoreCreditAvailable > 0;
    public bool CanUseLoyalty => LoyaltyValueAvailable > 0 && _settings.LoyaltyPointValue > 0;
    public string LoyaltyInfo => Customer is null ? "" : Loc.T("Payment.LoyaltyInfo", Customer.LoyaltyPoints, CurrencyFormat.Format(LoyaltyValueAvailable));

    /// <summary>Dollar amount for the next card / wallet tender.</summary>
    [ObservableProperty]
    public partial string AmountText { get; set; } = "";

    [ObservableProperty]
    public partial string? Reference { get; set; }

    public IReadOnlyList<PaymentInput> Payments
    {
        get
        {
            var list = Tenders.Select(t => new PaymentInput(t.Method, t.Amount, t.Reference)).ToList();
            if (TenderedUsd > 0) list.Add(new PaymentInput(PaymentMethod.Cash, Money.Round(TenderedUsd)));
            if (TenderedLbp > 0) list.Add(new PaymentInput(PaymentMethod.CashLbp, TenderedLbp));
            return list;
        }
    }

    [RelayCommand]
    private void AddTender(PaymentMethod method)
    {
        if (!TryParseAmount(out var amount) || amount <= 0)
        {
            Dialogs.Warning(Loc.T("Payment.EnterAmount"));
            return;
        }
        if (CashDue == 0)
        {
            Dialogs.Warning(Loc.T("Payment.AlreadyPaid"));
            return;
        }
        if (amount > CashDue)
        {
            Dialogs.Warning(Loc.T("Payment.CantExceed", Loc.EnumText(method), CurrencyFormat.Format(CashDue)));
            return;
        }
        if (method == PaymentMethod.StoreCredit && amount > StoreCreditAvailable - Used(PaymentMethod.StoreCredit))
        {
            Dialogs.Warning(Loc.T("Payment.CreditAvailable", CurrencyFormat.Format(StoreCreditAvailable)));
            return;
        }
        if (method == PaymentMethod.LoyaltyPoints && amount > LoyaltyValueAvailable - Used(PaymentMethod.LoyaltyPoints))
        {
            Dialogs.Warning(Loc.T("Payment.PointsWorth", CurrencyFormat.Format(LoyaltyValueAvailable)));
            return;
        }

        if (method == PaymentMethod.Delivery && !AllowDelivery) return;
        Tenders.Add(new TenderViewModel(method, Money.Round(amount), method is PaymentMethod.Card or PaymentMethod.MobileWallet or PaymentMethod.Delivery ? Reference : null));
        Reference = null;
    }

    [RelayCommand]
    private void UseFullStoreCredit()
    {
        AmountText = Math.Min(StoreCreditAvailable - Used(PaymentMethod.StoreCredit), CashDue).ToString("0.00");
        AddTender(PaymentMethod.StoreCredit);
    }

    [RelayCommand]
    private void UseFullLoyalty()
    {
        AmountText = Math.Min(LoyaltyValueAvailable - Used(PaymentMethod.LoyaltyPoints), CashDue).ToString("0.00");
        AddTender(PaymentMethod.LoyaltyPoints);
    }

    [RelayCommand]
    private void RemoveTender(TenderViewModel tender) => Tenders.Remove(tender);

    [RelayCommand]
    private void Complete()
    {
        if (!IsFullyPaid)
        {
            Dialogs.Warning(Loc.T("Payment.StillToPay", RemainingText));
            return;
        }
        if (SplitError.Length > 0)
        {
            Dialogs.Warning(SplitError);
            return;
        }
        if (CashDue == 0 && (TenderedUsd > 0 || TenderedLbp > 0))
        {
            // Card, wallet or delivery already cover everything: this cash would all be "change" and is refused.
            Dialogs.Warning(Loc.T("Err.CashNotNeeded"));
            ClearCash();
            return;
        }
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private decimal Used(PaymentMethod method) => Tenders.Where(t => t.Method == method).Sum(t => t.Amount);

    private void OnTendersChanged()
    {
        OnPropertyChanged(nameof(NonCashPaid));
        OnPropertyChanged(nameof(CashDue));
        OnPropertyChanged(nameof(CashDueLbp));
        AmountText = CashDue.ToString("0.00");
        // Card, wallet or delivery now cover everything: cash typed earlier is not being taken, so it must not
        // turn into "change" on the receipt and in the drawer count.
        if (CashDue == 0 && (UsdText.Length > 0 || LbpText.Length > 0)) ClearCash();
        RefreshCash();
    }

    private void RefreshCash()
    {
        var tender = new CashTender(TenderedUsd, LbpEnabled ? TenderedLbp : 0, LbpEnabled ? ChangeIn : ChangeCurrency.Usd);
        try
        {
            _cash = CashSettlement.Calculate(CashDue, tender, Rate, Rounding);
        }
        catch (BusinessRuleException)
        {
            _cash = new CashSettlement { ShortUsd = CashDue };
        }
        var plain = _cash;

        // No change to split any more: forget the split typed earlier.
        if (_split != SplitSide.None && !(LbpEnabled && HasChange))
        {
            _split = SplitSide.None;
            FillSplit("", "");
        }
        SplitError = "";
        if (_split != SplitSide.None)
        {
            try
            {
                _cash = CashSettlement.Calculate(CashDue, tender with { GiveUsd = GiveChangeUsd, GiveLbp = GiveChangeLbp }, Rate, Rounding);
                if (_split == SplitSide.Usd) FillSplit(GiveUsdText, _cash.ChangeLbp.ToString("N0", CultureInfo.CurrentCulture));
                else FillSplit(_cash.ChangeUsd.ToString("0", CultureInfo.CurrentCulture), GiveLbpText);
            }
            catch (BusinessRuleException ex)
            {
                SplitError = ex.Message;
                _cash = plain;
                if (_split == SplitSide.Usd) FillSplit(GiveUsdText, "");
                else FillSplit("", GiveLbpText);
            }
        }
        OnPropertyChanged(nameof(GiveUsdHint));
        OnPropertyChanged(nameof(GiveLbpHint));

        foreach (var name in new[]
                 {
                     nameof(TenderedUsd), nameof(TenderedLbp), nameof(LbpWorth), nameof(IsFullyPaid), nameof(RemainingUsd),
                     nameof(RemainingLbp), nameof(HasRemaining), nameof(ChangeUsd), nameof(ChangeLbp), nameof(HasChange),
                     nameof(ChangeText), nameof(RemainingText),
                 })
            OnPropertyChanged(name);

        BuildQuickButtons();
    }

    private void BuildQuickButtons()
    {
        // Dollars still needed after the pounds typed so far, and the other way round.
        var usdNeeded = LbpEnabled ? Math.Max(0, Math.Ceiling((CashDue - TenderedLbp / Rate) * 100m) / 100m) : CashDue;
        var lbpNeeded = Lbp.ToPay(CashDue - TenderedUsd, Rate, Rounding);

        var usd = new List<QuickCash>();
        if (usdNeeded > 0)
        {
            usd.Add(new QuickCash(Loc.T("Payment.Exact"), usdNeeded));
            foreach (var note in new[] { 1m, 5m, 10m, 20m, 50m, 100m })
            {
                var rounded = Math.Ceiling(usdNeeded / note) * note;
                if (rounded > usdNeeded && usd.All(o => o.Amount != rounded))
                    usd.Add(new QuickCash(CurrencyFormat.Format(rounded), rounded));
                if (usd.Count >= 6) break;
            }
        }
        QuickUsd = usd;

        var lbp = new List<QuickCash>();
        if (LbpEnabled && lbpNeeded > 0)
        {
            lbp.Add(new QuickCash(Loc.T("Payment.Exact"), lbpNeeded));
            foreach (var note in new[] { 100_000m, 500_000m, 1_000_000m, 5_000_000m, 10_000_000m })
            {
                var rounded = Math.Ceiling(lbpNeeded / note) * note;
                if (rounded > lbpNeeded && lbp.All(o => o.Amount != rounded))
                    lbp.Add(new QuickCash(rounded.ToString("N0"), rounded));
                if (lbp.Count >= 6) break;
            }
        }
        QuickLbp = lbp;
    }

    private static string FormatPair(decimal usd, decimal lbp) => (usd, lbp) switch
    {
        ( > 0, > 0) => $"{CurrencyFormat.Format(usd)} + {CurrencyFormat.Lbp(lbp)}",
        (_, > 0) => CurrencyFormat.Lbp(lbp),
        _ => CurrencyFormat.Format(usd),
    };

    private static decimal ParseOrZero(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0
        : decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var v) && v > 0 ? v
        : decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out v) && v > 0 ? v
        : 0;

    private bool TryParseAmount(out decimal amount) =>
        decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) ||
        decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
}
