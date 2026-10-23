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

public sealed partial class InventoryViewModel(
    IDialogService dialogs, InventoryService inventory, CategoryService categories, ReportService reports,
    Session session, PrintService print, ProductService products, SettingsService settings) : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.Inventory");

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial List<Category> Categories { get; set; } = [ProductsViewModel.AllCategories];

    [ObservableProperty]
    public partial Category SelectedCategory { get; set; } = ProductsViewModel.AllCategories;

    [ObservableProperty]
    public partial bool LowStockOnly { get; set; }

    [ObservableProperty]
    public partial List<ProductVariant> Items { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdjustCommand), nameof(CountCommand), nameof(ReorderLevelCommand))]
    public partial ProductVariant? SelectedItem { get; set; }

    [ObservableProperty]
    public partial List<StockMovement> Movements { get; set; } = [];

    [ObservableProperty]
    public partial InventoryValuation? Valuation { get; set; }

    public async Task OnNavigatedToAsync()
    {
        Categories = [ProductsViewModel.AllCategories, .. await categories.GetAllAsync()];
        await RefreshAsync();
    }

    private readonly LatestSearch _search = new();

    partial void OnSearchTextChanged(string value) => _ = LoadItemsAsync(immediately: false);
    partial void OnSelectedCategoryChanged(Category value) => _ = LoadItemsAsync();
    partial void OnLowStockOnlyChanged(bool value) => _ = LoadItemsAsync();

    async partial void OnSelectedItemChanged(ProductVariant? value)
    {
        try
        {
            var movements = value is null ? [] : await inventory.GetMovementsAsync(value.Id, max: 200);
            if (ReferenceEquals(SelectedItem, value)) Movements = movements; // a slower answer for another item is dropped
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Inventory.HistoryFailed"), ex);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadItemsAsync();
        try
        {
            Valuation = await reports.GetInventoryValuationAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Inventory.ValuationFailed"), ex);
        }
    }

    private async Task LoadItemsAsync(bool immediately = true)
    {
        var selectedId = SelectedItem?.Id;
        var text = SearchText;
        var categoryId = SelectedCategory is { Id: > 0 } c ? c.Id : (int?)null;
        var lowStockOnly = LowStockOnly;
        try
        {
            Func<CancellationToken, Task<List<ProductVariant>>> load = ct => inventory.GetStockAsync(text, categoryId, lowStockOnly, ct: ct);
            Action<List<ProductVariant>> apply = found =>
            {
                Items = found;
                SelectedItem = Items.FirstOrDefault(i => i.Id == selectedId) ?? Items.FirstOrDefault();
            };
            await (immediately ? _search.RunNowAsync(load, apply) : _search.RunAsync(load, apply));
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Inventory.LoadFailed"), ex);
        }
    }

    private bool HasSelection() => SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task AdjustAsync()
    {
        if (SelectedItem is null) return;
        if (Dialogs.ShowDialog(new StockAdjustViewModel(Dialogs, inventory, session, SelectedItem))) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CountAsync()
    {
        if (SelectedItem is not { } item) return;
        var counted = Dialogs.PromptInt(Loc.T("Inventory.StockCount"), Loc.T("Inventory.StockCountPrompt", item.DisplayName, item.Sku, item.StockQuantity), item.StockQuantity);
        if (counted is null) return;
        if (await RunAsync(() => inventory.SetCountedStockAsync(item.Id, counted.Value, Loc.T("Inventory.StockCount"), session.User.Id)))
        {
            Dialogs.Toast(Loc.T("Inventory.CountSaved", item.DisplayName, counted.Value));
            await RefreshAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ReorderLevelAsync()
    {
        if (SelectedItem is not { } item) return;
        var level = Dialogs.PromptInt(Loc.T("Inventory.ReorderLevel"), Loc.T("Inventory.ReorderPrompt", item.DisplayName), item.ReorderLevel);
        if (level is null) return;
        if (await RunAsync(() => inventory.UpdateReorderLevelAsync(item.Id, level.Value))) await LoadItemsAsync();
    }

    /// <summary>Rows ticked in the stock list (Ctrl/Shift+click).</summary>
    public System.Collections.ObjectModel.ObservableCollection<object> Selection { get; } = [];

    /// <summary>Labels for the selected rows (Ctrl+A selects everything listed); with nothing selected, for everything listed.</summary>
    [RelayCommand]
    private void PrintLabels()
    {
        var selected = Selection.OfType<ProductVariant>().ToList();
        List<ProductVariant> targets = selected.Count > 0 ? selected : Items;
        if (targets.Count > 200 && !Dialogs.Confirm(Loc.T("Labels.ManyItemsConfirm", targets.Count))) return;
        Dialogs.ShowDialog(new LabelPrintViewModel(Dialogs, print, products, settings, targets));
    }

    [RelayCommand]
    private void Export()
    {
        var path = Dialogs.SaveFile(Loc.T("Inventory.ExportTitle"), Loc.T("Common.CsvFilter"), $"stock_{DateTime.Today:yyyyMMdd}.csv");
        if (path is null) return;
        try
        {
            CsvExporter.Write(path,
                [Loc.T("Common.Product"), Loc.T("Common.Brand"), Loc.T("Common.Category"), Loc.T("Common.Size"), Loc.T("Common.Colour"),
                    Loc.T("Common.Sku"), Loc.T("Common.Barcode"), Loc.T("Common.Stock"), Loc.T("Inventory.ReorderLevel"), Loc.T("Common.Cost"),
                    Loc.T("Common.Price"), Loc.T("Inventory.StockValueCost")],
                Items.Select(v => new object?[]
                {
                    v.Product?.Name, v.Product?.Brand, v.Product?.Category?.Name, v.Size, v.Color, v.Sku, v.Barcode,
                    v.StockQuantity, v.ReorderLevel, v.EffectiveCost, v.EffectivePrice, Money.Round(Math.Max(0, v.StockQuantity) * v.EffectiveCost),
                }));
            Dialogs.Toast(Loc.T("Common.Exported", Items.Count));
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.ExportFailed"), ex);
        }
    }
}

public sealed record AdjustmentReason(string Label, StockMovementType Type, int Sign);

public sealed partial class StockAdjustViewModel(IDialogService dialogs, InventoryService inventory, Session session, ProductVariant item)
    : DialogViewModelBase(dialogs)
{
    public override string Title => Loc.T("Inventory.AdjustStock");
    public ProductVariant Item { get; } = item;

    public AdjustmentReason[] Reasons { get; } =
    [
        new(Loc.T("Inventory.Reason.Received"), StockMovementType.PurchaseReceipt, +1),
        new(Loc.T("Inventory.Reason.Damaged"), StockMovementType.Damaged, -1),
        new(Loc.T("Inventory.Reason.Lost"), StockMovementType.Adjustment, -1),
        new(Loc.T("Inventory.Reason.ReturnedToSupplier"), StockMovementType.Adjustment, -1),
        new(Loc.T("Inventory.Reason.FoundPlus"), StockMovementType.Adjustment, +1),
        new(Loc.T("Inventory.Reason.CorrectionMinus"), StockMovementType.Adjustment, -1),
    ];

    [ObservableProperty]
    public partial AdjustmentReason? SelectedReason { get; set; }

    [ObservableProperty]
    public partial int Quantity { get; set; } = 1;

    [ObservableProperty]
    public partial string? Notes { get; set; }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (SelectedReason is null) throw new BusinessRuleException(Loc.T("Inventory.ChooseReason"));
        if (Quantity <= 0) throw new BusinessRuleException(Loc.T("Common.QuantityAtLeastOne"));
        var notes = string.IsNullOrWhiteSpace(Notes) ? SelectedReason.Label : $"{SelectedReason.Label}: {Notes}";
        await inventory.AdjustStockAsync(Item.Id, SelectedReason.Sign * Quantity, SelectedReason.Type, notes, session.User.Id);
        Close(true);
    });

    [RelayCommand]
    private void Cancel() => Close(false);
}
