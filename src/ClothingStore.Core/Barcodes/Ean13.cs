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
    private static readonly string[] L = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    private static readonly string[] G = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
    private static readonly string[] R = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
    private static readonly string[] Parity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    /// <summary>
    /// Module widths (95 modules), alternating bar, space, bar, ... starting with a bar,
    /// the same shape as <see cref="Code128.EncodeWidths"/>. Quiet zone is not included (leave 11 modules each side).
    /// </summary>
    public static IReadOnlyList<int> EncodeWidths(string code)
    {
        if (!IsValid(code))
            throw new ArgumentException("A valid 13-digit EAN-13 (correct check digit) is required.", nameof(code));

        var bits = new System.Text.StringBuilder(95);
        bits.Append("101");
        var parity = Parity[code[0] - '0'];
        for (var i = 1; i <= 6; i++)
            bits.Append((parity[i - 1] == 'L' ? L : G)[code[i] - '0']);
        bits.Append("01010");
        for (var i = 7; i <= 12; i++)
            bits.Append(R[code[i] - '0']);
        bits.Append("101");

        var widths = new List<int>();
        var run = 1;
        for (var i = 1; i < bits.Length; i++)
        {
            if (bits[i] == bits[i - 1]) run++;
            else { widths.Add(run); run = 1; }
        }
        widths.Add(run);
        return widths;
    }
}
