using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using ClothingStore.Core.Localization;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Converters;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClothingStore.Desktop.ViewModels;

public enum CustomerDisplayMode
{
    /// <summary>No sale going on: logo, store name and welcome message.</summary>
    Idle,

    /// <summary>Items being rung up: the cart and the total.</summary>
    Cart,

    /// <summary>The payment window is open: total, what was handed over, and what is left or the change.</summary>
    Payment,

    /// <summary>Sale done: thank you, and the change, for a few seconds.</summary>
    ThankYou,
}

/// <summary>
/// What the customer-facing screen shows. It mirrors the register (the same cart and totals the cashier sees), so it
/// never reads the database and costs nothing while selling. Shows only what the customer is paying for.
/// </summary>
public sealed partial class CustomerDisplayViewModel : ObservableObject
{
    private static readonly TimeSpan ThankYouTime = TimeSpan.FromSeconds(8);

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _thankYouTimer = new() { Interval = ThankYouTime };

    public CustomerDisplayViewModel(SalesViewModel sales, SettingsService settings)
    {
        Sales = sales;
        _settings = settings;
        sales.Items.CollectionChanged += OnCartChanged;
        sales.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SalesViewModel.GrandTotal) or nameof(SalesViewModel.TotalLbp) or nameof(SalesViewModel.ShowLbp)
                or nameof(SalesViewModel.RateText))
                OnTotalsChanged();
        };
        sales.PaymentStarted += (_, payment) => BeginPayment(payment);
        sales.PaymentEnded += (_, _) => EndPayment();
        sales.SaleCompleted += (_, change) => ShowThankYou(change);
        settings.SettingsChanged += (_, _) => OnStoreChanged();
        _thankYouTimer.Tick += (_, _) =>
        {
            _thankYouTimer.Stop();
            ChangeGiven = null;
            UpdateMode();
        };
        UpdateMode();
    }

    /// <summary>The register: its cart lines and totals are shown as they are.</summary>
    public SalesViewModel Sales { get; }

    public Branding Branding => Branding.Instance;

    public string StoreName => _settings.Current.StoreName;
    public string? StorePhone => _settings.Current.Phone;
    public string WelcomeMessage => _settings.Current.CustomerDisplayMessage ?? Loc.T("Display.Welcome");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsCart), nameof(IsPayment), nameof(IsThankYou), nameof(ShowsCart))]
    public partial CustomerDisplayMode Mode { get; private set; }

    public bool IsIdle => Mode == CustomerDisplayMode.Idle;
    public bool IsCart => Mode == CustomerDisplayMode.Cart;
    public bool IsPayment => Mode == CustomerDisplayMode.Payment;
    public bool IsThankYou => Mode == CustomerDisplayMode.ThankYou;

    /// <summary>The item list stays on screen while paying, so the customer can check what they pay for.</summary>
    public bool ShowsCart => Mode is CustomerDisplayMode.Cart or CustomerDisplayMode.Payment;

    /// <summary>The payment window while it is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CashReceivedText), nameof(HasCashReceived), nameof(Total), nameof(TotalLbp), nameof(ShowLbp), nameof(RateText))]
    public partial PaymentViewModel? Payment { get; private set; }

    /// <summary>
    /// The total, in dollars and pounds. While paying it is the payment window's own (taken when it opened), so a rate
    /// changed at another till meanwhile can't show the customer two different pound amounts.
    /// </summary>
    public decimal Total => Payment?.Total ?? Sales.GrandTotal;
    public decimal TotalLbp => Payment is { LbpEnabled: true } p ? p.TotalLbp : Sales.TotalLbp;
    public bool ShowLbp => Payment?.LbpEnabled ?? Sales.ShowLbp;
    public string RateText => Payment?.RateText ?? Sales.RateText;

    private void OnTotalsChanged()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalLbp));
        OnPropertyChanged(nameof(ShowLbp));
        OnPropertyChanged(nameof(RateText));
    }

    /// <summary>"$20.00 + 500,000 LBP": the cash handed over so far.</summary>
    public string CashReceivedText => Payment is null ? "" : Pair(Payment.TenderedUsd, Payment.TenderedLbp);
    public bool HasCashReceived => Payment is { } p && (p.TenderedUsd > 0 || p.TenderedLbp > 0);

    /// <summary>Change given on the sale just completed; null when there was none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangeGiven))]
    public partial string? ChangeGiven { get; private set; }

    public bool HasChangeGiven => ChangeGiven is not null;

    private void BeginPayment(PaymentViewModel payment)
    {
        if (Payment is not null) Payment.PropertyChanged -= OnPaymentChanged;
        _thankYouTimer.Stop();
        Payment = payment;
        payment.PropertyChanged += OnPaymentChanged;
        UpdateMode();
    }

    private void EndPayment()
    {
        if (Payment is not null) Payment.PropertyChanged -= OnPaymentChanged;
        Payment = null;
        UpdateMode();
    }

    private void ShowThankYou(string? change)
    {
        EndPayment();
        ChangeGiven = change;
        Mode = CustomerDisplayMode.ThankYou;
        _thankYouTimer.Stop();
        _thankYouTimer.Start();
    }

    private void OnPaymentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaymentViewModel.TenderedUsd) or nameof(PaymentViewModel.TenderedLbp))
        {
            OnPropertyChanged(nameof(CashReceivedText));
            OnPropertyChanged(nameof(HasCashReceived));
        }
    }

    private void OnCartChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Mode == CustomerDisplayMode.ThankYou)
        {
            // Clearing the finished sale keeps the thank-you up; the next customer's first item ends it at once.
            if (Sales.Items.Count == 0) return;
            _thankYouTimer.Stop();
            ChangeGiven = null;
        }
        UpdateMode();
    }

    private void UpdateMode() => Mode =
        Payment is not null ? CustomerDisplayMode.Payment
        : Sales.Items.Count > 0 ? CustomerDisplayMode.Cart
        : CustomerDisplayMode.Idle;

    private void OnStoreChanged()
    {
        OnPropertyChanged(nameof(StoreName));
        OnPropertyChanged(nameof(StorePhone));
        OnPropertyChanged(nameof(WelcomeMessage));
    }

    private static string Pair(decimal usd, decimal lbp) => (usd, lbp) switch
    {
        ( > 0, > 0) => $"{CurrencyFormat.Format(usd)} + {CurrencyFormat.Lbp(lbp)}",
        (_, > 0) => CurrencyFormat.Lbp(lbp),
        _ => CurrencyFormat.Format(usd),
    };
}
