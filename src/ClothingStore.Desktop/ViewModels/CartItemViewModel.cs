using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Pricing;
using ClothingStore.Desktop.Converters;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClothingStore.Desktop.ViewModels;

public sealed partial class CartItemViewModel : ObservableObject
{
    public CartItemViewModel(ProductVariant variant, int quantity = 1)
    {
        VariantId = variant.Id;
        ProductName = variant.Product?.Name ?? variant.Sku;
        VariantDescription = variant.Description;
        Sku = variant.Sku;
        UnitPrice = variant.EffectivePrice;
        StockOnHand = variant.StockQuantity;
        Quantity = quantity;
    }

    public int VariantId { get; }
    public string ProductName { get; }
    public string VariantDescription { get; }
    public string Sku { get; }
    /// <summary>Shelf price when added; refreshed before payment in case it was changed meanwhile.</summary>
    [ObservableProperty]
    public partial decimal UnitPrice { get; private set; }

    /// <summary>Takes the current shelf price; true when it differed.</summary>
    public bool Reprice(decimal price)
    {
        if (price == UnitPrice) return false;
        UnitPrice = price;
        return true;
    }
    public int StockOnHand { get; }

    [ObservableProperty]
    public partial int Quantity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscountDisplay))]
    public partial DiscountType DiscountType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscountDisplay))]
    public partial decimal DiscountValue { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscountDisplay))]
    public partial decimal Discount { get; private set; }

    [ObservableProperty]
    public partial decimal Total { get; private set; }

    public string DiscountDisplay => Discount == 0
        ? ""
        : DiscountType == DiscountType.Percent
            ? $"-{CurrencyFormat.Format(Discount)} ({DiscountValue:0.##}%)"
            : $"-{CurrencyFormat.Format(Discount)}";

    public CartLineInput ToInput() => new(UnitPrice, Quantity, DiscountType, DiscountValue);

    public void Apply(CartLineResult result)
    {
        Discount = result.Discount;
        Total = result.Total;
    }
}
