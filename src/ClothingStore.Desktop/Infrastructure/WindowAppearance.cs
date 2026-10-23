using System.Windows;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>Settings every window shares: reading direction for the current language and the store logo as icon.</summary>
public static class WindowAppearance
{
    public static void Apply(Window window)
    {
        window.FlowDirection = Loc.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.IsRightToLeft ? "ar-LB" : "en-US");
        if (Branding.Instance.Icon is { } icon) window.Icon = icon;
    }
}
