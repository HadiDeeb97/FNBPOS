using System.Collections.ObjectModel;
using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Data.Services;
using UniPos.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniPos.Core.Localization;

namespace UniPos.Desktop.ViewModels.Dialogs;

/// <summary>A modifier group in the item editor: ticked when the item asks it, in the order shown.</summary>
public sealed partial class GroupLink(ModifierGroup group, bool isChecked) : ObservableObject
{
    public ModifierGroup Group { get; } = group;
    public string Name => Group.Name;

    /// <summary>"Pick 1 · Small, Medium, Large".</summary>
    public string Summary { get; } = ModifierGroupsViewModel.RuleText(group) + " · " + string.Join(", ", group.Options.Select(o => o.Name));

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;
}

/// <summary>Create or edit a menu item: price, code, stock, and the modifier groups it asks.</summary>
public sealed partial class ProductEditorViewModel : DialogViewModelBase
{
    public override System.Windows.Size? WindowSize => new(width: 1040, height: 720);

    private readonly ProductService _products;
    private readonly Session _session;

    /// <summary>The item being edited; 0 for a new one (also after "Save &amp; add new").</summary>
    private int _id;

    private ProductEditorViewModel(IDialogService dialogs, ProductService products, Session session, Product? product) : base(dialogs)
    {
        _products = products;
        _session = session;
        _id = product?.Id ?? 0;
        if (product is null) return;

        Name = product.Name;
        Description = product.Description;
        Code = product.Sku;
        Barcode = product.Barcode;
        Price = product.Price;
        Cost = product.Cost;
        IsActive = product.IsActive;
        SoldOut = product.SoldOut;
        TrackStock = product.TrackStock;
        ReorderLevel = product.ReorderLevel;
        StockOnHand = product.StockQuantity;
    }

    public static async Task<ProductEditorViewModel> CreateAsync(
        IDialogService dialogs, ProductService products, CategoryService categories, SupplierService suppliers,
        ModifierService modifiers, Session session, Product? product)
    {
        var vm = new ProductEditorViewModel(dialogs, products, session, product)
        {
            // Active ones, plus the item's own even if it was deactivated since: otherwise saving would quietly move
            // the item to another category or drop its supplier.
            Categories = (await categories.GetAllAsync(includeInactive: true)).Where(c => c.IsActive || c.Id == product?.CategoryId).ToList(),
            Suppliers = (await suppliers.GetAllAsync(includeInactive: true)).Where(s => s.IsActive || s.Id == product?.SupplierId).ToList(),
        };
        vm.SelectedCategory = vm.Categories.FirstOrDefault(c => c.Id == product?.CategoryId) ?? vm.Categories.FirstOrDefault();
        vm.SelectedSupplier = vm.Suppliers.FirstOrDefault(s => s.Id == product?.SupplierId);

        // The item's groups first, in its order, then the others A–Z.
        var all = await modifiers.GetGroupsAsync(includeInactive: true);
        var own = product?.ModifierGroups.OrderBy(g => g.SortOrder).Select(g => g.ModifierGroupId).ToList() ?? [];
        foreach (var id in own)
            if (all.FirstOrDefault(g => g.Id == id) is { } group) vm.Groups.Add(new GroupLink(group, isChecked: true));
        foreach (var group in all.Where(g => g.IsActive && !own.Contains(g.Id))) vm.Groups.Add(new GroupLink(group, isChecked: false));
        return vm;
    }

    public override string Title => _id == 0 ? Loc.T("ProductEditor.New") : Loc.T("ProductEditor.Edit", Name);

    public bool IsNew => _id == 0;

    /// <summary>"Save &amp; add new" for a new item; for one being edited it saves and starts a similar new one.</summary>
    public string SaveAndNewText => IsNew ? Loc.T("ProductEditor.SaveAndNew") : Loc.T("ProductEditor.SaveAndSimilar");

    /// <summary>"Save &amp; add new" keeps everything but the name for the next item (remembered on this PC).</summary>
    [ObservableProperty]
    public partial bool KeepDetails { get; set; } = LocalPreferences.Current.KeepProductDetails;

    partial void OnKeepDetailsChanged(bool value)
    {
        LocalPreferences.Current.KeepProductDetails = value;
        LocalPreferences.Current.Save();
    }

