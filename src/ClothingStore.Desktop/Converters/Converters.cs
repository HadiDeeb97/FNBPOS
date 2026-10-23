using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ClothingStore.Core;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.Converters;

/// <summary>Currency symbol used by <see cref="MoneyConverter"/>; updated when settings load or change.</summary>
public static class CurrencyFormat
{
    public static string Symbol { get; set; } = "$";
    public static string Format(decimal value) => Money.Format(value, Symbol);

    /// <summary>"1,567,000 LBP" in the screen language.</summary>
    public static string Lbp(decimal value) => Core.Pricing.Lbp.Format(value);
}

/// <summary>Formats a number of Lebanese pounds ("1,567,000 LBP").</summary>
public sealed class LbpConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        decimal d => CurrencyFormat.Lbp(d),
        int i => CurrencyFormat.Lbp(i),
        null => "",
        _ => value.ToString() ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        decimal d => CurrencyFormat.Format(d),
        double d => CurrencyFormat.Format((decimal)d),
        int i => CurrencyFormat.Format(i),
        null => "",
        _ => value.ToString() ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true -> Visible. Pass "invert" as parameter to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ parameter is "invert";
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Non-null (and non-empty string/collection) -> Visible. "invert" flips.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            string s => s.Length > 0,
            int i => i != 0,
            decimal d => d != 0,
            ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter is "invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Compares an enum/value to the parameter; used for RadioButton groups bound to an enum.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && value.ToString() == parameter.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string s ? Enum.Parse(targetType, s) : Binding.DoNothing;
}

/// <summary>Translated name of an enum value (see <see cref="Loc.EnumText"/>).</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? Loc.EnumText(e) : value?.ToString() ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>"Morgan Manager" -> "MM", for avatar circles.</summary>
public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var words = (value as string ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Take(2).Select(w => char.ToUpper(w[0], culture)));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>"D:\\Backups\\ClothingStorePOS_20261014-180000.bak" -> just the file name (the full path goes in a tooltip).</summary>
public sealed class FileNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path && path.Length > 0 ? System.IO.Path.GetFileName(path) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Negative numbers -> red-ish brush key name used for variance display.</summary>
public sealed class SignToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var d = value switch { decimal m => m, int i => i, _ => 0m };
        var key = d < 0 ? "DangerBrush" : d > 0 ? "SuccessBrush" : "TextBrush";
        return Application.Current.TryFindResource(key) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>True when every bound value is equal (e.g. an item and the selected item, for a radio button in a list).</summary>
public sealed class AllEqualConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 0 && values.All(v => Equals(v, values[0]));

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
