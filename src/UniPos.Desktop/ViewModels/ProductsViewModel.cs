using System.Collections.Concurrent;
using UniPos.Core.Entities;
using UniPos.Core.Menu;
using UniPos.Core.Security;
using UniPos.Data.Services;
using UniPos.Desktop.Infrastructure;
using UniPos.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniPos.Core.Localization;

namespace UniPos.Desktop.ViewModels;

public sealed record ProductRow(Product Product)
{
    public string PriceText => Converters.CurrencyFormat.Format(Product.Price);

    /// <summary>Stock of a counted item; null for items made to order (nothing to count).</summary>
    public int? Stock => Product.TrackStock ? Product.StockQuantity : null;
    public bool HasLowStock => Product.IsLowStock;

    /// <summary>The questions asked when it is ordered: "Size, Milk, Sugar".</summary>
    public string OptionsText => string.Join(", ", ModifierRules.ActiveGroups(Product).Select(g => g.Name));
}

/// <summary>A page filtered by category (Products, Inventory).</summary>
public interface ICategoryFilter
{
    List<Category> Categories { get; set; }
    Category SelectedCategory { get; set; }
}

public sealed partial class ProductsViewModel(
    IDialogService dialogs, ProductService products, CategoryService categories, SupplierService suppliers,
    ModifierService modifiers, Session session) : ViewModelBase(dialogs), IPageViewModel, ICategoryFilter
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
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand), nameof(ToggleSoldOutCommand))]
    public partial ProductRow? SelectedProduct { get; set; }

    public async Task OnNavigatedToAsync()
    {
        // The list and the category filter load side by side.
        var fresh = categories.GetAllAsync();
        var search = SearchAsync();
        ShowCategories(this, await fresh);
        await search;
    }

    /// <summary>
    /// Puts a fresh category list in the filter box only when it changed, keeping the chosen category. (Replacing it
    /// every visit emptied the box, which started a second search and forgot the category picked.)
    /// </summary>
    internal static void ShowCategories(ICategoryFilter page, List<Category> fresh)
    {
        var shown = page.Categories;
        if (shown.Count == fresh.Count + 1 && shown.Skip(1).Zip(fresh).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name)) return;
        var keep = page.SelectedCategory?.Id ?? 0;
        page.Categories = [AllCategories, .. fresh];
        page.SelectedCategory = page.Categories.FirstOrDefault(c => c.Id == keep) ?? AllCategories;
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
        var editor = await ProductEditorViewModel.CreateAsync(Dialogs, products, categories, suppliers, modifiers, session, null);
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
        var editor = await ProductEditorViewModel.CreateAsync(Dialogs, products, categories, suppliers, modifiers, session, product);
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

    /// <summary>Ran out (or back): the register refuses it until it is back, without editing the item.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ToggleSoldOutAsync()
    {
        if (SelectedProduct is not { } row || !CanEdit) return;
        var soldOut = !row.Product.SoldOut;
        if (await RunAsync(() => products.SetSoldOutAsync(row.Product.Id, soldOut)))
        {
            Dialogs.Toast(Loc.T(soldOut ? "Menu.MarkedSoldOut" : "Menu.MarkedBack", row.Product.Name), soldOut ? ToastKind.Info : ToastKind.Success);
            await SearchAsync();
        }
    }
}
