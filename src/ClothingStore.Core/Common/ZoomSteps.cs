namespace ClothingStore.Core;

/// <summary>
/// The text sizes (zoom levels) a till can use, from 90% to 150%. A bigger zoom leaves less room on the screen, so a
/// screen only offers the steps that still give the POS the width it was designed for.
/// </summary>
public static class ZoomSteps
{
    public static IReadOnlyList<double> All { get; } = [0.9, 1.0, 1.1, 1.25, 1.4, 1.5];

    /// <summary>Width (in screen units at 100%) the main window is designed for.</summary>
    public const double DesignWidth = 1024;

    /// <summary>The largest step that still leaves <see cref="DesignWidth"/> on a screen this wide (100% at the least).</summary>
    public static double MaxFor(double screenWidth) =>
        All.Where(s => s <= 1.0 || screenWidth / s >= DesignWidth).DefaultIfEmpty(1.0).Max();

    /// <summary>The nearest step to <paramref name="value"/>, no bigger than <paramref name="max"/>.</summary>
    public static double Clamp(double value, double max)
    {
        var allowed = All.Where(s => s <= max + 0.001).ToList();
        if (double.IsNaN(value) || value <= 0) return 1.0;
        return allowed.OrderBy(s => Math.Abs(s - value)).First();
    }

    /// <summary>One step bigger, if the screen allows it.</summary>
    public static double Larger(double current, double max) =>
        All.FirstOrDefault(s => s > current + 0.001 && s <= max + 0.001, Clamp(current, max));

    /// <summary>One step smaller (90% at the least).</summary>
    public static double Smaller(double current) =>
        All.LastOrDefault(s => s < current - 0.001, All[0]);
}
