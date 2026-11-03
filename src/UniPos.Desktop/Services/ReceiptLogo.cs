using UniPos.Core.Entities;
using UniPos.Desktop.Infrastructure;

namespace UniPos.Desktop.Services;

/// <summary>The logo for the top of sale and return receipts: the store logo when Settings say so and one is set.</summary>
public static class ReceiptLogo
{
    public static byte[]? For(StoreSettings settings) => settings.PrintLogoOnReceipt ? Branding.Instance.LogoImage : null;
}
