using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Menu;
using UniPos.Core.Pricing;
using UniPos.Core.Security;
using UniPos.Data.Services;
using UniPos.Desktop.Converters;
using UniPos.Desktop.Infrastructure;
using UniPos.Desktop.Services;
using UniPos.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniPos.Core.Localization;

namespace UniPos.Desktop.ViewModels;

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
            else if (e.PropertyName == nameof(Session.Features)) OnPropertyChanged(nameof(ShowOrderSource));
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
    public partial List<Product> SearchResults { get; set; } = [];

    [ObservableProperty]
    public partial Product? SelectedResult { get; set; }

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
        var held = RefreshHeldCountAsync(); // alongside the product list
        // The register keeps its state between visits: show the product list at once and refresh it in the background.
        if (BrowseCategories.Count == 0) await LoadCategoriesAsync();
        else _ = LoadCategoriesAsync();
        await held;
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
                found => BrowseProducts = found.Take(MaxTiles).Select(p => new ProductRow(p)).ToList());
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Products.SearchFailed"), ex);
        }
    }

    /// <summary>An item tile was clicked: add it (asking for its options first when it has any).</summary>
    [RelayCommand]
    private void PickProduct(ProductRow? row)
    {
        if (row is not null) AddProduct(row.Product);
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
                ct => SearchTiming.TimeAsync("Register (typing)", text, () => _products.SearchItemsAsync(text, ct: ct), _products, r => r.Count),
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
                    async () => await _products.FindByCodeAsync(text, ct) is { } exact ? [exact] : await _products.SearchItemsAsync(text, ct: ct),
                    _products, r => r.Count),
                results =>
                {
                    if (results.Count == 1) AddProduct(results[0]);
                    else ShowResults(text, results);
                });
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Products.SearchFailed"), ex);
        }
    }

    private void ShowResults(string text, List<Product> results)
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
    private void AddResult(Product? product)
    {
        product ??= SelectedResult;
        if (product is not null) AddProduct(product);
    }

    [RelayCommand]
    private void CloseSearch()
    {
        _search.Cancel();
        HideResults();
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds an item: one with options asks for them first (size, fillings...); "Cancel" adds nothing. A sold-out item is
    /// refused.
    /// </summary>
    private void AddProduct(Product product)
    {
        if (IsBusy) return; // the sale is being saved: anything added now would be cleared with it
        if (product.SoldOut)
        {
            Dialogs.Warning(Loc.T("Err.ItemSoldOut", product.Name));
            return;
        }
        IReadOnlyList<Modifier> options = [];
        if (ModifierRules.ActiveGroups(product).Any())
        {
            var picker = new ModifierPickerViewModel(Dialogs, product);
            if (!Dialogs.ShowDialog(picker))
            {
                FocusSearchRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            options = picker.Picked;
        }
        AddItem(product, options);
    }

    private void AddItem(Product product, IReadOnlyList<Modifier> options)
    {
        // A line with a discount or a typed price stays as it is: the same item added again goes on its own line.
        var ids = options.Select(o => o.Id).ToList();
        var existing = Items.FirstOrDefault(i => i.IsSame(product.Id, ids) && i.DiscountType == DiscountType.None && !i.PriceChanged);
        var newQty = (existing?.Quantity ?? 0) + 1;
        if (!CheckStock(product.Id, product.TrackStock, product.StockQuantity, newQty, existing, product.Name)) return;

        if (existing is not null)
        {
            existing.Quantity = newQty;
            SelectedItem = existing;
        }
        else
        {
            var item = NewItem(product, options);
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
    private bool CheckStock(int productId, bool trackStock, int onHand, int wanted, CartItemViewModel? line, string name)
    {
        if (!trackStock) return true; // made to order: no stock to run out of
        wanted += Items.Where(i => i.ProductId == productId && !ReferenceEquals(i, line)).Sum(i => i.Quantity);
        if (wanted <= onHand || _settings.Current.AllowNegativeStock) return true;
        Dialogs.Warning(onHand <= 0 ? Loc.T("Register.OutOfStock", name) : Loc.T("Register.OnlyInStock", onHand, name));
        return false;
    }

    // ---- Cart editing -----------------------------------------------------------------------

    [RelayCommand]
    private void Increase(CartItemViewModel? item)
    {
        item ??= SelectedItem;
        if (IsBusy || item is null || !CheckStock(item.ProductId, item.TrackStock, item.StockOnHand, item.Quantity + 1, item, item.ProductName)) return;
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
        var qty = Dialogs.PromptInt(Loc.T("Common.Qty"), Loc.T("Register.QuantityFor", item.ProductName, item.Options), item.Quantity);
        if (qty is null) return;
        if (qty <= 0)
        {
            Items.Remove(item);
            return;
        }
        if (CheckStock(item.ProductId, item.TrackStock, item.StockOnHand, qty.Value, item, item.ProductName)) item.Quantity = qty.Value;
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
        if (!EnsurePricesApproved())
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
        if (!EnsurePricesApproved())
        {
            CartDiscountType = oldType;
            CartDiscountValue = oldValue;
            Recalculate();
        }
    }

    private bool HasSelection() => SelectedItem is not null;

    /// <summary>The X on a row passes that row; the toolbar button passes nothing and removes the selected one.</summary>
    private bool CanRemoveItem(CartItemViewModel? item) => item is not null || SelectedItem is not null;

    /// <summary>
    /// Cashiers need a manager's credentials to give more than their limit off the shelf prices (typed prices and
    /// discounts together) and to sell anything below cost. Managers only see the "below cost" warning on the line.
    /// </summary>
    private bool EnsurePricesApproved()
    {
        Recalculate();
        if (Session.Can(Permission.OverrideDiscountLimit)) return true;

        var limit = _settings.Current.MaxCashierDiscountPercent;
        var worst = PriceRules.ReductionPercent(Items.Select(i => (i.ShelfPrice, i.Quantity)).ToList(), Totals);
        var overLimit = worst > limit && !(_approvedByUserId is not null && worst <= _approvedDiscountPercent);
        var belowCost = Items.Where(i => i.NeedsBelowCostApproval).ToList();
        if (!overLimit && belowCost.Count == 0) return true;

        var reasons = new List<string>();
        if (belowCost.Count > 0)
            reasons.Add(Loc.T("Register.BelowCostApproval", string.Join("\n", belowCost.Select(i => $"• {i.ProductName} {i.Options}".TrimEnd()))));
        if (overLimit) reasons.Add(Loc.T("Register.DiscountOverLimit", worst, limit));
        var approval = new ManagerApprovalViewModel(Dialogs, _users, string.Join("\n\n", reasons), Permission.OverrideDiscountLimit);
        if (!Dialogs.ShowDialog(approval) || approval.ApprovedBy is null) return false;

        _approvedByUserId = approval.ApprovedBy.Id;
        _approvedDiscountPercent = Math.Max(_approvedDiscountPercent, worst);
        foreach (var item in belowCost) item.BelowCostApprovedAt = item.NetEach;
        return true;
    }

    private CartItemViewModel NewItem(Product product, IReadOnlyList<Modifier> options, int quantity = 1) =>
        new(product, options, quantity) { ShowCost = Session.Can(Permission.OverrideDiscountLimit) };

    /// <summary>The cashier typed a price in a cart line's price box (Enter, Tab or clicking away).</summary>
    private void OnPriceTyped(object? sender, string text)
    {
        // Not for a line that left the cart (sold, held or cleared) while the typed price was on its way.
        if (sender is not CartItemViewModel item || IsBusy || !Items.Contains(item)) return;
        var cleaned = text.Replace(CurrencyFormat.Symbol, "").Trim();

        // A barcode scanned while the price box had the keyboard: it's an item to add, not a price.
        if (cleaned.Length >= 8 && cleaned.All(char.IsAsciiDigit))
        {
            SearchText = cleaned;
            SearchCommand.Execute(null);
            FocusSearchRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (!PriceRules.TryParsePrice(cleaned, out var price) || price <= 0)
        {
            Dialogs.Warning(Loc.T("Register.InvalidPrice"));
            return;
        }
        price = Money.Round(price);
        if (price == item.UnitPrice) return;
        // A slip of the finger (4500 for 45.00) is caught before it reaches the total.
        if (item.ShelfPrice > 0 && price > item.ShelfPrice * 10
            && !Dialogs.Confirm(Loc.T("Register.PriceMuchHigher", CurrencyFormat.Format(price), CurrencyFormat.Format(item.ShelfPrice))))
            return;

        var old = item.UnitPrice;
        item.SetPrice(price);
        if (!EnsurePricesApproved()) item.SetPrice(old);
        Recalculate();
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
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
            Items.Select(i => new HeldCartLine(i.ProductId, i.Quantity, i.DiscountType, i.DiscountValue, i.PriceChanged ? i.UnitPrice : null, i.OptionIds)).ToList(),
            CartDiscountType, CartDiscountValue, Customer?.Id, Channel, IsOnline ? DeliveryFee : 0, OrderNotes, IsOnline ? Courier : null, IsOnline ? DeliveryReference : null);
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
        var products = (await _products.GetItemsAsync(cart.Lines.Select(l => l.ProductId))).ToDictionary(p => p.Id);
        var customer = cart.CustomerId is { } customerId ? await _customers.GetAsync(customerId) : null;
        await _sales.ResumeAsync(picker.Chosen.Id);

        ResetSale();
        var missing = 0;
        foreach (var line in cart.Lines)
        {
            // Gone from the menu, sold out, or its options changed since it was held.
            var picked = line.Options ?? [];
            if (!products.TryGetValue(line.ProductId, out var p) || !p.IsActive || p.SoldOut || ModifierRules.Problem(p, picked) is not null)
            {
                missing++;
                continue;
            }
            var item = NewItem(p, ModifierRules.Ordered(p, picked), line.Quantity);
            item.DiscountType = line.DiscountType;
            item.DiscountValue = line.DiscountValue;
            if (line.UnitPrice is { } price) item.SetPrice(price);
            Items.Add(item);
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
    [NotifyPropertyChangedFor(nameof(IsOnline), nameof(ShowOrderSource), nameof(GrandTotal), nameof(TotalLbp))]
    public partial SalesChannel Channel { get; set; } = SalesChannel.InStore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GrandTotal), nameof(TotalLbp))]
    public partial decimal DeliveryFee { get; set; }

    /// <summary>
    /// The sale's note, typed in the register's note box (or the online order window): an address, an Instagram name, "gift
    /// wrap"... Saved with the sale and held carts, printed on the receipt, and shown in sales history, returns and the
    /// customer's purchases.
    /// </summary>
    [ObservableProperty]
    public partial string? OrderNotes { get; set; }

    /// <summary>Delivery company or driver of the online order.</summary>
    [ObservableProperty]
    public partial string? Courier { get; set; }

    /// <summary>The delivery company's invoice / tracking number.</summary>
    [ObservableProperty]
    public partial string? DeliveryReference { get; set; }

    public bool IsOnline => Channel != SalesChannel.InStore;

    /// <summary>
    /// The in store / online order bar: only where online orders are switched on, or for an online order already in
    /// the cart (resumed from hold), so it can still be finished or turned back into an in-store sale.
    /// </summary>
    public bool ShowOrderSource => Session.Features.Delivery || IsOnline;

    /// <summary>Items plus the delivery fee of an online order.</summary>
    public decimal GrandTotal => Totals.Total + (IsOnline ? DeliveryFee : 0);

    /// <summary>Turns this sale into an online order, or edits its details.</summary>
    [RelayCommand]
    private async Task OnlineOrderAsync()
    {
        if (!ShowOrderSource) return; // F11 where online orders are switched off

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
        // The note stays (it is the sale's), unless it is only the customer's address put there for the delivery.
        if (Customer?.FullAddress is { } address && OrderNotes?.Trim() == address.Trim()) OrderNotes = null;
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

        if (!EnsurePricesApproved()) return;
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
                var products = (await _products.GetItemsAsync(Items.Select(i => i.ProductId))).ToDictionary(p => p.Id);
                foreach (var item in Items)
                {
                    if (!products.TryGetValue(item.ProductId, out var p) || !p.IsActive || p.SoldOut || ModifierRules.Problem(p, item.OptionIds) is not null)
                    {
                        unavailable.Add(item);
                        continue;
                    }
                    var options = ModifierRules.Ordered(p, item.OptionIds);
                    if (item.Reprice(ModifierRules.Price(p, options), ModifierRules.Cost(p, options))) repriced++;
                }
                Recalculate();
            })) return;
        if (unavailable.Count > 0)
        {
            // Switched off (or deleted) since it was scanned: the sale would be refused after the money is taken.
            foreach (var item in unavailable) Items.Remove(item);
            Dialogs.Warning(Loc.T("Register.ItemsUnavailable", string.Join("\n", unavailable.Select(i => $"{i.ProductName} {i.Options}".TrimEnd()))));
            return;
        }
        if (repriced > 0)
        {
            Dialogs.Warning(Loc.T("Register.PricesChanged", repriced));
            return; // let the cashier see the new total before taking money
        }
        // Shelf prices or costs may have changed since the prices were typed.
        if (!EnsurePricesApproved()) return;
        // Managers sell below cost without approval, but are asked once.
        var belowCost = Items.Where(i => i.IsBelowCost).ToList();
        if (belowCost.Count > 0 && Session.Can(Permission.OverrideDiscountLimit)
            && !Dialogs.Confirm(Loc.T("Register.BelowCostConfirm", string.Join("\n", belowCost.Select(i => $"• {i.ProductName} {i.Options} ({i.BelowCostText})".Replace("  ", " "))))))
            return;

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
                TillId = Session.TillId,
                CustomerId = Customer?.Id,
                Lines = Items.Select(i => new CheckoutLine(i.ProductId, i.Quantity, i.DiscountType, i.DiscountValue, i.PriceChanged ? i.UnitPrice : null, i.OptionIds)).ToList(),
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
                Notes = OrderNotes,
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
            foreach (CartItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
                item.PriceTyped -= OnPriceTyped;
            }
        if (e.NewItems is not null)
            foreach (CartItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnItemPropertyChanged;
                item.PriceTyped += OnPriceTyped;
            }
        if (e.Action == NotifyCollectionChangedAction.Reset)
            SelectedItem = null;
        Recalculate();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CartItemViewModel.Quantity) or nameof(CartItemViewModel.DiscountType) or nameof(CartItemViewModel.DiscountValue)
            or nameof(CartItemViewModel.UnitPrice))
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
