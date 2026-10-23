namespace ClothingStore.Core;

public static class Money
{
    /// <summary>Rounds to 2 decimals using commercial rounding (0.005 -> 0.01).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static string Format(decimal value, string currencySymbol) =>
        value < 0
            ? $"-{currencySymbol}{Math.Abs(value):N2}"
            : $"{currencySymbol}{value:N2}";
}
