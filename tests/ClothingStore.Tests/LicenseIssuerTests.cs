using System.Security.Cryptography;
using ClothingStore.Core.Licensing;

namespace ClothingStore.Tests;

/// <summary>The vendor side used by the License Maker app and pos-license.</summary>
public class LicenseIssuerTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    public void Pasted_pc_ids_are_cleaned_up_and_bad_ones_reported()
    {
        var ids = LicenseIssuer.ParseMachineIds("7kq2 m9xd abcd efgh\r\n7KQ2-M9XD-ABCD-EFGH, AAAABBBBCCCCDDDD; oops", out var invalid);
        Assert.Equal(["7KQ2-M9XD-ABCD-EFGH", "AAAA-BBBB-CCCC-DDDD"], ids);
        Assert.Equal(["oops"], invalid);
    }

    [Fact]
    public void Durations_and_early_renewal_keep_the_remaining_days()
    {
        Assert.Equal(new DateOnly(2027, 10, 7), LicenseIssuer.ExpiryFor(LicenseDuration.OneYear, Today));
        Assert.Equal(new DateOnly(2026, 11, 7), LicenseIssuer.ExpiryFor(LicenseDuration.OneMonth, Today));
        Assert.True(LicenseIssuer.IsLifetime(LicenseIssuer.ExpiryFor(LicenseDuration.Lifetime, Today)));

        // Renewed 10 days early: the new year starts after the old last day. Renewed late: from today.
        var early = LicenseIssuer.RenewFrom(Today.AddDays(10), Today);
        Assert.Equal(Today.AddDays(10).AddYears(1), LicenseIssuer.ExpiryFor(LicenseDuration.OneYear, early));
        Assert.Equal(Today, LicenseIssuer.RenewFrom(Today.AddDays(-5), Today));
    }

    [Fact]
    public void An_issued_key_works_in_the_app_and_a_lifetime_key_never_warns()
    {
        var (privatePem, publicPem) = LicenseIssuer.CreateKeys();
        using var privateKey = ECDsa.Create();
        privateKey.ImportFromPem(privatePem);
        using var publicKey = LicenseCodec.ImportPublicKey(publicPem);

        var (license, key) = LicenseIssuer.Issue(privateKey, " Boutique Rana ", ["7KQ2-M9XD-ABCD-EFGH"], Today, LicenseIssuer.LifetimeEnd);
        Assert.Equal("Boutique Rana", license.Licensee);
        Assert.Equal(LicenseState.Valid, LicenseStatus.Evaluate(key, publicKey, "7KQ2M9XDABCDEFGH", Today.AddYears(50)).State);

        Assert.Throws<ArgumentException>(() => LicenseIssuer.Issue(privateKey, "", ["7KQ2-M9XD-ABCD-EFGH"], Today, Today));
        Assert.Throws<ArgumentException>(() => LicenseIssuer.Issue(privateKey, "Rana", [], Today, Today));
        Assert.Throws<ArgumentException>(() => LicenseIssuer.Issue(privateKey, "Rana", ["7KQ2-M9XD-ABCD-EFGH"], Today, Today.AddDays(-1)));

        // The text pasted into licensing.json reads back as the same key.
        var config = System.Text.Json.JsonSerializer.Deserialize<string>("\"" + LicenseIssuer.PublicKeyForConfig(publicPem) + "\"")!;
        using var fromConfig = LicenseCodec.ImportPublicKey(config);
        Assert.NotNull(LicenseCodec.Verify(key, fromConfig));
    }

    [Fact]
    public void The_customer_list_keeps_history_and_shows_each_store_once()
    {
        var path = Path.Combine(Path.GetTempPath(), $"book-{Guid.NewGuid():N}", "customers.json");
        try
        {
            var book = new LicenseBook();
            book.Add(Entry("A1", "Rana", Today.AddDays(-300), Today.AddDays(5)));
            book.Add(Entry("A2", "rana", Today, Today.AddYears(1)));
            book.Add(Entry("B1", "Lara", Today.AddDays(-30), Today.AddDays(-1)));
            book.Save(path);

            var loaded = LicenseBook.Load(path);
            Assert.Equal(3, loaded.Entries.Count);
            var latest = loaded.Latest();
            Assert.Equal(["B1", "A2"], latest.Select(l => l.LicenseId));
            Assert.Equal(-1, latest[0].DaysLeft(Today));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static IssuedLicense Entry(string id, string store, DateOnly issued, DateOnly expires) => new()
    {
        LicenseId = id, Licensee = store, Machines = ["AAAA-BBBB-CCCC-DDDD"], IssuedOn = issued, ExpiresOn = expires, Key = "POS1.x.y",
    };
}
