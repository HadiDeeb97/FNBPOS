using ClothingStore.Core.Entities;
using ClothingStore.Desktop.Infrastructure;

namespace ClothingStore.Desktop.Services;

/// <summary>The logo for the top of sale and return receipts: the store logo when Settings say so and one is set.</summary>
public static class ReceiptLogo
{
    public static byte[]? For(StoreSettings settings) => settings.PrintLogoOnReceipt ? Branding.Instance.LogoImage : null;
}
