using UniPos.Core;
using UniPos.Core.Receipts;
using UniPos.Core.Security;

namespace UniPos.Tests;

public class CoreUtilityTests
{
    [Fact]
    public void A_note_typed_on_several_lines_prints_on_several_lines_within_the_receipt_width()
    {
        var lines = ReceiptFormatter.Wrap("Note: Hamra street, building 4\r\nInsta @rami_style\nGift wrap please, with a card saying happy birthday", 20).ToList();

        Assert.All(lines, l => Assert.True(l.Length <= 20 && !l.Contains('\n') && !l.Contains('\r'), l));
        Assert.Equal("Note: Hamra street,", lines[0]);
        Assert.Contains("Insta @rami_style", lines);
        Assert.Equal("Gift wrap please,", lines[lines.IndexOf("Insta @rami_style") + 1]);
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
            Lines = [new ReceiptLine("A very long item name that will need to wrap onto another line", "Large · Nutella, Banana, Strawberry, White chocolate", 2, 19.99m, 4m, 35.98m)],
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
