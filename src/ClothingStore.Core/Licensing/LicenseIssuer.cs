using System.Security.Cryptography;
using System.Text.Json;

namespace ClothingStore.Core.Licensing;

/// <summary>How long a license lasts; <see cref="Lifetime"/> never expires in practice.</summary>
public enum LicenseDuration
{
    OneMonth,
    ThreeMonths,
    SixMonths,
    OneYear,
    TwoYears,
    Lifetime,
}

/// <summary>Vendor side: checks the inputs and signs license keys (used by the License Maker app and pos-license).</summary>
public static class LicenseIssuer
{
    /// <summary>"Lifetime" licenses run to this day.</summary>
    public static readonly DateOnly LifetimeEnd = new(2099, 12, 31);

    public static bool IsLifetime(DateOnly expiresOn) => expiresOn >= LifetimeEnd;

    /// <summary>
    /// Last valid day for a license of <paramref name="duration"/> starting on <paramref name="from"/>. A renewal starts
    /// from the old expiry when that is still ahead, so renewing early doesn't lose days.
    /// </summary>
    public static DateOnly ExpiryFor(LicenseDuration duration, DateOnly from) => duration switch
    {
        LicenseDuration.OneMonth => from.AddMonths(1),
        LicenseDuration.ThreeMonths => from.AddMonths(3),
        LicenseDuration.SixMonths => from.AddMonths(6),
        LicenseDuration.OneYear => from.AddYears(1),
        LicenseDuration.TwoYears => from.AddYears(2),
        _ => LifetimeEnd,
    };

    /// <summary>Start of a renewal: the current last day if still ahead (the new period follows on), otherwise today.</summary>
    public static DateOnly RenewFrom(DateOnly currentExpiry, DateOnly today) => currentExpiry >= today ? currentExpiry : today;

    /// <summary>
    /// PC IDs from pasted text (one per line, or separated by commas/spaces between IDs), formatted "XXXX-XXXX-XXXX-XXXX".
    /// Anything that isn't a 16-character ID is returned in <paramref name="invalid"/>.
    /// </summary>
    public static List<string> ParseMachineIds(string? text, out List<string> invalid)
    {
        invalid = [];
        var ids = new List<string>();
        foreach (var part in (text ?? "").Split(['\n', '\r', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = LicenseData.Normalize(part);
            if (normalized.Length == 0) continue;
            if (normalized.Length != 16)
            {
                invalid.Add(part);
                continue;
            }
            var formatted = string.Join('-', Enumerable.Range(0, 4).Select(i => normalized.Substring(i * 4, 4)));
            if (!ids.Contains(formatted)) ids.Add(formatted);
        }
        return ids;
    }

    /// <summary>Signs a new license and checks the result with the matching public key before handing it out.</summary>
    public static (LicenseData License, string Key) Issue(
        ECDsa privateKey, string licensee, IReadOnlyList<string> machines, DateOnly issuedOn, DateOnly expiresOn, string? notes = null)
    {
        licensee = licensee.Trim();
        if (licensee.Length == 0) throw new ArgumentException("Enter the store name.");
        if (machines.Count == 0) throw new ArgumentException("Enter at least one PC ID.");
        if (expiresOn < issuedOn) throw new ArgumentException("The last day is before today.");

        var license = new LicenseData
        {
            LicenseId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Licensee = licensee,
            Machines = machines,
            IssuedOn = issuedOn,
            ExpiresOn = expiresOn,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        var key = LicenseCodec.Sign(license, privateKey);
        using var publicKey = LicenseCodec.ImportPublicKey(privateKey.ExportSubjectPublicKeyInfoPem());
        if (LicenseCodec.Verify(key, publicKey) is null) throw new CryptographicException("The new key failed its own check.");
        return (license, key);
    }

    /// <summary>Creates a new signing key pair as (private PEM, public PEM).</summary>
    public static (string PrivatePem, string PublicPem) CreateKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>The public key text as it goes into licensing.json ("publicKey"), with \n escapes.</summary>
    public static string PublicKeyForConfig(string publicPem) =>
        JsonSerializer.Serialize(publicPem.Replace("\r", "").Trim() + "\n").Trim('"');
}

/// <summary>One license issued to a customer, as kept in the License Maker's customer list.</summary>
public sealed record IssuedLicense
{
    public required string LicenseId { get; init; }
    public required string Licensee { get; init; }
    public required IReadOnlyList<string> Machines { get; init; }
    public required DateOnly IssuedOn { get; init; }
    public required DateOnly ExpiresOn { get; init; }
    public string? Phone { get; init; }
    public string? Notes { get; init; }
    public required string Key { get; init; }

    public int DaysLeft(DateOnly today) => ExpiresOn.DayNumber - today.DayNumber;
}

/// <summary>
/// The vendor's record of every license issued (a JSON file next to the keys), newest first. Re-issuing for the same
/// store keeps the old entries as history; <see cref="Latest"/> gives one row per store.
/// </summary>
public sealed class LicenseBook
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public List<IssuedLicense> Entries { get; init; } = [];

    /// <summary>The most recent license of each store.</summary>
    public List<IssuedLicense> Latest() =>
        Entries.GroupBy(e => e.Licensee.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.IssuedOn).ThenByDescending(e => e.ExpiresOn).First())
            .OrderBy(e => e.ExpiresOn)
            .ToList();

    public void Add(IssuedLicense entry) => Entries.Insert(0, entry);

    public static LicenseBook Load(string path)
    {
        if (!File.Exists(path)) return new LicenseBook();
        return JsonSerializer.Deserialize<LicenseBook>(File.ReadAllText(path), Json) ?? new LicenseBook();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Write then swap, so a crash mid-save can't leave a half-written customer list.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}
