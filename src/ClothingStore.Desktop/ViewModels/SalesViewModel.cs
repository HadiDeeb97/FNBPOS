using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Pricing;
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

/// <summary>The register: scan or search items, build the cart, discount, hold/resume and take payment.</summary>
public sealed partial class SalesViewModel : ViewModelBase, IPageViewModel
{
    private readonly ProductService _products;
    private readonly SalesService _sales;
    private readonly CustomerService _customers;
    private readonly SettingsService _settings;
    private readonly UserService _users;
    private readonly DeliveryService _deliveries;
    private readonly CategoryService _categories;
    private readonly PrintService _print;
    private readonly INavigationService _navigation;

    private int? _approvedByUserId;
    private decimal _approvedDiscountPercent;

    private readonly ShiftService _shifts;

    public SalesViewModel(
        IDialogService dialogs, ProductService products, SalesService sales, CustomerService customers,
        SettingsService settings, UserService users, CategoryService categories, Session session, PrintService print,
        INavigationService navigation, DeliveryService deliveries, ShiftService shifts)
        : base(dialogs)
    {
        _deliveries = deliveries;
        _shifts = shifts;
        _products = products;
        _sales = sales;
        _customers = customers;
        _settings = settings;
        _users = users;
        _categories = categories;
        _print = print;
        _navigation = navigation;
        Session = session;

        Items.CollectionChanged += OnItemsChanged;
        settings.SettingsChanged += (_, _) => Recalculate();
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Session.CurrentUser)) ResetSale();
        };
    }

    public string Title => Loc.T("Nav.Register");
    public Session Session { get; }

    /// <summary>Asks the view to put the cursor back in the scan box.</summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>The payment window opened (the customer display shows what is paid as it is typed).</summary>
    public event EventHandler<PaymentViewModel>? PaymentStarted;

    /// <summary>The payment window closed without a sale (cancelled, or the sale was refused).</summary>
    public event EventHandler? PaymentEnded;

    /// <summary>A sale was saved; the text is the change given ("$5.00 + 100,000 LBP"), or null when there was none.</summary>
    public event EventHandler<string?>? SaleCompleted;

    public ObservableCollection<CartItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveItemCommand), nameof(LineDiscountCommand), nameof(SetQuantityCommand))]
    public partial CartItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial List<ProductVariant> SearchResults { get; set; } = [];

    [ObservableProperty]
    public partial ProductVariant? SelectedResult { get; set; }

    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    /// <summary>Shown in the results panel when nothing matches.</summary>
    [ObservableProperty]
    public partial string? SearchMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    public partial Customer? Customer { get; set; }

    public bool HasCustomer => Customer is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CartDiscountDisplay))]
    public partial DiscountType CartDiscountType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CartDiscountDisplay))]
    public partial decimal CartDiscountValue { get; set; }

    [ObservableProperty]
    public partial CartTotals Totals { get; private set; } = CartTotals.Empty;

    [ObservableProperty]
    public partial int HeldCount { get; private set; }

    public int ItemCount => Items.Sum(i => i.Quantity);
    public bool HasItems => Items.Count > 0;

    public string TaxLabel => _settings.Current.PricesIncludeTax
        ? Loc.T("Register.TaxIncluded", _settings.Current.TaxRate)
        : Loc.T("Register.Tax", _settings.Current.TaxRate);

    public string CartDiscountDisplay => CartDiscountType switch
    {
        DiscountType.Percent => Loc.T("Register.CartDiscountPercent", CartDiscountValue),
        DiscountType.Amount => Loc.T("Register.CartDiscountAmount", CurrencyFormat.Format(CartDiscountValue)),
        _ => "",
    };

    public async Task OnNavigatedToAsync()
    {
        OnPropertyChanged(nameof(HasCashDrawer)); // may have been switched on in Settings since

        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
        await RefreshHeldCountAsync();
        // The register keeps its state between visits: show the product list at once and refresh it in the background.
        if (BrowseCategories.Count == 0) await LoadCategoriesAsync();
        else _ = LoadCategoriesAsync();
    }

    // ---- Browse panel (pick items without scanning) -------------------------------------------

    private const string BrowseVisibleKey = "register:browse";

    /// <summary>Tiles are created for every product shown, so very large categories show the first ones only.</summary>
    private const int MaxTiles = 150;
    private readonly LatestSearch _browse = new(TimeSpan.Zero);

    /// <summary>Category chips: "All" first.</summary>
    [ObservableProperty]
    public partial List<Category> BrowseCategories { get; set; } = [];

    [ObservableProperty]
    public partial Category? BrowseCategory { get; set; }

    [ObservableProperty]
    public partial List<ProductRow> BrowseProducts { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBrowseVisible { get; set; } =
        !LocalPreferences.Current.Layout.TryGetValue(BrowseVisibleKey, out var shown) || shown != "hidden";

    partial void OnIsBrowseVisibleChanged(bool value)
    {
        LocalPreferences.Current.Layout[BrowseVisibleKey] = value ? "shown" : "hidden";
        LocalPreferences.Current.Save();
    }

    partial void OnBrowseCategoryChanged(Category? value) => _ = LoadBrowseProductsAsync();

    [RelayCommand]
    private void ToggleBrowse() => IsBrowseVisible = !IsBrowseVisible;

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var selectedId = BrowseCategory?.Id ?? 0;
            BrowseCategories = [ProductsViewModel.AllCategories, .. (await _categories.GetAllAsync()).Where(c => c.IsActive)];
            BrowseCategory = BrowseCategories.FirstOrDefault(c => c.Id == selectedId) ?? BrowseCategories[0];
            await LoadBrowseProductsAsync();
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Products.SearchFailed"), ex);
        }
    }

    private async Task LoadBrowseProductsAsync()
    {
        var categoryId = BrowseCategory is { Id: > 0 } c ? c.Id : (int?)null;
        try
        {
            await _browse.RunNowAsync(
                ct => _products.SearchAsync(null, categoryId, includeInactive: false, ct, max: MaxTiles + 50),
                found => BrowseProducts = found.Where(p => p.Variants.Any(v => v.IsActive)).Take(MaxTiles).Select(p => new ProductRow(p)).ToList());
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Products.SearchFailed"), ex);
        }
    }

    /// <summary>A product tile was clicked: add it, or ask for size and colour first.</summary>
    [RelayCommand]
    private void PickProduct(ProductRow? row)
    {
        if (row is null) return;
        var variants = row.Product.Variants.Where(v => v.IsActive).ToList();
        foreach (var v in variants) v.Product = row.Product;
        if (variants.Count == 1)
        {
            AddVariant(variants[0]);
            return;
        }
        var picker = new VariantPickerViewModel(Dialogs, row.Product, variants, _settings.Current.AllowNegativeStock);
        if (Dialogs.ShowDialog(picker) && picker.Chosen is { } chosen) AddVariant(chosen);
        else FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- Adding items -----------------------------------------------------------------------

    private readonly LatestSearch _search = new();

    /// <summary>Shows matching items while the cashier types, without adding anything.</summary>
    partial void OnSearchTextChanged(string value)
    {
        var text = value.Trim();
        if (text.Length < 2)
        {
            _search.Cancel();
            if (IsSearchOpen && text.Length == 0) HideResults();
            return;
        }
        _ = LiveSearchAsync(text);
    }

    private async Task LiveSearchAsync(string text)
    {
        try
        {
            await _search.RunAsync(
                ct => SearchTiming.TimeAsync("Register (typing)", text, () => _products.SearchVariantsAsync(text, ct: ct), _products, r => r.Count),
                results => ShowResults(text, results));
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Products.SearchFailed"), ex);
        }
    }

    /// <summary>
    /// Enter or a barcode scan: an exact barcode/SKU or a single match is added straight away, otherwise the
    /// matches are listed. Runs even while an earlier search is still going (that one is cancelled).
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        var text = SearchText.Trim();
        if (text.Length == 0) return;
        try
        {
            await _search.RunNowAsync(
                ct => SearchTiming.TimeAsync("Register (Enter/scan)", text,
                    async () => await _products.FindByCodeAsync(text, ct) is { } exact ? [exact] : await _products.SearchVariantsAsync(text, ct: ct),
                    _products, r => r.Count),
                results =>
                {
                    if (results.Count == 1) AddVariant(results[0]);
                    else ShowResults(text, results);
                });
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Products.SearchFailed"), ex);
        }
    }

    private void ShowResults(string text, List<ProductVariant> results)
    {
        SearchResults = results;
        SelectedResult = results.FirstOrDefault();
        SearchMessage = results.Count == 0 ? Loc.T("Register.NoMatch", text) : null;
        IsSearchOpen = true;
    }

    private void HideResults()
    {
        IsSearchOpen = false;
        SearchResults = [];
        SearchMessage = null;
    }

    [RelayCommand]
    private void AddResult(ProductVariant? variant)
    {
        variant ??= SelectedResult;
        if (variant is null) return;
        AddVariant(variant);
    }

    [RelayCommand]
    private void CloseSearch()
    {
        _search.Cancel();
        HideResults();
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AddVariant(ProductVariant variant)
    {
        if (IsBusy) return; // the sale is being saved: anything added now would be cleared with it
        var existing = Items.FirstOrDefault(i => i.VariantId == variant.Id && i.DiscountType == DiscountType.None);
        var newQty = (existing?.Quantity ?? 0) + 1;
        if (!CheckStock(variant.Id, variant.StockQuantity, newQty, existing, variant.DisplayName)) return;

        if (existing is not null)
        {
            existing.Quantity = newQty;
            SelectedItem = existing;
        }
        else
        {
            var item = new CartItemViewModel(variant);
            Items.Add(item);
            SelectedItem = item;
        }

        SearchText = "";
        HideResults();
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// <paramref name="wanted"/> is the quantity for <paramref name="line"/> (null: a new line); the same item on other
    /// lines (e.g. one with a discount) counts too, since they all come out of the same stock.
    /// </summary>
    private bool CheckStock(int variantId, int onHand, int wanted, CartItemViewModel? line, string name)
    {
        wanted += Items.Where(i => i.VariantId == variantId && !ReferenceEquals(i, line)).Sum(i => i.Quantity);
        if (wanted <= onHand || _settings.Current.AllowNegativeStock) return true;
        Dialogs.Warning(onHand <= 0 ? Loc.T("Register.OutOfStock", name) : Loc.T("Register.OnlyInStock", onHand, name));
        return false;
    }

    // ---- Cart editing -----------------------------------------------------------------------

    [RelayCommand]
    private void Increase(CartItemViewModel? item)
    {
        item ??= SelectedItem;
        if (IsBusy || item is null || !CheckStock(item.VariantId, item.StockOnHand, item.Quantity + 1, item, item.ProductName)) return;
        item.Quantity++;
    }

    [RelayCommand]
    private void Decrease(CartItemViewModel? item)
    {
        item ??= SelectedItem;
        if (IsBusy || item is null) return;
        if (item.Quantity > 1) item.Quantity--;
        else Items.Remove(item);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveItem))]
    private void RemoveItem(CartItemViewModel? item)
    {
        item ??= SelectedItem;
        if (IsBusy || item is null) return;
        var index = Items.IndexOf(item);
        Items.Remove(item);
        SelectedItem = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetQuantity()
    {
        if (IsBusy || SelectedItem is not { } item) return;
        var qty = Dialogs.PromptInt(Loc.T("Common.Qty"), Loc.T("Register.QuantityFor", item.ProductName, item.VariantDescription), item.Quantity);
        if (qty is null) return;
        if (qty <= 0)
        {
            Items.Remove(item);
            return;
        }
        if (CheckStock(item.VariantId, item.StockOnHand, qty.Value, item, item.ProductName)) item.Quantity = qty.Value;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void LineDiscount()
    {
        if (SelectedItem is not { } item) return;
        var dialog = new DiscountViewModel(Dialogs, Loc.T("Register.LineDiscountTitle", item.ProductName), item.UnitPrice * item.Quantity, item.DiscountType, item.DiscountValue);
        if (!Dialogs.ShowDialog(dialog)) return;

        var (oldType, oldValue) = (item.DiscountType, item.DiscountValue);
        item.DiscountType = dialog.DiscountType;
        item.DiscountValue = dialog.Value;
        if (!EnsureDiscountApproved())
        {
            item.DiscountType = oldType;
            item.DiscountValue = oldValue;
        }
    }

    [RelayCommand]
    private void CartDiscount()
    {
        if (!HasItems) return;
        var dialog = new DiscountViewModel(Dialogs, Loc.T("Register.CartDiscount"), Totals.Subtotal - Totals.LineDiscounts, CartDiscountType, CartDiscountValue);
        if (!Dialogs.ShowDialog(dialog)) return;

        var (oldType, oldValue) = (CartDiscountType, CartDiscountValue);
        CartDiscountType = dialog.DiscountType;
        CartDiscountValue = dialog.Value;
        Recalculate();
        if (!EnsureDiscountApproved())
        {
            CartDiscountType = oldType;
            CartDiscountValue = oldValue;
            Recalculate();
        }
    }

    private bool HasSelection() => SelectedItem is not null;

    /// <summary>The X on a row passes that row; the toolbar button passes nothing and removes the selected one.</summary>
    private bool CanRemoveItem(CartItemViewModel? item) => item is not null || SelectedItem is not null;

    /// <summary>Cashiers need a manager's credentials for discounts above the configured limit.</summary>
    private bool EnsureDiscountApproved()
    {
        Recalculate();
        if (Session.Can(Permission.OverrideDiscountLimit)) return true;

        var limit = _settings.Current.MaxCashierDiscountPercent;
        var worst = Math.Max(
            CartCalculator.DiscountPercent(Totals.Subtotal, Totals.DiscountTotal),
            Totals.Lines.Select(l => CartCalculator.DiscountPercent(l.Gross, l.Discount)).DefaultIfEmpty(0).Max());
        if (worst <= limit) return true;
        if (_approvedByUserId is not null && worst <= _approvedDiscountPercent) return true;

        var approval = new ManagerApprovalViewModel(Dialogs, _users,
            Loc.T("Register.DiscountOverLimit", worst, limit), Permission.OverrideDiscountLimit);
        if (!Dialogs.ShowDialog(approval) || approval.ApprovedBy is null) return false;

        _approvedByUserId = approval.ApprovedBy.Id;
        _approvedDiscountPercent = worst;
        return true;
    }

    // ---- Customer ---------------------------------------------------------------------------

    [RelayCommand]
    private void SelectCustomer()
    {
        var picker = new CustomerPickerViewModel(Dialogs, _customers);
        if (Dialogs.ShowDialog(picker) && picker.Selected is not null) Customer = picker.Selected;
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearCustomer()
    {
        if (IsOnline)
        {
            Dialogs.Warning(Loc.T("Register.OnlineNeedsCustomer"));
            return;
        }
        Customer = null;
    }

    // ---- Hold / resume ----------------------------------------------------------------------

    [RelayCommand]
    private Task HoldAsync() => RunAsync(async () =>
    {
        if (!HasItems) return;
        var label = Dialogs.Prompt(Loc.T("Register.Hold"), Loc.T("Register.HoldPrompt"),
            Customer?.FullName ?? Loc.T("Register.HoldDefault", DateTime.Now));
        if (label is null) return;

        var cart = new HeldCart(
            Items.Select(i => new HeldCartLine(i.VariantId, i.Quantity, i.DiscountType, i.DiscountValue)).ToList(),
            CartDiscountType, CartDiscountValue, Customer?.Id, Channel, IsOnline ? DeliveryFee : 0, IsOnline ? OrderNotes : null, IsOnline ? Courier : null, IsOnline ? DeliveryReference : null);
        await _sales.HoldAsync(label, Session.User.Id, cart);
        ResetSale();
        Dialogs.Toast(Loc.T("Register.Held", label));
        await RefreshHeldCountAsync();
    });

    [RelayCommand]
    private Task ResumeAsync() => RunAsync(async () =>
    {
        if (HasItems && !Dialogs.Confirm(Loc.T("Register.ReplaceCart"))) return;

        var picker = new HeldSalesViewModel(Dialogs, _sales);
        if (!Dialogs.ShowDialog(picker) || picker.Chosen is null)
        {
            await RefreshHeldCountAsync();
            return;
        }

        // Load everything first and only then take it off hold, so a failed load doesn't lose the cart.
        var cart = await _sales.PeekHeldAsync(picker.Chosen.Id);
        var variants = (await _products.GetVariantsAsync(cart.Lines.Select(l => l.VariantId))).ToDictionary(v => v.Id);
        var customer = cart.CustomerId is { } customerId ? await _customers.GetAsync(customerId) : null;
        await _sales.ResumeAsync(picker.Chosen.Id);

        ResetSale();
        var missing = 0;
        foreach (var line in cart.Lines)
        {
            if (!variants.TryGetValue(line.VariantId, out var v) || !v.IsActive || v.Product is { IsActive: false })
            {
                missing++;
                continue;
            }
            Items.Add(new CartItemViewModel(v, line.Quantity) { DiscountType = line.DiscountType, DiscountValue = line.DiscountValue });
        }
        CartDiscountType = cart.CartDiscountType;
        CartDiscountValue = cart.CartDiscountValue;
        Customer = customer;
        Channel = cart.Channel;
        DeliveryFee = cart.DeliveryFee;
        OrderNotes = cart.Notes;
        Courier = cart.Courier;
        DeliveryReference = cart.DeliveryReference;
        SelectedItem = Items.FirstOrDefault();
        Recalculate();
        await RefreshHeldCountAsync();

        if (missing > 0) Dialogs.Warning(Loc.T("Register.MissingItems", missing));
    });

    [RelayCommand]
    private void ClearCart()
    {
        if (!HasItems && Customer is null) return;
        if (Dialogs.Confirm(Loc.T("Register.ClearConfirm"))) ResetSale();
    }

    // ---- Online order -----------------------------------------------------------------------

    /// <summary>In store, or the channel of an online order (WhatsApp, Instagram...).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline), nameof(GrandTotal), nameof(TotalLbp))]
    public partial SalesChannel Channel { get; set; } = SalesChannel.InStore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GrandTotal), nameof(TotalLbp))]
    public partial decimal DeliveryFee { get; set; }

    /// <summary>Address, Instagram name or anything else about the order (saved with the sale, printed on the receipt).</summary>
    [ObservableProperty]
    public partial string? OrderNotes { get; set; }

    /// <summary>Delivery company or driver of the online order.</summary>
    [ObservableProperty]
    public partial string? Courier { get; set; }

    /// <summary>The delivery company's invoice / tracking number.</summary>
    [ObservableProperty]
    public partial string? DeliveryReference { get; set; }

    public bool IsOnline => Channel != SalesChannel.InStore;

    /// <summary>Items plus the delivery fee of an online order.</summary>
    public decimal GrandTotal => Totals.Total + (IsOnline ? DeliveryFee : 0);

    /// <summary>Turns this sale into an online order, or edits its details.</summary>
    [RelayCommand]
    private async Task OnlineOrderAsync()
    {
        // An online order is always for someone: pick (or add) the customer first.
        if (Customer is null)
        {
            var picker = new CustomerPickerViewModel(Dialogs, _customers);
            if (!Dialogs.ShowDialog(picker) || picker.Selected is null)
            {
                Dialogs.Warning(Loc.T("Register.OnlineNeedsCustomer"));
                return;
            }
            Customer = picker.Selected.Region is null && picker.Selected.RegionId is not null
                ? await _customers.GetAsync(picker.Selected.Id) ?? picker.Selected
                : picker.Selected;
        }
        // Their address goes into the delivery notes unless something was typed already.
        if (string.IsNullOrWhiteSpace(OrderNotes) && Customer?.FullAddress is { } address) OrderNotes = address;

        IReadOnlyList<DeliveryPartner> partners = [];
        try { partners = await _deliveries.GetPartnersAsync(); } catch { /* the list can still be opened from the window */ }
        var dialog = new OnlineSaleViewModel(Dialogs, _deliveries, Channel, DeliveryFee, OrderNotes, Courier, partners, DeliveryReference);
        if (!Dialogs.ShowDialog(dialog)) return;
        Channel = dialog.Channel;
        DeliveryFee = dialog.DeliveryFee;
        OrderNotes = dialog.Notes;
        Courier = dialog.Courier;
        DeliveryReference = dialog.DeliveryReference;
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to a normal in-store sale.</summary>
    [RelayCommand]
    private void InStore()
    {
        Channel = SalesChannel.InStore;
        DeliveryFee = 0;
        OrderNotes = null;
        Courier = null;
        DeliveryReference = null;
    }

    // ---- Checkout ---------------------------------------------------------------------------

    /// <summary>The total in Lebanese pounds at today's rate, under the dollar total.</summary>
    public bool ShowLbp => _settings.Current.ActiveLbpRate > 0;
    public decimal TotalLbp => Lbp.ToPay(GrandTotal, _settings.Current.ActiveLbpRate, _settings.Current.LbpRounding);
    public string RateText => Loc.T("Rate.Short", _settings.Current.LbpRate.ToString("N0"));

    /// <summary>Shown when this PC has a cash drawer on its receipt printer (Settings → Printers).</summary>
    public bool HasCashDrawer => LocalPreferences.Current.CashDrawer;

    /// <summary>Opens the drawer without a sale (change for a customer, a float check). Counted on the Z report.</summary>
    [RelayCommand]
    private async Task OpenDrawerAsync()
    {
        if (Session.CurrentShift is not { } shift)
        {
            Dialogs.Warning(Loc.T("Drawer.NeedsShift"));
            return;
        }
        if (!CashDrawer.Open(out var error))
        {
            Dialogs.Warning(Loc.T("Drawer.Failed", error ?? ""));
            return;
        }
        await RunAsync(() => _shifts.RecordNoSaleOpeningAsync(shift.Id));
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task PayAsync()
    {
        if (!HasItems) return;

        if (!Session.HasOpenShift)
        {
            if (Dialogs.Confirm(Loc.T("Register.NeedShift")))
                await _navigation.NavigateToAsync<ShiftViewModel>();
            return;
        }

        if (!EnsureDiscountApproved()) return;
        if (IsOnline && Customer is null)
        {
            Dialogs.Warning(Loc.T("Register.OnlineNeedsCustomer"));
            return;
        }

        // The sale is priced on the server from current prices and tax: catch up with changes made at another till
        // (or in Products) since the items were scanned, so the total and change shown are what is charged.
        var repriced = 0;
        List<CartItemViewModel> unavailable = [];
        if (!await RunAsync(async () =>
            {
                await _settings.GetAsync();
                var variants = (await _products.GetVariantsAsync(Items.Select(i => i.VariantId).Distinct())).ToDictionary(v => v.Id);
                foreach (var item in Items)
                {
                    if (!variants.TryGetValue(item.VariantId, out var v) || !v.IsActive || v.Product is { IsActive: false }) unavailable.Add(item);
                    else if (item.Reprice(v.EffectivePrice)) repriced++;
                }
                Recalculate();
            })) return;
        if (unavailable.Count > 0)
        {
            // Switched off (or deleted) since it was scanned: the sale would be refused after the money is taken.
            foreach (var item in unavailable) Items.Remove(item);
            Dialogs.Warning(Loc.T("Register.ItemsUnavailable", string.Join("\n", unavailable.Select(i => $"{i.ProductName} {i.VariantDescription}"))));
            return;
        }
        if (repriced > 0)
        {
            Dialogs.Warning(Loc.T("Register.PricesChanged", repriced));
            return; // let the cashier see the new total before taking money
        }

        var payment = new PaymentViewModel(Dialogs, GrandTotal, Customer, _settings.Current, allowDelivery: IsOnline);
        PaymentStarted?.Invoke(this, payment);
        bool paid;
        try
        {
            paid = Dialogs.ShowDialog(payment);
        }
        catch
        {
            PaymentEnded?.Invoke(this, EventArgs.Empty); // the customer's screen doesn't stay on "paying"
            throw;
        }
        if (!paid)
        {
            PaymentEnded?.Invoke(this, EventArgs.Empty);
            return;
        }

        Sale? sale = null;
        var ok = await RunAsync(async () =>
        {
            sale = await _sales.CompleteSaleAsync(new CheckoutRequest
            {
                UserId = Session.User.Id,
                ShiftId = Session.CurrentShift?.Id,
                CustomerId = Customer?.Id,
                Lines = Items.Select(i => new CheckoutLine(i.VariantId, i.Quantity, i.DiscountType, i.DiscountValue)).ToList(),
                CartDiscountType = CartDiscountType,
                CartDiscountValue = CartDiscountValue,
                Payments = payment.Payments,
                ChangeIn = payment.EffectiveChangeIn,
                GiveChangeUsd = payment.GiveChangeUsd,
                GiveChangeLbp = payment.GiveChangeLbp,
                ExchangeRate = payment.Rate,
                ApprovedByUserId = _approvedByUserId,
                Channel = Channel,
                DeliveryFee = IsOnline ? DeliveryFee : 0,
                Notes = IsOnline ? OrderNotes : null,
                Courier = IsOnline ? Courier : null,
                DeliveryReference = IsOnline ? DeliveryReference : null,
            });
        });
        if (!ok || sale is null)
        {
            PaymentEnded?.Invoke(this, EventArgs.Empty);
            // Most likely the rate changed on another till: pick it up so the next attempt shows the new amounts.
            try { await _settings.RefreshCurrencyAsync(); } catch { /* checked again at checkout */ }
            return;
        }

        // Cash changed hands: open the drawer (when this PC has one) before the receipt prints.
        if (sale.CashTendered > 0 || sale.CashTenderedLbp > 0) CashDrawer.OpenFor(DrawerReason.CashSale);
        var change = (sale.ChangeGiven, sale.ChangeGivenLbp) switch
        {
            ( > 0, > 0) => $"{CurrencyFormat.Format(sale.ChangeGiven)} + {CurrencyFormat.Lbp(sale.ChangeGivenLbp)}",
            (_, > 0) => CurrencyFormat.Lbp(sale.ChangeGivenLbp),
            ( > 0, _) => CurrencyFormat.Format(sale.ChangeGiven),
            _ => null,
        };
        SaleCompleted?.Invoke(this, change); // before the cart is cleared, so the display thanks instead of going idle
        ResetSale();
        _ = LoadBrowseProductsAsync(); // stock on the tiles changed
        var receipt = Core.Receipts.ReceiptFormatter.Format(ReceiptBuilder.FromSale(sale, _settings.Current), _settings.Current.ReceiptWidth);
        var title = change is not null
            ? Loc.T("Register.ChangeDue", change)
            : Loc.T("Register.SaleComplete", sale.ReceiptNumber);
        Dialogs.ShowDialog(new TextPreviewViewModel(Dialogs, _print, title, receipt, Loc.T("Register.NewSale"), LocalPreferences.Current.ReceiptCopies,
            ReceiptLogo.For(_settings.Current)));
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- Internals --------------------------------------------------------------------------

    private void ResetSale()
    {
        Items.Clear();
        Customer = null;
        CartDiscountType = DiscountType.None;
        CartDiscountValue = 0;
        Channel = SalesChannel.InStore;
        DeliveryFee = 0;
        OrderNotes = null;
        Courier = null;
        DeliveryReference = null;
        SearchText = "";
        HideResults();
        _approvedByUserId = null;
        _approvedDiscountPercent = 0;
        Recalculate();
    }

    private async Task RefreshHeldCountAsync()
    {
        try
        {
            HeldCount = (await _sales.GetHeldAsync()).Count;
        }
        catch
        {
            HeldCount = 0;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (CartItemViewModel item in e.OldItems) item.PropertyChanged -= OnItemPropertyChanged;
        if (e.NewItems is not null)
            foreach (CartItemViewModel item in e.NewItems) item.PropertyChanged += OnItemPropertyChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
            SelectedItem = null;
        Recalculate();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CartItemViewModel.Quantity) or nameof(CartItemViewModel.DiscountType) or nameof(CartItemViewModel.DiscountValue))
            Recalculate();
    }

    private bool _recalculating;

    private void Recalculate()
    {
        if (_recalculating) return;
        _recalculating = true;
        try
        {
            var s = _settings.Current;
            Totals = Items.Count == 0
                ? CartTotals.Empty
                : CartCalculator.Calculate(Items.Select(i => i.ToInput()).ToList(), CartDiscountType, CartDiscountValue, s.TaxRate, s.PricesIncludeTax);
            for (var i = 0; i < Items.Count; i++) Items[i].Apply(Totals.Lines[i]);
            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(TaxLabel));
            OnPropertyChanged(nameof(ShowLbp));
            OnPropertyChanged(nameof(GrandTotal));
            OnPropertyChanged(nameof(TotalLbp));
            OnPropertyChanged(nameof(RateText));
        }
        finally
        {
            _recalculating = false;
        }
    }
}
