using System.Security.Cryptography;
using ClothingStore.Core;
using ClothingStore.Core.Licensing;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

public class LicensingTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private static (ECDsa Private, ECDsa Public) Keys()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key, LicenseCodec.ImportPublicKey(key.ExportSubjectPublicKeyInfoPem()));
    }

    private static LicenseData License(DateOnly expires, params string[] machines) => new()
    {
        LicenseId = "L1", Licensee = "Boutique Rana", Machines = machines, IssuedOn = Today.AddDays(-300), ExpiresOn = expires,
    };

    [Fact]
    public void A_signed_key_is_valid_on_its_pc_until_the_last_day()
    {
        var (priv, pub) = Keys();
        var key = LicenseCodec.Sign(License(Today.AddDays(90), "AAAA-BBBB-CCCC-DDDD", "EEEE-FFFF-GGGG-HHHH"), priv);

        var status = LicenseStatus.Evaluate(key, pub, "eeee ffff gggg hhhh", Today);
        Assert.Equal((LicenseState.Valid, 90), (status.State, status.DaysLeft));
        Assert.True(status.CanRun);

        Assert.Equal(LicenseState.ExpiringSoon, LicenseStatus.Evaluate(key, pub, "AAAA-BBBB-CCCC-DDDD", Today.AddDays(60)).State); // 30 days left
        Assert.Equal(LicenseState.ExpiringSoon, LicenseStatus.Evaluate(key, pub, "AAAA-BBBB-CCCC-DDDD", Today.AddDays(90)).State); // last day
        var expired = LicenseStatus.Evaluate(key, pub, "AAAA-BBBB-CCCC-DDDD", Today.AddDays(91));
        Assert.Equal(LicenseState.Expired, expired.State);
        Assert.False(expired.CanRun);
    }

    [Fact]
    public void Another_pc_or_an_edited_key_is_refused()
    {
        var (priv, pub) = Keys();
        var key = LicenseCodec.Sign(License(Today.AddDays(90), "AAAA-BBBB-CCCC-DDDD"), priv);

        Assert.Equal(LicenseState.WrongMachine, LicenseStatus.Evaluate(key, pub, "ZZZZ-ZZZZ-ZZZZ-ZZZZ", Today).State);

        // Someone pushes the expiry date out by re-encoding the payload: the signature no longer matches.
        var parts = key.Split('.');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Pad(parts[1].Replace('-', '+').Replace('_', '/'))));
        var forged = json.Replace(Today.AddDays(90).ToString("yyyy-MM-dd"), "2099-12-31");
        var forgedKey = $"{parts[0]}.{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(forged)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.{parts[2]}";
        Assert.Equal(LicenseState.Invalid, LicenseStatus.Evaluate(forgedKey, pub, "AAAA-BBBB-CCCC-DDDD", Today).State);

        // A key made with someone else's private key.
        var (otherPriv, _) = Keys();
        Assert.Equal(LicenseState.Invalid, LicenseStatus.Evaluate(LicenseCodec.Sign(License(Today.AddDays(90), "AAAA-BBBB-CCCC-DDDD"), otherPriv), pub, "AAAA-BBBB-CCCC-DDDD", Today).State);

        Assert.Equal(LicenseState.Missing, LicenseStatus.Evaluate("  ", pub, "AAAA-BBBB-CCCC-DDDD", Today).State);
        Assert.Equal(LicenseState.Invalid, LicenseStatus.Evaluate("hello", pub, "AAAA-BBBB-CCCC-DDDD", Today).State);
        Assert.Equal(LicenseState.NotConfigured, LicenseStatus.Evaluate(key, null, "AAAA-BBBB-CCCC-DDDD", Today).State);
    }

    [Fact]
    public void Keys_survive_being_wrapped_over_lines_in_an_email()
    {
        var (priv, pub) = Keys();
        var key = LicenseCodec.Sign(License(Today.AddDays(10), "AAAA-BBBB-CCCC-DDDD"), priv);
        var wrapped = string.Join("\r\n", key.Chunk(40).Select(c => new string(c)));
        Assert.Equal(LicenseState.ExpiringSoon, LicenseStatus.Evaluate(wrapped, pub, "AAAA-BBBB-CCCC-DDDD", Today).State);
    }

    [Fact]
    public void Setting_the_clock_back_does_not_bring_the_date_back()
    {
        var local = new DateTime(2025, 1, 1);
        Assert.Equal(Today, LicenseStatus.TrustedToday(local, null, new DateTime(2026, 10, 6, 15, 0, 0), new DateTime(2026, 9, 1)));
        Assert.Equal(DateOnly.FromDateTime(local), LicenseStatus.TrustedToday(local));
    }

    [Fact]
    public void Machine_ids_are_short_stable_and_readable()
    {
        var id = MachineIdentity.FromRaw("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", id);
        Assert.Equal(id, MachineIdentity.FromRaw(" 6F9619FF-8B86-D011-B42D-00C04FC964FF "));
        Assert.NotEqual(id, MachineIdentity.FromRaw("another-pc"));
    }

    [Fact]
    public async Task The_database_supplies_its_own_clock_and_the_latest_activity()
    {
        await using var db = await TestDatabase.CreateAsync();
        var clock = new LicenseClockService(db.Factory);
        var tee = await db.CreateTeeAsync();
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Card, 22m)],
        });

        var evidence = await clock.GetEvidenceAsync();
        Assert.NotNull(evidence.ServerNow);
        Assert.Equal(DateTime.Today, evidence.LatestActivity!.Value.Date);
        Assert.Null(evidence.LastSeenRecord);

        await clock.SaveLastSeenAsync("2026-10-06|sig");
        await clock.SaveLastSeenAsync("2026-10-07|sig");
        Assert.Equal("2026-10-07|sig", (await clock.GetEvidenceAsync()).LastSeenRecord);
    }

    private static string Pad(string s) => s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');

    [Fact]
    public void The_free_trial_runs_seven_days_from_the_first_start_then_stops()
    {
        var start = Today;
        var first = LicenseStatus.Trial(start, Today);
        Assert.Equal((LicenseState.Trial, 6, true), (first.State, first.DaysLeft, first.CanRun));

        var lastDay = LicenseStatus.Trial(start, Today.AddDays(6));
        Assert.Equal((LicenseState.Trial, 0, true), (lastDay.State, lastDay.DaysLeft, lastDay.CanRun));

        var over = LicenseStatus.Trial(start, Today.AddDays(7));
        Assert.Equal((LicenseState.TrialEnded, false), (over.State, over.CanRun));
    }
}
