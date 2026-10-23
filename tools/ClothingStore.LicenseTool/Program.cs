using System.Globalization;
using System.Security.Cryptography;
using ClothingStore.Core.Licensing;

// Vendor tool: makes your signing keys and issues license keys for customers' PCs.
// Keep private.pem to yourself (never in the repository, never on a customer's PC).

return args.FirstOrDefault()?.ToLowerInvariant() switch
{
    "keygen" => KeyGen(Option("--out") ?? "license-keys"),
    "issue" => Issue(),
    "inspect" => Inspect(args.ElementAtOrDefault(1)),
    _ => Usage(),
};

int Usage()
{
    Console.WriteLine("""
        pos-license keygen  [--out <folder>]
            Creates private.pem (KEEP SECRET) and public.pem. Paste public.pem into
            src/ClothingStore.Desktop/licensing.json ("publicKey") before building a release.

        pos-license issue --key <private.pem> --licensee "<store name>" --machine <ID> [--machine <ID> ...]
                          (--days <n> | --expires <yyyy-MM-dd>) [--notes "<text>"] [--out <file.lic>]
            Prints a license key for the given machine ID(s) (shown on the app's activation screen).

        pos-license inspect <key or file>
            Shows what a license key contains.
        """);
    return 1;
}

int KeyGen(string folder)
{
    Directory.CreateDirectory(folder);
    var privatePath = Path.Combine(folder, "private.pem");
    if (File.Exists(privatePath))
    {
        Console.Error.WriteLine($"{privatePath} already exists. Delete it first if you really want new keys (old licenses would stop working).");
        return 2;
    }
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(Path.Combine(folder, "public.pem"), key.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine($"Keys written to {Path.GetFullPath(folder)}");
    Console.WriteLine("Back up private.pem somewhere safe: without it you can't issue or renew licenses.");
    Console.WriteLine();
    Console.WriteLine("Public key for licensing.json (\"publicKey\"):");
    Console.WriteLine(key.ExportSubjectPublicKeyInfoPem().Replace("\r", "").Replace("\n", "\\n"));
    return 0;
}

int Issue()
{
    var keyPath = Option("--key");
    var licensee = Option("--licensee");
    var machines = Options("--machine");
    if (keyPath is null || licensee is null || machines.Count == 0) return Usage();

    var today = DateOnly.FromDateTime(DateTime.Today);
    DateOnly expires;
    if (Option("--expires") is { } exp)
        expires = DateOnly.ParseExact(exp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    else if (Option("--days") is { } days)
        expires = today.AddDays(int.Parse(days, CultureInfo.InvariantCulture));
    else
        return Usage();

    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(keyPath));
    var machineIds = LicenseIssuer.ParseMachineIds(string.Join('\n', machines), out var invalid);
    if (invalid.Count > 0)
    {
        Console.Error.WriteLine($"Not a PC ID (16 letters/digits): {string.Join(", ", invalid)}");
        return 2;
    }
    var (license, text) = LicenseIssuer.Issue(key, licensee, machineIds, today, expires, Option("--notes"));

    // Same customer list as the License Maker app, so both tools show every license issued.
    var bookPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(keyPath))!, "customers.json");
    var book = LicenseBook.Load(bookPath);
    book.Add(new IssuedLicense
    {
        LicenseId = license.LicenseId, Licensee = license.Licensee, Machines = license.Machines, IssuedOn = license.IssuedOn,
        ExpiresOn = license.ExpiresOn, Notes = license.Notes, Key = text,
    });
    book.Save(bookPath);

    if (Option("--out") is { } outPath)
    {
        File.WriteAllText(outPath, text);
        Console.WriteLine($"License written to {Path.GetFullPath(outPath)}");
    }
    Console.WriteLine($"License {license.LicenseId} for {license.Licensee}, {license.Machines.Count} PC(s), valid until {license.ExpiresOn:yyyy-MM-dd}:");
    Console.WriteLine();
    Console.WriteLine(text);
    return 0;
}

int Inspect(string? input)
{
    if (input is null) return Usage();
    var text = File.Exists(input) ? File.ReadAllText(input) : input;
    var license = LicenseCodec.ReadUnverified(text);
    if (license is null)
    {
        Console.Error.WriteLine("Not a license key.");
        return 2;
    }
    Console.WriteLine($"License:   {license.LicenseId}");
    Console.WriteLine($"Licensee:  {license.Licensee}");
    Console.WriteLine($"Issued:    {license.IssuedOn:yyyy-MM-dd}");
    Console.WriteLine($"Expires:   {license.ExpiresOn:yyyy-MM-dd}");
    Console.WriteLine($"Machines:  {string.Join(", ", license.Machines)}");
    if (license.Notes is not null) Console.WriteLine($"Notes:     {license.Notes}");
    Console.WriteLine("(Signature not checked here; the app checks it.)");
    return 0;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

List<string> Options(string name) =>
    args.Select((a, i) => (a, i)).Where(x => x.a == name && x.i + 1 < args.Length).Select(x => args[x.i + 1]).ToList();
