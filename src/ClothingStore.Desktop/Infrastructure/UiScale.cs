using System.Windows;
using System.Windows.Media;
using ClothingStore.Core;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Text size of the POS on this PC (90%–150%). Everything is zoomed together, text, buttons, tables and boxes, so bigger
/// text never gets cut off inside a fixed-size box. Set <c>UiScale.IsRoot="True"</c> on a window's root content element.
/// </summary>
public static class UiScale
{
    /// <summary>The largest zoom this screen allows (it still leaves the width the POS is designed for).</summary>
    public static double Max => ZoomSteps.MaxFor(SystemParameters.WorkArea.Width);

    public static double Current => ZoomSteps.Clamp(LocalPreferences.Current.UiScale, Max);

    public static int Percent => (int)Math.Round(Current * 100);

    public static bool CanGrow => ZoomSteps.Larger(Current, Max) > Current + 0.001;
    public static bool CanShrink => ZoomSteps.Smaller(Current) < Current - 0.001;

    /// <summary>Raised after the zoom changed (all zoom roots update themselves).</summary>
    public static event EventHandler? Changed;

    public static void Larger() => Set(ZoomSteps.Larger(Current, Max));
    public static void Smaller() => Set(ZoomSteps.Smaller(Current));
    public static void Reset() => Set(1.0);

    public static void Set(double value)
    {
        var scale = ZoomSteps.Clamp(value, Max);
        if (Math.Abs(scale - Current) < 0.001) return;
        LocalPreferences.Current.UiScale = scale;
        LocalPreferences.Current.Save();
        Changed?.Invoke(null, EventArgs.Empty);
        if (Application.Current?.MainWindow is Views.MainWindow { IsVisible: true })
            ToastHost.Instance.Show(Loc.T("Shell.TextSizeChanged", Percent), ToastKind.Info);
    }

    // ---- Attached property: the element whose content is zoomed ----

    public static readonly DependencyProperty IsRootProperty = DependencyProperty.RegisterAttached(
        "IsRoot", typeof(bool), typeof(UiScale), new PropertyMetadata(false, OnIsRootChanged));

    public static bool GetIsRoot(DependencyObject element) => (bool)element.GetValue(IsRootProperty);
    public static void SetIsRoot(DependencyObject element, bool value) => element.SetValue(IsRootProperty, value);

    private static void OnIsRootChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        Apply(element);
        void OnChanged(object? s, EventArgs a) => Apply(element);
        element.Loaded += (_, _) =>
        {
            Changed -= OnChanged;
            Changed += OnChanged;
            Apply(element);
        };
        element.Unloaded += (_, _) => Changed -= OnChanged;
    }

    private static void Apply(FrameworkElement element)
    {
        var scale = Current;
        var normal = Math.Abs(scale - 1.0) < 0.001;
        element.LayoutTransform = normal ? Transform.Identity : new ScaleTransform(scale, scale);
        // "Display" text is laid out for one size and looks soft once zoomed; "Ideal" text stays sharp at any size.
        if (normal) element.ClearValue(TextOptions.TextFormattingModeProperty);
        else TextOptions.SetTextFormattingMode(element, TextFormattingMode.Ideal);
    }
}
