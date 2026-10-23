using System.Collections.ObjectModel;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.ViewModels;

public sealed record StatusFilter(string Label, PurchaseOrderStatus? Status);

public sealed partial class PurchaseOrdersViewModel(
    IDialogService dialogs, PurchaseOrderService orders, SupplierService suppliers, ProductService products,
    InventoryService inventory, SettingsService settings, Session session, PrintService print)
    : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.PurchaseOrders");

    public StatusFilter[] Filters { get; } =
    [
        new(Loc.T("Purchasing.AllOrders"), null),
        .. Enum.GetValues<PurchaseOrderStatus>().Select(s => new StatusFilter(Loc.EnumText(s), s)),
    ];

    [ObservableProperty]
    public partial StatusFilter SelectedFilter { get; set; } = null!;

    [ObservableProperty]
    public partial List<PurchaseOrder> Orders { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(MarkOrderedCommand), nameof(ReceiveCommand), nameof(CancelOrderCommand), nameof(DeleteCommand), nameof(PrintCommand))]
    public partial PurchaseOrder? SelectedOrder { get; set; }

    [ObservableProperty]
    public partial PurchaseOrder? Detail { get; set; }

    public async Task OnNavigatedToAsync()
    {
        // The first visit picks the first filter (which loads); later visits keep the filter and reload.
        if (SelectedFilter is null) SelectedFilter = Filters[0];
        else await RefreshAsync();
    }

    partial void OnSelectedFilterChanged(StatusFilter value) => _ = RefreshAsync();

    async partial void OnSelectedOrderChanged(PurchaseOrder? value)
    {
        // Clear first so Edit/Receive never act on the previous order while this one loads, and drop a slow
        // answer for an order that is no longer selected.
        Detail = null;
        if (value is null) return;
        try
        {
            var detail = await orders.GetAsync(value.Id);
            if (ReferenceEquals(SelectedOrder, value)) Detail = detail;
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Purchasing.LoadOrderFailed"), ex);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (SelectedFilter is null) return;
        var selectedId = SelectedOrder?.Id;
        try
        {
            Orders = await orders.GetAllAsync(SelectedFilter.Status);
            SelectedOrder = Orders.FirstOrDefault(o => o.Id == selectedId) ?? Orders.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Purchasing.LoadFailed"), ex);
        }
    }

    private bool HasSelection() => SelectedOrder is not null;

    [RelayCommand]
    private async Task NewAsync()
    {
        var editor = await PurchaseOrderEditorViewModel.CreateAsync(Dialogs, orders, suppliers, products, inventory, session, null);
        if (Dialogs.ShowDialog(editor)) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        if (Detail is null) return;
        if (Detail.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered))
        {
            Dialogs.Warning(Loc.T("Purchasing.OnlyOpenEditable"));
            return;
        }
        var editor = await PurchaseOrderEditorViewModel.CreateAsync(Dialogs, orders, suppliers, products, inventory, session, Detail);
        if (Dialogs.ShowDialog(editor)) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task MarkOrderedAsync()
    {
        if (SelectedOrder is null) return;
        if (await RunAsync(() => orders.MarkOrderedAsync(SelectedOrder.Id))) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ReceiveAsync()
    {
        if (Detail is null) return;
        if (Detail.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
        {
            Dialogs.Warning(Loc.T("Purchasing.AlreadyClosed"));
            return;
        }
        if (Dialogs.ShowDialog(new ReceivePurchaseOrderViewModel(Dialogs, orders, session, Detail))) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CancelOrderAsync()
    {
        if (SelectedOrder is null) return;
        if (!Dialogs.Confirm(Loc.T("Purchasing.CloseConfirm", SelectedOrder.OrderNumber))) return;
        if (await RunAsync(() => orders.CancelAsync(SelectedOrder.Id))) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (SelectedOrder is null) return;
        if (!Dialogs.Confirm(Loc.T("Purchasing.DeleteDraftConfirm", SelectedOrder.OrderNumber))) return;
        if (await RunAsync(() => orders.DeleteDraftAsync(SelectedOrder.Id))) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Print()
    {
        if (Detail is null) return;
        Dialogs.ShowDialog(new TextPreviewViewModel(Dialogs, print, Loc.T("Purchasing.OrderTitle", Detail.OrderNumber), ReceiptBuilder.PurchaseOrder(Detail, settings.Current)));
    }
}

public sealed partial class PoLineViewModel(ProductVariant variant, int quantity, decimal unitCost) : ObservableObject
{
    public ProductVariant Variant { get; } = variant;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    public partial int Quantity { get; set; } = quantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    public partial decimal UnitCost { get; set; } = unitCost;

    public decimal LineTotal => Quantity * UnitCost;
}

public sealed partial class PurchaseOrderEditorViewModel : DialogViewModelBase
{
    public override System.Windows.Size? WindowSize => new(width: 1140, height: 660);

    private readonly PurchaseOrderService _orders;
    private readonly ProductService _products;
    private readonly InventoryService _inventory;
    private readonly Session _session;
    private readonly int _id;

    private PurchaseOrderEditorViewModel(IDialogService dialogs, PurchaseOrderService orders, ProductService products,
        InventoryService inventory, Session session, PurchaseOrder? order) : base(dialogs)
    {
        _orders = orders;
        _products = products;
        _inventory = inventory;
        _session = session;
        _id = order?.Id ?? 0;
        OrderNumber = order?.OrderNumber ?? Loc.T("Purchasing.NewNumber");
        ExpectedDate = order?.ExpectedDate ?? DateTime.Today.AddDays(14);
        Notes = order?.Notes;
        foreach (var line in order?.Lines ?? [])
            AddLine(new PoLineViewModel(line.ProductVariant!, line.QuantityOrdered, line.UnitCost));
    }

    public static async Task<PurchaseOrderEditorViewModel> CreateAsync(IDialogService dialogs, PurchaseOrderService orders, SupplierService suppliers,
        ProductService products, InventoryService inventory, Session session, PurchaseOrder? order)
    {
        var vm = new PurchaseOrderEditorViewModel(dialogs, orders, products, inventory, session, order)
        {
            // The order's own supplier stays selectable even if deactivated since (otherwise saving switches supplier).
            Suppliers = (await suppliers.GetAllAsync(includeInactive: true)).Where(s => s.IsActive || s.Id == order?.SupplierId).ToList(),
        };
        vm.SelectedSupplier = vm.Suppliers.FirstOrDefault(s => s.Id == order?.SupplierId) ?? vm.Suppliers.FirstOrDefault();
        return vm;
    }

    public override string Title => _id == 0 ? Loc.T("Purchasing.NewOrder") : Loc.T("Purchasing.EditOrder", OrderNumber);
    public string OrderNumber { get; }
    public List<Supplier> Suppliers { get; private init; } = [];
    public ObservableCollection<PoLineViewModel> Lines { get; } = [];

    [ObservableProperty] public partial Supplier? SelectedSupplier { get; set; }
    [ObservableProperty] public partial DateTime? ExpectedDate { get; set; }
    [ObservableProperty] public partial string? Notes { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial List<ProductVariant> SearchResults { get; set; } = [];
    [ObservableProperty] public partial ProductVariant? SelectedResult { get; set; }

    public decimal Total => Lines.Sum(l => l.LineTotal);
    public int TotalUnits => Lines.Sum(l => l.Quantity);

    private void AddLine(PoLineViewModel line)
    {
        line.PropertyChanged += (_, _) => RaiseTotals();
        Lines.Add(line);
        RaiseTotals();
    }

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalUnits));
    }

    private readonly LatestSearch _search = new();

    partial void OnSearchTextChanged(string value) => _ = LoadResultsAsync(immediately: false);

    [RelayCommand]
    private Task SearchAsync() => LoadResultsAsync(immediately: true);

    private async Task LoadResultsAsync(bool immediately)
    {
        var text = SearchText.Trim();
        if (text.Length == 0)
        {
            _search.Cancel();
            SearchResults = [];
            return;
        }
        try
        {
            Func<CancellationToken, Task<List<ProductVariant>>> load = async ct =>
                await _products.FindByCodeAsync(text, ct) is { } exact ? [exact] : await _products.SearchVariantsAsync(text, 200, ct);
            Action<List<ProductVariant>> apply = found =>
            {
                SearchResults = found;
                SelectedResult = found.FirstOrDefault();
            };
            await (immediately ? _search.RunNowAsync(load, apply) : _search.RunAsync(load, apply));
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Products.SearchFailed"), ex);
        }
    }

    [RelayCommand]
    private void AddResult(ProductVariant? variant)
    {
        variant ??= SelectedResult;
        if (variant is null) return;
        var existing = Lines.FirstOrDefault(l => l.Variant.Id == variant.Id);
        if (existing is not null) existing.Quantity++;
        else AddLine(new PoLineViewModel(variant, 1, variant.EffectiveCost));
    }

    [RelayCommand]
    private void AddAllResults()
    {
        foreach (var v in SearchResults) AddResult(v);
    }

    [RelayCommand]
    private Task AddLowStockAsync() => RunAsync(async () =>
    {
        if (SelectedSupplier is null) throw new BusinessRuleException(Loc.T("Purchasing.ChooseSupplierFirst"));
        var low = (await _inventory.GetStockAsync(lowStockOnly: true))
            .Where(v => v.Product?.SupplierId == SelectedSupplier.Id)
            .ToList();
        if (low.Count == 0)
        {
            Dialogs.Toast(Loc.T("Purchasing.NoLowStock", SelectedSupplier.Name), ToastKind.Info);
            return;
        }
        foreach (var v in low.Where(v => Lines.All(l => l.Variant.Id != v.Id)))
        {
            // Order enough to get back to twice the reorder level.
            var qty = Math.Max(1, v.ReorderLevel * 2 - Math.Max(0, v.StockQuantity));
            AddLine(new PoLineViewModel(v, qty, v.EffectiveCost));
        }
    });

    [RelayCommand]
    private void RemoveLine(PoLineViewModel line)
    {
        Lines.Remove(line);
        RaiseTotals();
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (SelectedSupplier is null) throw new BusinessRuleException(Loc.T("Purchasing.ChooseSupplier"));
        await _orders.SaveAsync(new PurchaseOrder
        {
            Id = _id,
            SupplierId = SelectedSupplier.Id,
            ExpectedDate = ExpectedDate,
            Notes = Notes,
            Lines = Lines.Select(l => new PurchaseOrderLine { ProductVariantId = l.Variant.Id, QuantityOrdered = l.Quantity, UnitCost = l.UnitCost }).ToList(),
        }, _session.User.Id);
        Close(true);
    });

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class ReceiveLineViewModel(PurchaseOrderLine line) : ObservableObject
{
    public PurchaseOrderLine Line { get; } = line;

    [ObservableProperty]
    public partial int ReceiveNow { get; set; } = line.QuantityOutstanding;
}

public sealed partial class ReceivePurchaseOrderViewModel(IDialogService dialogs, PurchaseOrderService orders, Session session, PurchaseOrder order)
    : DialogViewModelBase(dialogs)
{
    public override System.Windows.Size? WindowSize => new(width: 860, height: 520);

    public override string Title => Loc.T("Purchasing.ReceiveTitle", order.OrderNumber, order.Supplier?.Name);
    public List<ReceiveLineViewModel> Lines { get; } = order.Lines.Select(l => new ReceiveLineViewModel(l)).ToList();

    [ObservableProperty]
    public partial bool UpdateCosts { get; set; } = true;

    [RelayCommand]
    private void ReceiveNothing()
    {
        foreach (var l in Lines) l.ReceiveNow = 0;
    }

    [RelayCommand]
    private void ReceiveAll()
    {
        foreach (var l in Lines) l.ReceiveNow = l.Line.QuantityOutstanding;
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var quantities = Lines.Where(l => l.ReceiveNow != 0).ToDictionary(l => l.Line.Id, l => l.ReceiveNow);
        if (quantities.Count == 0) throw new BusinessRuleException(Loc.T("Purchasing.EnterQuantities"));
        var over = Lines.Where(l => l.ReceiveNow > l.Line.QuantityOutstanding).ToList();
        if (over.Count > 0 && !Dialogs.Confirm(Loc.T("Purchasing.OverReceiveConfirm",
                string.Join("\n", over.Select(l => $"{l.Line.ProductVariant?.Product?.Name} {l.Line.ProductVariant?.Description}: {l.ReceiveNow} / {l.Line.QuantityOutstanding}")))))
            return;
        var result = await orders.ReceiveAsync(order.Id, quantities, session.User.Id, UpdateCosts, allowOverReceipt: over.Count > 0);
        Dialogs.Toast(Loc.T("Purchasing.Booked", quantities.Values.Sum(), Loc.EnumText(result.Status)));
        Close(true);
    });

    [RelayCommand]
    private void Cancel() => Close(false);
}
