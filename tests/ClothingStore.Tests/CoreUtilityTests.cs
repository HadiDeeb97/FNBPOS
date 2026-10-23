using ClothingStore.Core;
using ClothingStore.Core.Barcodes;
using ClothingStore.Core.Receipts;
using ClothingStore.Core.Security;

namespace ClothingStore.Tests;

public class CoreUtilityTests
{
    [Theory]
    [InlineData("400638133393", 1)] // 4006381333931 — common reference EAN
    [InlineData("590123412345", 7)] // 5901234123457
    public void Ean13_check_digit(string payload, int expected) =>
        Assert.Equal(expected, Ean13.ComputeCheckDigit(payload));

    [Fact]
    public void Ean13_in_store_codes_are_valid()
    {
        var code = Ean13.CreateInStore(123);
        Assert.Equal(13, code.Length);
        Assert.StartsWith("20", code);
        Assert.True(Ean13.IsValid(code));
        Assert.False(Ean13.IsValid(code[..12] + ((code[12] - '0' + 1) % 10)));
    }

    [Fact]
    public void Code128_patterns_are_well_formed()
    {
        Assert.Equal(107, Code128.Patterns.Length);
        Assert.All(Code128.Patterns.Take(106), p => Assert.Equal(11, p.Sum(c => c - '0')));
        Assert.Equal(13, Code128.Patterns[106].Sum(c => c - '0'));
    }

    [Fact]
    public void Code128_checksum_and_framing()
    {
        // Start B(104) + P(48)*1 + J(42)*2 + J(42)*3 + 1(17)*4 + 2(18)*5 + 3(19)*6 + C(35)*7 = 879; 879 % 103 = 55
        var values = Code128.EncodeValues("PJJ123C");
        Assert.Equal(Code128.StartB, values[0]);
        Assert.Equal(55, values[^2]);
        Assert.Equal(Code128.Stop, values[^1]);

        var widths = Code128.EncodeWidths("PJJ123C");
        Assert.Equal(11 * 9 + 13, widths.Sum()); // start + 7 chars + checksum, then stop
    }

    [Fact]
    public void Password_hash_roundtrip()
    {
        var hash = PasswordHasher.Hash("hunter22");
        Assert.True(PasswordHasher.Verify("hunter22", hash));
        Assert.False(PasswordHasher.Verify("hunter23", hash));
        Assert.NotEqual(hash, PasswordHasher.Hash("hunter22")); // salted
        Assert.False(PasswordHasher.Verify("x", "garbage"));
    }

    [Fact]
    public void Role_permissions()
    {
        Assert.True(Permissions.Has(UserRole.Cashier, Permission.Sell));
        Assert.False(Permissions.Has(UserRole.Cashier, Permission.VoidSales));
        Assert.True(Permissions.Has(UserRole.Manager, Permission.VoidSales));
        Assert.False(Permissions.Has(UserRole.Manager, Permission.ManageUsers));
        Assert.True(Permissions.Has(UserRole.Admin, Permission.ManageUsers));
        Assert.False(Permissions.Has(UserRole.Cashier, Permission.ViewCustomerSpending));
        Assert.True(Permissions.Has(UserRole.Manager, Permission.ViewCustomerSpending));
    }

    [Theory]
    [InlineData("SFOS", "M", "Light Blue", "SFOS-M-LIG")]
    [InlineData("TEE", "XL", "Red", "TEE-XL-RED")]
    [InlineData("BEANIE", "One Size", "", "BEANIE-ONESIZE")]
    public void Sku_generation(string style, string size, string color, string expected) =>
        Assert.Equal(expected, SkuGenerator.Build(style, size, color));

    [Fact]
    public void Sku_made_unique()
    {
        var taken = new HashSet<string> { "A", "A-2" };
        Assert.Equal("A-3", SkuGenerator.MakeUnique("A", taken));
    }

    [Fact]
    public void Receipt_lines_fit_width()
    {
        var doc = new ReceiptDocument
        {
            Title = "Sales Receipt",
            StoreName = "Threads & Co",
            StoreAddress = "1 Main Street\nSpringfield",
            Number = "R20260101-0001",
            Date = new DateTime(2026, 1, 1, 10, 30, 0),
            Cashier = "Casey",
            Lines = [new ReceiptLine("A very long product name that will need to wrap onto another line", "M / Black", 2, 19.99m, 4m, 35.98m)],
            Subtotal = 39.98m, Discount = 4m, Tax = 3.40m, TaxRate = 10, Total = 37.38m,
            Payments = [new ReceiptPayment("Cash", 40m)],
            Change = 2.62m,
            Footer = "Thanks!",
        };

        var lines = ReceiptFormatter.Format(doc, 32);
        Assert.All(lines, l => Assert.True(l.Length <= 32, $"'{l}' is too long"));
        Assert.Contains(lines, l => l.StartsWith("TOTAL") && l.EndsWith("$37.38"));
        Assert.Contains(lines, l => l.StartsWith("Change") && l.EndsWith("$2.62"));
    }
}
