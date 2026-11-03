using System.Windows;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>Settings every window shares: reading direction for the current language and the store logo as icon.</summary>
public static class WindowAppearance
{
    public static void Apply(Window window)
    {
        window.FlowDirection = Loc.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        // Arabic screens get Arabic text rules. English ones leave the language unset, so date pickers write dates the way
        // this PC does (Windows' region settings) instead of always the American way.
        if (Loc.IsRightToLeft) window.Language = System.Windows.Markup.XmlLanguage.GetLanguage("ar-LB");
        else window.ClearValue(FrameworkElement.LanguageProperty);
        if (Branding.Instance.Icon is { } icon) window.Icon = icon;
    }
}
