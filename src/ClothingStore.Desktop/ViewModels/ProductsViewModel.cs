using System.Collections.Concurrent;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Security;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.ViewModels;

public sealed record ProductRow(Product Product)
{
    public int TotalStock => Product.Variants.Where(v => v.IsActive).Sum(v => v.StockQuantity);
    public int VariantCount => Product.Variants.Count(v => v.IsActive);
    public string Sizes => string.Join(", ", Product.Variants.Where(v => v.IsActive).Select(v => v.Size).Where(s => s.Length > 0).Distinct());
    public string Colors => string.Join(", ", Product.Variants.Where(v => v.IsActive).Select(v => v.Color).Where(s => s.Length > 0).Distinct());
    public bool HasLowStock => Product.Variants.Any(v => v.IsActive && v.IsLowStock);

    /// <summary>The price, or "from – to" when some sizes/colours have their own price.</summary>
    public string PriceText => PriceRange(Product.Variants.Where(v => v.IsActive).Select(v => v.PriceOverride ?? Product.Price), Product.Price);

    public static string PriceRange(IEnumerable<decimal> prices, decimal fallback)
    {
        var list = prices.ToList();
        if (list.Count == 0) return Converters.CurrencyFormat.Format(fallback);
        var (min, max) = (list.Min(), list.Max());
        return min == max ? Converters.CurrencyFormat.Format(min) : $"{Converters.CurrencyFormat.Format(min)} – {Converters.CurrencyFormat.Format(max)}";
    }
}

public sealed partial class ProductsViewModel(
    IDialogService dialogs, ProductService products, CategoryService categories, SupplierService suppliers,
    Session session, PrintService print, SettingsService settings, SizePresetService sizePresets) : ViewModelBase(dialogs), IPageViewModel
{
    /// <summary>The "All categories" filter entry (one per language, so selecting it by reference keeps working).</summary>
    public static Category AllCategories =>
        AllCategoriesByLanguage.GetOrAdd(Loc.Language, _ => new Category { Id = 0, Name = Loc.T("Common.AllCategories") });

    private static readonly ConcurrentDictionary<string, Category> AllCategoriesByLanguage = new();

    public string Title => Loc.T("Nav.Products");
    public bool CanEdit => session.Can(Permission.ManageProducts);

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial List<Category> Categories { get; set; } = [AllCategories];

    [ObservableProperty]
    public partial Category SelectedCategory { get; set; } = AllCategories;

    [ObservableProperty]
    public partial bool IncludeInactive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCapped))]
    public partial List<ProductRow> Products { get; set; } = [];

    /// <summary>Only the first products (A to Z) are listed: a search or category narrows it.</summary>
    public bool IsCapped => Products.Count >= ProductService.ProductListLimit;
    public string CappedText => Loc.T("Products.Capped", ProductService.ProductListLimit);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand), nameof(PrintLabelsCommand))]
    public partial ProductRow? SelectedProduct { get; set; }

    public async Task OnNavigatedToAsync()
    {
        Categories = [AllCategories, .. await categories.GetAllAsync()];
        await SearchAsync();
    }

    private readonly LatestSearch _search = new();

    partial void OnSearchTextChanged(string value) => _ = LoadAsync(immediately: false);
    partial void OnSelectedCategoryChanged(Category value) => _ = SearchAsync();
    partial void OnIncludeInactiveChanged(bool value) => _ = SearchAsync();

    [RelayCommand]
    private Task SearchAsync() => LoadAsync(immediately: true);

    private async Task LoadAsync(bool immediately)
    {
        var selectedId = SelectedProduct?.Product.Id;
        var text = SearchText;
        var categoryId = SelectedCategory is { Id: > 0 } c ? c.Id : (int?)null;
        var includeInactive = IncludeInactive;
        try
        {
            Func<CancellationToken, Task<List<Product>>> load = ct =>
                SearchTiming.TimeAsync("Products page", text ?? "", () => products.SearchAsync(text, categoryId, includeInactive, ct), products, r => r.Count);
            Action<List<Product>> apply = found =>
            {
                Products = found.Select(p => new ProductRow(p)).ToList();
                SelectedProduct = Products.FirstOrDefault(p => p.Product.Id == selectedId) ?? Products.FirstOrDefault();
            };
            await (immediately ? _search.RunNowAsync(load, apply) : _search.RunAsync(load, apply));
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Products.SearchFailed"), ex);
        }
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!CanEdit) return;
        var editor = await ProductEditorViewModel.CreateAsync(Dialogs, products, categories, suppliers, sizePresets, session, null);
        // Several may have been saved with "Save & add new" before the window was closed or cancelled.
        if (Dialogs.ShowDialog(editor) | editor.SavedAny) await SearchAsync();
    }

    private bool HasSelection() => SelectedProduct is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        if (SelectedProduct is null || !CanEdit) return;
        var product = await products.GetAsync(SelectedProduct.Product.Id);
        if (product is null) return;
        var editor = await ProductEditorViewModel.CreateAsync(Dialogs, products, categories, suppliers, sizePresets, session, product);
        if (Dialogs.ShowDialog(editor) | editor.SavedAny) await SearchAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (SelectedProduct is not { } row || !CanEdit) return;
        if (!Dialogs.Confirm(Loc.T("Products.DeleteConfirm", row.Product.Name)))
            return;
        var deleted = false;
        if (await RunAsync(async () => deleted = await products.DeleteAsync(row.Product.Id)))
        {
            Dialogs.Toast(deleted ? Loc.T("Products.Deleted", row.Product.Name) : Loc.T("Products.Deactivated"), deleted ? ToastKind.Success : ToastKind.Info);
            await SearchAsync();
        }
    }

    /// <summary>Rows ticked in the list (Ctrl/Shift+click); labels are printed for all of them.</summary>
    public System.Collections.ObjectModel.ObservableCollection<object> Selection { get; } = [];

    /// <summary>Labels for every size/colour of the selected products. With nothing selected, opens an empty list to scan into.</summary>
    [RelayCommand]
    private void PrintLabels()
    {
        var rows = Selection.OfType<ProductRow>().ToList();
        if (rows.Count == 0 && SelectedProduct is not null) rows.Add(SelectedProduct);
        var variants = rows
            .SelectMany(r => r.Product.Variants.Where(v => v.IsActive).Select(v => { v.Product = r.Product; return v; }))
            .ToList();
        Dialogs.ShowDialog(new LabelPrintViewModel(Dialogs, print, products, settings, variants));
    }
}
