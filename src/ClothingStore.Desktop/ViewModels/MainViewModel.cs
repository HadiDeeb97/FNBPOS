using System.Collections.ObjectModel;
using System.Windows.Threading;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Security;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels;

public sealed partial class NavItem(string title, string glyph, Type pageType, Func<Task> navigate) : ObservableObject
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public Type PageType { get; } = pageType;
    public IAsyncRelayCommand NavigateCommand { get; } = new AsyncRelayCommand(navigate);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Number shown in a red bubble; 0 hides it.</summary>
    [ObservableProperty]
    public partial int Badge { get; set; }
}

public sealed class NavGroup(string title, IReadOnlyList<NavItem> items)
{
    public string Title { get; } = title;
    public IReadOnlyList<NavItem> Items { get; } = items;
}

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly NavigationService _navigation;
    private readonly SettingsService _settings;
    private readonly UserService _users;
    private readonly BrandingService _branding;
    private readonly DispatcherTimer _clock;

    private readonly SalesViewModel _register;

    public MainViewModel(
        IDialogService dialogs, NavigationService navigation, Session session, SettingsService settings, UserService users,
        BrandingService branding, LicenseManager license, SalesViewModel register)
        : base(dialogs)
    {
        _register = register;
        License = license;
        _navigation = navigation;
        _settings = settings;
        _users = users;
        _branding = branding;
        Session = session;
        IsSidebarCollapsed = LocalPreferences.Current.SidebarCollapsed;
        UiScale.Changed += OnUiScaleChanged;

        AddGroup("Nav.Group.Sell",
            Nav<SalesViewModel>("Nav.Register", "", Permission.Sell),
            Nav<ReturnsViewModel>("Nav.Returns", "", Permission.ProcessReturns),
            Nav<SalesHistoryViewModel>("Nav.SalesHistory", "", Permission.Sell),
            Nav<DeliveriesViewModel>("Nav.Deliveries", "\uE7B8", Permission.Sell),
            Nav<ShiftViewModel>("Nav.CashDrawer", "", Permission.Sell));
        AddGroup("Nav.Group.People",
            Nav<CustomersViewModel>("Nav.Customers", "", Permission.ManageCustomers));
        AddGroup("Nav.Group.Catalog",
            Nav<ProductsViewModel>("Nav.Products", "", Permission.ViewProducts),
            Nav<InventoryViewModel>("Nav.Inventory", "", Permission.ManageInventory),
            Nav<CategoriesViewModel>("Nav.Categories", "", Permission.ManageProducts));
        AddGroup("Nav.Group.Purchasing",
            Nav<PurchaseOrdersViewModel>("Nav.PurchaseOrders", "", Permission.ManagePurchasing),
            Nav<SuppliersViewModel>("Nav.Suppliers", "", Permission.ManagePurchasing));
        AddGroup("Nav.Group.Insights",
            Nav<ReportsViewModel>("Nav.Reports", "", Permission.ViewReports));
        AddGroup("Nav.Group.Admin",
            Nav<UsersViewModel>("Nav.Users", "", Permission.ManageCashiers),
            Nav<SettingsViewModel>("Nav.Settings", "", Permission.ManageSettings));

        _navigation.Navigated += OnNavigated;
        _navigation.PropertyChanged += OnNavigationPropertyChanged;
        _settings.SettingsChanged += OnSettingsChanged;

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _clock.Tick += OnClockTick;
        _clock.Start();
    }

    public event EventHandler? SignOutRequested;

    /// <summary>The user picked another language; the app rebuilds its windows.</summary>
    public event EventHandler<string>? LanguageChangeRequested;

    /// <summary>The user asked for default panel sizes; the app rebuilds the window after forgetting them.</summary>
    public event EventHandler? LayoutResetRequested;

    public Session Session { get; }
    public ObservableCollection<NavGroup> NavGroups { get; } = [];
    public IEnumerable<NavItem> NavItems => NavGroups.SelectMany(g => g.Items);
    public IPageViewModel? CurrentPage => _navigation.CurrentPage;
    public bool IsPageLoading => _navigation.IsLoading;
    public string StoreName => _settings.Current.StoreName;
    public DateTime Now => DateTime.Now;
    public Branding Branding => Branding.Instance;
    public string RoleDisplay => Session.CurrentUser is { } u ? Loc.EnumText(u.Role) : "";
    public bool IsArabic => Loc.IsRightToLeft;

    /// <summary>LBP rate chip in the top bar.</summary>
    public bool ShowRate => _settings.Current.ActiveLbpRate > 0;
    public string RateText => Loc.T("Rate.Short", _settings.Current.LbpRate.ToString("N0"));
    public bool CanChangeRate => Session.Can(Permission.ChangeExchangeRate);
    public bool CanChangeLogo => Session.Can(Permission.ManageBranding);

    /// <summary>License status for the expiry banner.</summary>
    public LicenseManager License { get; }

    [ObservableProperty]
    public partial bool IsSidebarCollapsed { get; set; }

    [ObservableProperty]
    public partial bool IsUserMenuOpen { get; set; }

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        LocalPreferences.Current.SidebarCollapsed = value;
        LocalPreferences.Current.Save();
    }

    /// <summary>Opens the first screen this user may use (the register for sellers).</summary>
    public async Task StartAsync(Type? page = null)
    {
        var start = NavItems.FirstOrDefault(i => i.PageType == page) ?? NavItems.FirstOrDefault();
        if (start is not null) await start.NavigateCommand.ExecuteAsync(null);
        await OfferOtherDrawerAsync();
    }

    private NavItem? Nav<TPage>(string titleKey, string glyph, Permission permission) where TPage : IPageViewModel =>
        Session.Can(permission) ? new NavItem(Loc.T(titleKey), glyph, typeof(TPage), () => _navigation.NavigateToAsync<TPage>()) : null;

    private void AddGroup(string titleKey, params NavItem?[] items)
    {
        var visible = items.OfType<NavItem>().ToList();
        if (visible.Count > 0) NavGroups.Add(new NavGroup(Loc.T(titleKey), visible));
    }

    private void OnNavigated(object? sender, EventArgs e)
    {
        var type = _navigation.CurrentPage?.GetType();
        foreach (var item in NavItems) item.IsSelected = item.PageType == type;
    }

    private void OnNavigationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationService.CurrentPage)) OnPropertyChanged(nameof(CurrentPage));
        else if (e.PropertyName == nameof(NavigationService.IsLoading)) OnPropertyChanged(nameof(IsPageLoading));
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StoreName));
        OnPropertyChanged(nameof(ShowRate));
        OnPropertyChanged(nameof(RateText));
    }

    private int _ticks;
    private bool _checkingLicense;

    private async void OnClockTick(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Now));

        // Every hour, re-check the license (the app may stay open for days).
        if (++_ticks % 240 == 0) await RecheckLicenseAsync();

        // Every minute, pick up a rate or logo changed on another till.
        if (_ticks % 4 != 0) return;
        try
        {
            if (await _settings.RefreshCurrencyAsync())
                Dialogs.Toast(Loc.T("Rate.ChangedElsewhere", _settings.Current.LbpRate.ToString("N0")));
            await Branding.Instance.RefreshAsync(_branding, onlyIfChanged: true);
        }
        catch
        {
            // Offline for a moment: the next tick tries again, and checkout re-checks the rate anyway.
        }
    }

    private async Task RecheckLicenseAsync()
    {
        // The clock keeps ticking while the activation window is open: never stack a second one.
        if (_checkingLicense) return;
        _checkingLicense = true;
        try
        {
            await CheckLicenseOnceAsync();
        }
        finally
        {
            _checkingLicense = false;
        }
    }

    private async Task CheckLicenseOnceAsync()
    {
        try
        {
            if ((await License.CheckAsync()).CanRun) return;
        }
        catch
        {
            return; // database unreachable for a moment: check again later
        }
        if (!Dialogs.ShowDialog(new ActivationViewModel(Dialogs, License, required: true)))
            System.Windows.Application.Current.Shutdown();
    }

    [RelayCommand]
    private void ShowLicense()
    {
        IsUserMenuOpen = false;
        if (Dialogs.ShowDialog(new ActivationViewModel(Dialogs, License, required: false)))
            Dialogs.Toast(Loc.T("License.Activated"));
    }

    [RelayCommand]
    private void ChangeLogo()
    {
        IsUserMenuOpen = false;
        Dialogs.ShowDialog(new StoreLogoViewModel(Dialogs, _branding, Session));
    }

    [RelayCommand]
    private void ChangeRate()
    {
        if (Dialogs.ShowDialog(new ExchangeRateViewModel(Dialogs, _settings, Session)))
            Dialogs.Toast(Loc.T("Rate.Changed", _settings.Current.LbpRate.ToString("N0")));
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    [RelayCommand]
    private void ChangePassword()
    {
        IsUserMenuOpen = false;
        if (Dialogs.ShowDialog(new ChangePasswordViewModel(Dialogs, _users, Session, forced: false)))
            Dialogs.Toast(Loc.T("Shell.PasswordChanged"));
    }

    [RelayCommand]
    private async Task SetLanguageAsync(string language)
    {
        IsUserMenuOpen = false;
        if (Loc.Normalize(language) == Loc.Language) return;
        try
        {
            await _users.SetPreferredLanguageAsync(Session.User.Id, language);
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.SomethingWentWrong"), ex);
            return;
        }
        LanguageChangeRequested?.Invoke(this, Loc.Normalize(language));
    }

    // ---- Text size of this PC (everyone can change it; also Ctrl +, Ctrl −, Ctrl 0) ----

    public string TextSizeText => $"{UiScale.Percent}%";

    [RelayCommand(CanExecute = nameof(CanTextLarger))]
    private void TextLarger() => UiScale.Larger();

    [RelayCommand(CanExecute = nameof(CanTextSmaller))]
    private void TextSmaller() => UiScale.Smaller();

    private static bool CanTextLarger() => UiScale.CanGrow;
    private static bool CanTextSmaller() => UiScale.CanShrink;

    private void OnUiScaleChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(TextSizeText));
        TextLargerCommand.NotifyCanExecuteChanged();
        TextSmallerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ResetLayout()
    {
        IsUserMenuOpen = false;
        if (Dialogs.Confirm(Loc.T("Shell.ResetLayoutConfirm"))) LayoutResetRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closing the app with a sale in progress: ask before its items are lost.</summary>
    public bool ConfirmLeavingCart() =>
        !_register.HasItems || Dialogs.Confirm(Loc.T("Shell.CloseConfirmCart", _register.Items.Count));

    [RelayCommand]
    private void SignOut()
    {
        IsUserMenuOpen = false;
        // Signing out clears the register, so say so when a sale is in progress (hold it with F9 to keep it).
        if (_register.HasItems && !Dialogs.Confirm(Loc.T("Shell.SignOutConfirmCart", _register.Items.Count))) return;
        if (Session.HasOpenShift)
        {
            if (!ConfirmLeavingDrawerOpen()) return;
        }
        else if (!_register.HasItems && !Dialogs.Confirm(Loc.T("Shell.SignOutConfirm")))
        {
            return;
        }
        SignOutRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Leaving with the drawer still open: leave it open for the next cashier (they can continue it or count it), or
    /// go and close it now. True to go ahead and leave.
    /// </summary>
    public bool ConfirmLeavingDrawerOpen()
    {
        if (!Session.HasOpenShift) return true;
        if (Dialogs.Choose(Loc.T("Shell.DrawerStillOpen"), Loc.T("Nav.CashDrawer"), Loc.T("Shell.LeaveDrawerOpen"), Loc.T("Shell.CloseDrawerNow")))
            return true;
        _ = _navigation.NavigateToAsync<ShiftViewModel>();
        return false;
    }

    /// <summary>
    /// Right after signing in: another cashier's drawer is still open on this PC. Continue it, or count and close it.
    /// </summary>
    private async Task OfferOtherDrawerAsync()
    {
        if (Session.OtherDrawer is not { } drawer || !Session.Can(Permission.Sell)) return;
        var name = (drawer.CurrentUser ?? drawer.User)?.FullName ?? "?";
        var keep = Dialogs.Choose(
            Loc.T("Shift.OtherDrawerOpen", name, drawer.OpenedAt.ToString("g")) + "\n\n" + Loc.T("Shift.OtherDrawerQuestion"),
            Loc.T("Shift.OtherDrawerTitle"), Loc.T("Shift.ContinueDrawer"), Loc.T("Shift.CountAndClose"));
        await _navigation.NavigateToAsync<ShiftViewModel>(async page =>
        {
            if (keep) await page.ContinueDrawerCommand.ExecuteAsync(null);
            else await page.CountOtherDrawerCommand.ExecuteAsync(null);
        });
    }

    public void Dispose()
    {
        _clock.Stop();
        _clock.Tick -= OnClockTick;
        _navigation.Navigated -= OnNavigated;
        _navigation.PropertyChanged -= OnNavigationPropertyChanged;
        _settings.SettingsChanged -= OnSettingsChanged;
        UiScale.Changed -= OnUiScaleChanged;
    }
}