    /// <summary>"Saved "Nutella crepe" ✓" after "Save &amp; add new", so it's clear the last one went in.</summary>
    [ObservableProperty]
    public partial string? SavedMessage { get; private set; }

    /// <summary>At least one item was saved, even if the window is then cancelled: the list should refresh.</summary>
    public bool SavedAny { get; private set; }

    /// <summary>Asks the view to put the cursor in the name box (ready for the next item).</summary>
    public event EventHandler? FocusNameRequested;

    public List<Category> Categories { get; private init; } = [];
    public List<Supplier> Suppliers { get; private init; } = [];

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string? Description { get; set; }
    [ObservableProperty] public partial string? Code { get; set; }
    [ObservableProperty] public partial string? Barcode { get; set; }
    [ObservableProperty] public partial decimal Price { get; set; }
    [ObservableProperty] public partial decimal Cost { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; } = true;
    [ObservableProperty] public partial bool SoldOut { get; set; }
    [ObservableProperty] public partial bool TrackStock { get; set; }
    [ObservableProperty] public partial int OpeningStock { get; set; }
    [ObservableProperty] public partial int ReorderLevel { get; set; } = 2;
    [ObservableProperty] public partial Category? SelectedCategory { get; set; }
    [ObservableProperty] public partial Supplier? SelectedSupplier { get; set; }

    /// <summary>Stock of an item being edited (changed on the stock screen, not here).</summary>
    public int StockOnHand { get; }

    public string MarginText => Price <= 0 ? "" : Loc.T("ProductEditor.Margin", (Price - Cost) / Price * 100m);

    partial void OnPriceChanged(decimal value) => OnPropertyChanged(nameof(MarginText));
    partial void OnCostChanged(decimal value) => OnPropertyChanged(nameof(MarginText));

    /// <summary>Every modifier group, the ones this item asks ticked, in the order the till asks them.</summary>
    public ObservableCollection<GroupLink> Groups { get; } = [];

    [RelayCommand]
    private void MoveGroupUp(GroupLink link)
    {
        var i = Groups.IndexOf(link);
        if (i > 0) Groups.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveGroupDown(GroupLink link)
    {
        var i = Groups.IndexOf(link);
        if (i >= 0 && i < Groups.Count - 1) Groups.Move(i, i + 1);
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        await SaveProductAsync();
        Close(true);
    });

    /// <summary>
    /// Saves, then stays open for the next item: with the same details (only the name to type, a new code) when "keep
    /// details" is ticked or an item was being edited, else empty.
    /// </summary>
    [RelayCommand]
    private Task SaveAndNewAsync() => RunAsync(async () =>
    {
        var keep = KeepDetails || !IsNew;
        await SaveProductAsync();
        SavedAny = true;
        var savedName = Name.Trim();

        _id = 0;
        Code = Barcode = null;
        OpeningStock = 0;
        if (!keep)
        {
            Description = null;
            Price = Cost = 0;
            IsActive = true;
            SoldOut = TrackStock = false;
            ReorderLevel = 2;
            SelectedCategory = Categories.FirstOrDefault(c => c.IsActive) ?? Categories.FirstOrDefault();
            SelectedSupplier = null;
            foreach (var link in Groups) link.IsChecked = false;
        }
        Name = "";
        SavedMessage = Loc.T("ProductEditor.SavedNext", savedName);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(SaveAndNewText));
        FocusNameRequested?.Invoke(this, EventArgs.Empty);
    });

    private async Task SaveProductAsync()
    {
        if (SelectedCategory is null) throw new BusinessRuleException(Loc.T("ProductEditor.NeedCategory"));
        var product = new Product
        {
            Id = _id,
            Name = Name ?? "",
            Description = Description,
            Sku = Code ?? "",
            Barcode = Barcode,
            Price = Price,
            Cost = Cost,
            IsActive = IsActive,
            SoldOut = SoldOut,
            TrackStock = TrackStock,
            StockQuantity = Math.Max(0, OpeningStock),
            ReorderLevel = ReorderLevel,
            CategoryId = SelectedCategory.Id,
            SupplierId = SelectedSupplier?.Id,
            ModifierGroups = Groups.Where(g => g.IsChecked).Select(g => new ProductModifierGroup { ModifierGroupId = g.Group.Id }).ToList(),
        };
        await _products.SaveAsync(product, _session.User.Id);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
