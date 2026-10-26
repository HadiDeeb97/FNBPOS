using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Pricing;
using ClothingStore.Core.Localization;
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
        UnitPrice = ShelfPrice = variant.EffectivePrice;
        UnitCost = variant.EffectiveCost;
        StockOnHand = variant.StockQuantity;
        Quantity = quantity;
    }

    public int VariantId { get; }
    public string ProductName { get; }
    public string VariantDescription { get; }
    public string Sku { get; }
    /// <summary>The price charged: the shelf price, or the one the cashier typed in the price box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriceText), nameof(PriceChanged))]
    public partial decimal UnitPrice { get; private set; }

    /// <summary>Shelf price when added; refreshed before payment in case it was changed meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriceChanged))]
    public partial decimal ShelfPrice { get; private set; }

    public decimal UnitCost { get; private set; }

    public bool PriceChanged => UnitPrice != ShelfPrice;

    /// <summary>Managers see what the item cost next to the "below cost" warning; cashiers only see the warning.</summary>
    public bool ShowCost { get; init; }

    /// <summary>What the price box shows; typing in it asks the register to change the price (<see cref="PriceTyped"/>).</summary>
    public string PriceText
    {
        get => UnitPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            // Handled once the box has finished saving: the approval window that may open takes the focus, and the box
            // losing it would otherwise start a second save of the same price while this one is still running.
            void Handle()
            {
                PriceTyped?.Invoke(this, value);
                OnPropertyChanged(nameof(PriceText)); // shows the price that was kept, even when the typed one was refused
            }
            if (System.Windows.Application.Current?.Dispatcher is { } dispatcher) dispatcher.BeginInvoke(Handle);
            else Handle();
        }
    }

    public event EventHandler<string>? PriceTyped;

    public void SetPrice(decimal price) => UnitPrice = price;

    /// <summary>
    /// Takes the current shelf price and cost; true when the price charged changed with it. A price the cashier typed
    /// stays as it is.
    /// </summary>
    public bool Reprice(decimal shelfPrice, decimal cost)
    {
        UnitCost = cost;
        var typed = PriceChanged;
        var changed = !typed && shelfPrice != UnitPrice;
        ShelfPrice = shelfPrice;
        if (!typed) UnitPrice = shelfPrice;
        return changed;
    }

    /// <summary>The line brings in less than it cost (after its discounts, without tax).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BelowCostText))]
    public partial bool IsBelowCost { get; private set; }

    public string BelowCostText => ShowCost
        ? Loc.T("Register.BelowCostWithCost", CurrencyFormat.Format(UnitCost))
        : Loc.T("Register.BelowCost");

    /// <summary>What one sold for (after discounts, without tax) when a manager approved selling it below cost.</summary>
    public decimal? BelowCostApprovedAt { get; set; }

    /// <summary>What one brings in now, after its discounts and without tax.</summary>
    public decimal NetEach { get; private set; }

    public bool NeedsBelowCostApproval => IsBelowCost && (BelowCostApprovedAt is not { } approved || NetEach < approved);
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
        NetEach = Quantity > 0 ? (result.Total - result.Tax) / Quantity : 0;
        IsBelowCost = PriceRules.IsBelowCost(result, Quantity, UnitCost);
    }
}
