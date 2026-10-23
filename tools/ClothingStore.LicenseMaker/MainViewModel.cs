using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using ClothingStore.Core.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ClothingStore.LicenseMaker;

public sealed record DurationOption(LicenseDuration? Duration, string Label);

/// <summary>A customer in the list: their latest license and how long it has left.</summary>
public sealed record CustomerRow(IssuedLicense License, DateOnly Today)
{
    public string Store => License.Licensee;
    public string? Phone => License.Phone;
    public int Pcs => License.Machines.Count;
    public string Expires => LicenseIssuer.IsLifetime(License.ExpiresOn) ? "Lifetime" : License.ExpiresOn.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);
    public int DaysLeft => License.DaysLeft(Today);

    public string Status => LicenseIssuer.IsLifetime(License.ExpiresOn) ? "Active"
        : DaysLeft < 0 ? $"Expired {-DaysLeft} day(s) ago"
        : DaysLeft <= LicenseStatus.WarningDays ? $"{DaysLeft} day(s) left"
        : "Active";

    /// <summary>0 = fine, 1 = renew soon, 2 = expired (for the row colour).</summary>
    public int Urgency => LicenseIssuer.IsLifetime(License.ExpiresOn) ? 0 : DaysLeft < 0 ? 2 : DaysLeft <= LicenseStatus.WarningDays ? 1 : 0;
}

