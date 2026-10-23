namespace ClothingStore.Core.Barcodes;

/// <summary>EAN-13 helpers. In-store barcodes use the GS1 restricted-circulation prefix "20".</summary>
public static class Ean13
{
    public const string InStorePrefix = "20";

    public static int ComputeCheckDigit(string twelveDigits)
    {
        if (twelveDigits.Length != 12 || !twelveDigits.All(char.IsAsciiDigit))
            throw new ArgumentException("EAN-13 payload must be exactly 12 digits.", nameof(twelveDigits));

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = twelveDigits[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }
        return (10 - sum % 10) % 10;
    }

    public static bool IsValid(string? code) =>
        code is { Length: 13 } && code.All(char.IsAsciiDigit) && ComputeCheckDigit(code[..12]) == code[12] - '0';

    /// <summary>Creates an in-store EAN-13 from a sequence number (max 10 digits).</summary>
    public static string CreateInStore(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, 9_999_999_999L);
        var payload = InStorePrefix + sequence.ToString("D10");
        return payload + ComputeCheckDigit(payload);
    }
}
