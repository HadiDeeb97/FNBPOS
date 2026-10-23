using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.ViewModels;

public sealed partial class SuppliersViewModel(IDialogService dialogs, SupplierService suppliers) : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.Suppliers");

    [ObservableProperty]
    public partial List<Supplier> Suppliers { get; set; } = [];

    [ObservableProperty]
    public partial Supplier? SelectedSupplier { get; set; }

    /// <summary>Working copy shown in the form.</summary>
    [ObservableProperty]
    public partial Supplier Editing { get; set; } = new();

    public async Task OnNavigatedToAsync() => await LoadAsync();

    partial void OnSelectedSupplierChanged(Supplier? value)
    {
        if (value is null) return;
        Editing = new Supplier
        {
            Id = value.Id, Name = value.Name, ContactName = value.ContactName, Phone = value.Phone,
            Email = value.Email, Address = value.Address, Notes = value.Notes, IsActive = value.IsActive,
        };
    }

    private async Task LoadAsync(int? selectId = null)
    {
        Suppliers = await suppliers.GetAllAsync(includeInactive: true);
        SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == selectId) ?? Suppliers.FirstOrDefault();
        if (SelectedSupplier is null) Editing = new Supplier();
    }

    [RelayCommand]
    private void New()
    {
        SelectedSupplier = null;
        Editing = new Supplier();
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var saved = await suppliers.SaveAsync(Editing);
        await LoadAsync(saved.Id);
        Dialogs.Toast(Loc.T("Common.Saved"));
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Editing.Id == 0 || !Dialogs.Confirm(Loc.T("Suppliers.DeleteConfirm", Editing.Name))) return;
        if (!await suppliers.DeleteAsync(Editing.Id))
            Dialogs.Toast(Loc.T("Suppliers.Deactivated"), ToastKind.Info);
        await LoadAsync();
    });
}

public sealed partial class CategoriesViewModel(IDialogService dialogs, CategoryService categories) : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.Categories");

    [ObservableProperty]
    public partial List<Category> Categories { get; set; } = [];

    [ObservableProperty]
    public partial Category? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial Category Editing { get; set; } = new();

    public async Task OnNavigatedToAsync() => await LoadAsync();

    partial void OnSelectedCategoryChanged(Category? value)
    {
        if (value is null) return;
        Editing = new Category { Id = value.Id, Name = value.Name, Description = value.Description, IsActive = value.IsActive };
    }

    private async Task LoadAsync(int? selectId = null)
    {
        Categories = await categories.GetAllAsync(includeInactive: true);
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == selectId) ?? Categories.FirstOrDefault();
        if (SelectedCategory is null) Editing = new Category();
    }

    [RelayCommand]
    private void New()
    {
        SelectedCategory = null;
        Editing = new Category();
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var saved = await categories.SaveAsync(Editing);
        await LoadAsync(saved.Id);
        Dialogs.Toast(Loc.T("Common.Saved"));
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Editing.Id == 0 || !Dialogs.Confirm(Loc.T("Categories.DeleteConfirm", Editing.Name))) return;
        if (!await categories.DeleteAsync(Editing.Id))
            Dialogs.Toast(Loc.T("Categories.Deactivated"), ToastKind.Info);
        await LoadAsync();
    });
}
