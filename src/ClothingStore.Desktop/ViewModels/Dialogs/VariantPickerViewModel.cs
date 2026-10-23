using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

/// <summary>One square in the size × colour grid; empty when that combination doesn't exist.</summary>
public sealed record VariantCell(ProductVariant? Variant, bool CanSell)
{
    public bool Exists => Variant is not null;
    public string Size => Variant?.Size ?? "";
    public int Stock => Variant?.StockQuantity ?? 0;
    public bool IsLow => Variant is { IsLowStock: true } || Stock <= 0;
}

public sealed record VariantRow(string Color, IReadOnlyList<VariantCell> Cells);

/// <summary>Pick the size and colour of a product from a grid that shows the stock of each one.</summary>
public sealed partial class VariantPickerViewModel : DialogViewModelBase
{
    private const string None = "—";

    public VariantPickerViewModel(IDialogService dialogs, Product product, IReadOnlyList<ProductVariant> variants, bool allowNegativeStock)
        : base(dialogs)
    {
        Product = product;
        PriceText = ProductRow.PriceRange(variants.Select(v => v.PriceOverride ?? product.Price), product.Price);
        var sizes = variants.Select(v => v.Size).Distinct().ToList();
        SizeHeaders = sizes.Select(s => s.Length == 0 ? None : s).ToList();
        Rows = variants
            .GroupBy(v => v.Color)
            .Select(g => new VariantRow(
                g.Key.Length == 0 ? None : g.Key,
                sizes.Select(size =>
                {
                    var variant = g.FirstOrDefault(v => v.Size == size);
                    return new VariantCell(variant, variant is not null && (allowNegativeStock || variant.StockQuantity > 0));
                }).ToList()))
            .ToList();
    }

    public override string Title => Loc.T("VariantPicker.Title");
    public Product Product { get; }
    public string PriceText { get; }
    public IReadOnlyList<string> SizeHeaders { get; }
    public IReadOnlyList<VariantRow> Rows { get; }
    public ProductVariant? Chosen { get; private set; }

    [RelayCommand]
    private void Choose(VariantCell? cell)
    {
        if (cell is not { CanSell: true, Variant: { } variant }) return;
        Chosen = variant;
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
