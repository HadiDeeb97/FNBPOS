using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClothingStore.Core.Licensing;
using ClothingStore.Core.Localization;
using ClothingStore.Data.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>Vendor details and public key, from the embedded licensing.json.</summary>
public sealed record LicensingConfig(string? PublicKey, string? VendorName, string? VendorPhone, string? VendorEmail)
{
    public string Contact => string.Join("  ·  ", new[] { VendorName, VendorPhone, VendorEmail }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Checks this PC's license at startup and sign-in. The key is stored per PC in ProgramData; the "last date seen"
/// is kept (signed) both there and in the database, so turning a PC's clock back doesn't extend a license.
/// </summary>
public sealed partial class LicenseManager : ObservableObject
{
    private static readonly string Folder = PickFolder();
    private static readonly string KeyPath = Path.Combine(Folder, "license.key");
    private static readonly string SeenPath = Path.Combine(Folder, "state.dat");
    private static readonly string TrialPath = Path.Combine(Folder, "trial.dat");

    private readonly LicenseClockService _clock;
    private readonly ECDsa? _publicKey;

    public LicenseManager(LicenseClockService clock)
    {
        _clock = clock;
        Config = LoadConfig();
        if (!string.IsNullOrWhiteSpace(Config.PublicKey))
        {
            try { _publicKey = LicenseCodec.ImportPublicKey(Config.PublicKey); }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException) { _publicKey = null; }
        }
        MachineId = MachineIdentity.FromRaw(ReadMachineGuid());
    }

    public LicensingConfig Config { get; }

    /// <summary>This PC's ID, read to the vendor to get a license.</summary>
    public string MachineId { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBanner), nameof(BannerText), nameof(IsWarning))]
    public partial LicenseStatus Status { get; private set; } = new(LicenseState.Missing);

    public DateOnly TrustedToday { get; private set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>A strip at the top of the main window: trial or expiry warning, or "licensing not configured" in developer builds.</summary>
    public bool ShowBanner => Status.State is LicenseState.ExpiringSoon or LicenseState.NotConfigured or LicenseState.Trial;
    public bool IsWarning => Status.State is LicenseState.ExpiringSoon or LicenseState.Trial;

    public string BannerText => Status.State switch
    {
        LicenseState.ExpiringSoon => Status.DaysLeft == 0
            ? Loc.T("License.ExpiresToday", Config.Contact)
            : Loc.T("License.ExpiresIn", Status.DaysLeft, Status.License!.ExpiresOn.ToString("d", CultureInfo.CurrentCulture), Config.Contact),
        LicenseState.NotConfigured => Loc.T("License.NotConfigured"),
        LicenseState.Trial => Status.DaysLeft == 0
            ? Loc.T("License.TrialLastDay", Config.Contact)
            : Loc.T("License.TrialDaysLeft", Status.DaysLeft + 1, Config.Contact),
        _ => "",
    };

    public async Task<LicenseStatus> CheckAsync(CancellationToken ct = default)
    {
        var evidence = await _clock.GetEvidenceAsync(ct);
        var seenDb = ReadSeen(evidence.LastSeenRecord);
        var seenFile = ReadSeen(TryRead(SeenPath));
        // Sales are stamped with each till's own clock: one till set a year ahead by mistake must not make the license
        // look expired everywhere, so activity far ahead of the server's clock is ignored.
        var activity = evidence.LatestActivity is { } latest && evidence.ServerNow is { } server && latest > server.AddDays(2)
            ? null
            : evidence.LatestActivity;
        TrustedToday = LicenseStatus.TrustedToday(DateTime.Now, evidence.ServerNow, activity, seenDb, seenFile);

        var status = LicenseStatus.Evaluate(TryRead(KeyPath), _publicKey, MachineId, TrustedToday);
        if (status.State != LicenseState.NotConfigured)
        {
            // Likewise the shared "last seen" date is never written beyond the server's own date (a wrong clock on this
            // PC then only affects this PC, and is undone by correcting it).
            var seen = evidence.ServerNow is { } serverNow && TrustedToday > DateOnly.FromDateTime(serverNow).AddDays(1)
                ? DateOnly.FromDateTime(serverNow).AddDays(1)
                : TrustedToday;
            // ...and never moves back, so setting the clocks back still doesn't extend the license.
            foreach (var earlier in new[] { seenDb, seenFile })
                if (earlier is { } e && DateOnly.FromDateTime(e) > seen) seen = DateOnly.FromDateTime(e);
            await RememberSeenAsync(seen, ct);
        }
        if (status.State == LicenseState.Missing) status = await TrialAsync(evidence, ct);
        Status = status;
        return status;
    }

    /// <summary>
    /// A PC with no key runs for <see cref="LicenseStatus.TrialDays"/> days from its first start. The start date is
    /// kept signed both on this PC and in the database, and the earliest copy wins, so deleting one of them (or a new
    /// database, or setting the clock back) doesn't start the trial again.
    /// </summary>
    private async Task<LicenseStatus> TrialAsync(ClockEvidence evidence, CancellationToken ct)
    {
        var fromDb = ReadSigned(evidence.TrialStartRecord, TrialKey());
        var fromFile = ReadSigned(TryRead(TrialPath), TrialKey());
        var start = new[] { fromDb, fromFile }.Where(d => d is not null).Select(d => DateOnly.FromDateTime(d!.Value))
            .DefaultIfEmpty(TrustedToday).Min();
        if (fromDb is null || fromFile is null || DateOnly.FromDateTime(fromDb.Value) != start || DateOnly.FromDateTime(fromFile.Value) != start)
        {
            var record = Sign(start, TrialKey());
            try
            {
                Directory.CreateDirectory(Folder);
                await File.WriteAllTextAsync(TrialPath, record, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            try
            {
                await _clock.SaveTrialStartAsync(record, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The file copy still counts; the database copy is written next time.
            }
        }
        return LicenseStatus.Trial(start, TrustedToday);
    }

    /// <summary>Stores a new key if it is valid for this PC; returns the resulting status either way.</summary>
    public async Task<LicenseStatus> ActivateAsync(string key, CancellationToken ct = default)
    {
        var status = LicenseStatus.Evaluate(key, _publicKey, MachineId, TrustedToday);
        if (!status.CanRun) return status;

        Directory.CreateDirectory(Folder);
        await File.WriteAllTextAsync(KeyPath, key.Trim(), ct);
        return await CheckAsync(ct);
    }

    private async Task RememberSeenAsync(DateOnly seen, CancellationToken ct)
    {
        var record = SignSeen(seen);
        try
        {
            Directory.CreateDirectory(Folder);
            await File.WriteAllTextAsync(SeenPath, record, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        try
        {
            await _clock.SaveLastSeenAsync(record, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The file copy still counts; the database copy is written next time.
        }
    }

    private string SignSeen(DateOnly date) => Sign(date, SeenKey());
    private DateTime? ReadSeen(string? record) => ReadSigned(record, SeenKey());

    // "yyyy-MM-dd|HMAC" so the date can't simply be edited back.
    private static string Sign(DateOnly date, byte[] key)
    {
        var text = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return text + "|" + Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text)));
    }

    private static DateTime? ReadSigned(string? record, byte[] key)
    {
        var parts = record?.Trim().Split('|');
        if (parts is not { Length: 2 }) return null;
        var expected = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(parts[0])));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[1]))) return null;
        return DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    // Same on every till (the database copy is shared), different for every vendor key.
    private byte[] SeenKey() => SHA256.HashData(Encoding.UTF8.GetBytes("ClothingStorePOS.seen|" + (Config.PublicKey ?? "")));

    // A separate key, so a "last seen" record (a later date) can't be passed off as the trial start.
    private byte[] TrialKey() => SHA256.HashData(Encoding.UTF8.GetBytes("ClothingStorePOS.trial|" + (Config.PublicKey ?? "")));

    private static string? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Windows' install ID (changes when Windows is reinstalled or on another PC; copying the app folder doesn't carry it).</summary>
    private static string ReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            if (key?.GetValue("MachineGuid") is string guid && guid.Length > 0) return guid;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return Environment.MachineName;
    }

    private static string PickFolder()
    {
        var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClothingStorePOS");
        try
        {
            Directory.CreateDirectory(common);
            var probe = Path.Combine(common, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return common;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClothingStorePOS");
        }
    }

    private static string? ReadResource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClothingStore.Desktop.licensing.json");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static LicensingConfig LoadConfig()
    {
        string? text = null;
        try
        {
            text = ReadResource();
            if (text is null) return new LicensingConfig(null, null, null, null);
            // Comments and trailing commas are allowed: a note left in the file must not switch licensing off.
            return JsonSerializer.Deserialize<LicensingConfig>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                       { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                   ?? new LicensingConfig(null, null, null, null);
        }
        catch (JsonException)
        {
            // Still broken: keep licensing on if the public key itself is there, rather than silently running unlicensed.
            var match = text is null ? null : System.Text.RegularExpressions.Regex.Match(text, @"-----BEGIN PUBLIC KEY-----.*?-----END PUBLIC KEY-----",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            return new LicensingConfig(match is { Success: true } ? match.Value.Replace("\\n", "\n") : null, null, null, null);
        }
    }
}