/// <summary>
/// Issue and renew license keys: keeps the signing keys and a list of every customer in one folder (back it up), so
/// renewing is "pick the store, pick how long, Generate".
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly MakerSettings _settings = MakerSettings.Load();
    private ECDsa? _privateKey;
    private LicenseBook _book = new();

    public MainViewModel()
    {
        Durations =
        [
            new(LicenseDuration.OneMonth, "1 month"),
            new(LicenseDuration.ThreeMonths, "3 months"),
            new(LicenseDuration.SixMonths, "6 months"),
            new(LicenseDuration.OneYear, "1 year"),
            new(LicenseDuration.TwoYears, "2 years"),
            new(LicenseDuration.Lifetime, "Lifetime (never expires)"),
            new(null, "Until a date I choose"),
        ];
        SelectedDuration = Durations[3];
        CustomDate = DateTime.Today.AddYears(1);
        OpenFolder(_settings.KeysFolder);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    // ---- Keys folder ------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeys), nameof(NeedsKeys))]
    public partial string? KeysFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeys), nameof(NeedsKeys))]
    public partial bool KeysLoaded { get; set; }

    public bool HasKeys => KeysLoaded;
    public bool NeedsKeys => !KeysLoaded;

    private string PrivatePath => Path.Combine(KeysFolder!, "private.pem");
    private string PublicPath => Path.Combine(KeysFolder!, "public.pem");
    private string BookPath => Path.Combine(KeysFolder!, "customers.json");

    private void OpenFolder(string? folder)
    {
        _privateKey?.Dispose();
        _privateKey = null;
        KeysLoaded = false;
        KeysFolder = folder;
        Customers = [];
        if (string.IsNullOrWhiteSpace(folder) || !File.Exists(Path.Combine(folder, "private.pem"))) return;

        var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(PrivatePath));
        _privateKey = key;
        if (!File.Exists(PublicPath)) File.WriteAllText(PublicPath, key.ExportSubjectPublicKeyInfoPem());
        _book = LicenseBook.Load(BookPath);
        KeysLoaded = true;
        _settings.KeysFolder = folder;
        _settings.Save();
        RefreshCustomers();
    }

    [RelayCommand]
    private void CreateKeys()
    {
        Directory.CreateDirectory(MakerSettings.DefaultFolder);
        var dialog = new OpenFolderDialog { Title = "Choose an empty folder for your license keys", InitialDirectory = MakerSettings.DefaultFolder };
        if (dialog.ShowDialog() != true) return;

        var folder = dialog.FolderName;
        if (File.Exists(Path.Combine(folder, "private.pem")))
        {
            MessageBox.Show("This folder already has keys. Use \"Open my keys folder\" instead, or pick another folder.", "License Maker",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var (privatePem, publicPem) = LicenseIssuer.CreateKeys();
        File.WriteAllText(Path.Combine(folder, "private.pem"), privatePem);
        File.WriteAllText(Path.Combine(folder, "public.pem"), publicPem);
        OpenFolder(folder);
        MessageBox.Show(
            "Your keys are ready.\n\n1. Back up this folder (USB stick or cloud). Without private.pem you can't renew anyone's license.\n" +
            "2. Click \"Copy public key for the app\" and paste it into licensing.json (\"publicKey\") before building the app you sell.",
            "License Maker", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void OpenKeysFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder that contains private.pem" };
        if (KeysFolder is not null && Directory.Exists(KeysFolder)) dialog.InitialDirectory = KeysFolder;
        if (dialog.ShowDialog() != true) return;
        if (!File.Exists(Path.Combine(dialog.FolderName, "private.pem")))
        {
            MessageBox.Show("There is no private.pem in that folder.", "License Maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        OpenFolder(dialog.FolderName);
    }

    [RelayCommand]
    private void CopyPublicKey()
    {
        if (_privateKey is null) return;
        Clipboard.SetText(LicenseIssuer.PublicKeyForConfig(_privateKey.ExportSubjectPublicKeyInfoPem()));
        Status = "Public key copied. Paste it between the quotes of \"publicKey\" in src/ClothingStore.Desktop/licensing.json.";
    }

    // ---- Customers --------------------------------------------------------------------------

    [ObservableProperty]
    public partial List<CustomerRow> Customers { get; set; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    partial void OnSearchTextChanged(string value) => RefreshCustomers();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedKeyCommand), nameof(SaveSelectedKeyCommand))]
    public partial CustomerRow? SelectedCustomer { get; set; }

    public string Summary
    {
        get
        {
            var all = _book.Latest().Select(l => new CustomerRow(l, Today)).ToList();
            return $"{all.Count} customer(s) · {all.Count(c => c.Urgency == 1)} to renew soon · {all.Count(c => c.Urgency == 2)} expired";
        }
    }

    private void RefreshCustomers()
    {
        var text = SearchText.Trim();
        Customers = _book.Latest()
            .Where(l => text.Length == 0
                        || l.Licensee.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || (l.Phone?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                        || l.Machines.Any(m => LicenseData.Normalize(m).Contains(LicenseData.Normalize(text), StringComparison.Ordinal)))
            .Select(l => new CustomerRow(l, Today))
            .ToList();
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Fills the form from the chosen customer, ready to renew (same store and PCs).</summary>
    partial void OnSelectedCustomerChanged(CustomerRow? value)
    {
        if (value is null) return;
        var l = value.License;
        Licensee = l.Licensee;
        Phone = l.Phone;
        MachineText = string.Join(Environment.NewLine, l.Machines);
        Notes = l.Notes;
        _renewing = l;
        GeneratedKey = null;
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(PeriodHint));
    }

    private bool HasSelection() => SelectedCustomer is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopySelectedKey()
    {
        Clipboard.SetText(SelectedCustomer!.License.Key);
        Status = $"Key for {SelectedCustomer.Store} copied.";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SaveSelectedKey() => SaveKeyFile(SelectedCustomer!.License.Licensee, SelectedCustomer.License.Key);

    // ---- New / renew form -------------------------------------------------------------------

    /// <summary>The customer being renewed (null = a new customer).</summary>
    private IssuedLicense? _renewing;

    public string FormTitle => _renewing is null ? "New license" : $"Renew {_renewing.Licensee}";

    public IReadOnlyList<DurationOption> Durations { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomDate), nameof(PeriodHint))]
    public partial DurationOption SelectedDuration { get; set; }

    public bool IsCustomDate => SelectedDuration.Duration is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodHint))]
    public partial DateTime? CustomDate { get; set; }

    [ObservableProperty]
    public partial string Licensee { get; set; } = "";

    [ObservableProperty]
    public partial string? Phone { get; set; }

    [ObservableProperty]
    public partial string MachineText { get; set; } = "";

    [ObservableProperty]
    public partial string? Notes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGeneratedKey))]
    public partial string? GeneratedKey { get; set; }

    public bool HasGeneratedKey => GeneratedKey is not null;

    [ObservableProperty]
    public partial string? GeneratedSummary { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    /// <summary>The last day the key will be valid, before generating it.</summary>
    public string PeriodHint => Expiry() is { } d
        ? LicenseIssuer.IsLifetime(d) ? "Never expires." : $"Valid until {d:dddd d MMMM yyyy} (last day included)."
        : "Choose the last valid day.";

    private DateOnly? Expiry()
    {
        if (SelectedDuration.Duration is { } duration)
        {
            // A renewal continues from the current last day if it hasn't passed, so renewing early loses nothing.
            var from = _renewing is null ? Today : LicenseIssuer.RenewFrom(_renewing.ExpiresOn, Today);
            return LicenseIssuer.ExpiryFor(duration, from);
        }
        return CustomDate is { } date ? DateOnly.FromDateTime(date) : null;
    }

    [RelayCommand]
    private void New()
    {
        SelectedCustomer = null;
        _renewing = null;
        Licensee = "";
        Phone = null;
        MachineText = "";
        Notes = null;
        GeneratedKey = null;
        GeneratedSummary = null;
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(PeriodHint));
    }

    [RelayCommand]
    private void PasteMachineId()
    {
        if (!Clipboard.ContainsText()) return;
        var ids = LicenseIssuer.ParseMachineIds(Clipboard.GetText(), out _);
        if (ids.Count == 0)
        {
            Status = "The clipboard doesn't contain a PC ID (like 7KQ2-M9XD-ABCD-EFGH).";
            return;
        }
        var existing = LicenseIssuer.ParseMachineIds(MachineText, out _);
        MachineText = string.Join(Environment.NewLine, existing.Concat(ids).Distinct());
    }

    [RelayCommand]
    private void Generate()
    {
        if (_privateKey is null) return;
        var machines = LicenseIssuer.ParseMachineIds(MachineText, out var invalid);
        if (invalid.Count > 0)
        {
            MessageBox.Show($"These don't look like PC IDs (16 letters/digits, like 7KQ2-M9XD-ABCD-EFGH):\n\n{string.Join("\n", invalid)}",
                "License Maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (Expiry() is not { } expires)
        {
            MessageBox.Show("Choose the last valid day.", "License Maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        (LicenseData license, string key) issued;
        try
        {
            issued = LicenseIssuer.Issue(_privateKey, Licensee, machines, Today, expires, Notes);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "License Maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (license, key) = issued;
        var entry = new IssuedLicense
        {
            LicenseId = license.LicenseId, Licensee = license.Licensee, Machines = license.Machines, IssuedOn = license.IssuedOn,
            ExpiresOn = license.ExpiresOn, Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(), Notes = license.Notes, Key = key,
        };
        _book.Add(entry);
        _book.Save(BookPath);
        _renewing = entry;
        RefreshCustomers();

        GeneratedKey = key;
        GeneratedSummary = $"{license.Licensee} · {license.Machines.Count} PC(s) · " +
                           (LicenseIssuer.IsLifetime(license.ExpiresOn) ? "never expires" : $"valid until {license.ExpiresOn:d MMM yyyy}");
        Clipboard.SetText(key);
        Status = "Key generated and copied. Send it to the customer (WhatsApp, email or the .lic file).";
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(PeriodHint));
    }

    [RelayCommand]
    private void CopyKey()
    {
        if (GeneratedKey is null) return;
        Clipboard.SetText(GeneratedKey);
        Status = "Key copied.";
    }

    /// <summary>A ready-to-send message with the key and what to do with it.</summary>
    [RelayCommand]
    private void CopyMessage()
    {
        if (GeneratedKey is null || _renewing is null) return;
        var until = LicenseIssuer.IsLifetime(_renewing.ExpiresOn) ? "with no expiry date" : $"until {_renewing.ExpiresOn:d MMMM yyyy}";
        Clipboard.SetText(
            $"Hello {_renewing.Licensee}, here is your Clothing Store POS license, valid {until}.\n\n" +
            "Open the app, copy everything below, and paste it on the activation screen (or user menu → License…):\n\n" + GeneratedKey);
        Status = "Message copied. Paste it into WhatsApp or an email.";
    }

    [RelayCommand]
    private void SaveKey()
    {
        if (GeneratedKey is not null) SaveKeyFile(Licensee, GeneratedKey);
    }

    private void SaveKeyFile(string licensee, string key)
    {
        var safe = string.Concat(licensee.Split(Path.GetInvalidFileNameChars())).Trim();
        var dialog = new SaveFileDialog
        {
            FileName = (safe.Length == 0 ? "license" : safe) + ".lic",
            Filter = "License key (*.lic)|*.lic",
            InitialDirectory = KeysFolder,
        };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, key);
        Status = $"Saved {dialog.FileName}.";
    }

    /// <summary>Adds a key made earlier (e.g. with the command-line tool) to the customer list.</summary>
    [RelayCommand]
    private void ImportKey()
    {
        var dialog = new OpenFileDialog { Filter = "License key (*.lic;*.txt)|*.lic;*.txt|All files|*.*", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        var added = 0;
        foreach (var file in dialog.FileNames)
        {
            var key = File.ReadAllText(file).Trim();
            if (_privateKey is null || LicenseCodec.ReadUnverified(key) is not { } l) continue;
            using var publicKey = LicenseCodec.ImportPublicKey(_privateKey.ExportSubjectPublicKeyInfoPem());
            if (LicenseCodec.Verify(key, publicKey) is null) continue; // signed with other keys
            if (_book.Entries.Any(e => e.LicenseId == l.LicenseId)) continue;
            _book.Add(new IssuedLicense
            {
                LicenseId = l.LicenseId, Licensee = l.Licensee, Machines = l.Machines, IssuedOn = l.IssuedOn, ExpiresOn = l.ExpiresOn,
                Notes = l.Notes, Key = key,
            });
            added++;
        }
        _book.Save(BookPath);
        RefreshCustomers();
        Status = added == 0 ? "No new keys made with these keys were found." : $"{added} license(s) added to the list.";
    }
}
