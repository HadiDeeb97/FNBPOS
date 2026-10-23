using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClothingStore.Core.Licensing;

/// <summary>What a license grants: which store, on which PCs, until when.</summary>
public sealed record LicenseData
{
    public required string LicenseId { get; init; }
    public required string Licensee { get; init; }

    /// <summary>Machine IDs (as shown on the activation screen) of the PCs allowed to run the app.</summary>
    public required IReadOnlyList<string> Machines { get; init; }

    public required DateOnly IssuedOn { get; init; }

    /// <summary>Last day the license is valid (inclusive).</summary>
    public required DateOnly ExpiresOn { get; init; }

    public string? Notes { get; init; }

    public bool AllowsMachine(string machineId) =>
        Machines.Any(m => string.Equals(Normalize(m), Normalize(machineId), StringComparison.Ordinal));

    public static string Normalize(string machineId) =>
        new string(machineId.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}

/// <summary>
/// License keys are "POS1." + base64url(JSON) + "." + base64url(ECDSA P-256 signature). Only the vendor's private key
/// can make one; the app checks it with the public key, so editing a date or adding a PC breaks the signature.
/// </summary>
public static class LicenseCodec
{
    private const string Prefix = "POS1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Sign(LicenseData license, ECDsa privateKey)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(license, Json);
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{Prefix}.{Base64Url(payload)}.{Base64Url(signature)}";
    }

    /// <summary>The license if the key is well formed and signed by <paramref name="publicKey"/>; otherwise null.</summary>
    public static LicenseData? Verify(string? key, ECDsa publicKey)
    {
        if (!TrySplit(key, out var payload, out var signature)) return null;
        try
        {
            if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return null;
            return JsonSerializer.Deserialize<LicenseData>(payload, Json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Reads the contents without checking the signature (for the vendor tool's "inspect").</summary>
    public static LicenseData? ReadUnverified(string? key)
    {
        if (!TrySplit(key, out var payload, out _)) return null;
        try
        {
            return JsonSerializer.Deserialize<LicenseData>(payload, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ECDsa ImportPublicKey(string pem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }

    private static bool TrySplit(string? key, out byte[] payload, out byte[] signature)
    {
        payload = signature = [];
        // Keys may arrive wrapped over several lines or with spaces (copied from an email).
        var compact = new string((key ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        var parts = compact.Split('.');
        if (parts.Length != 3 || parts[0] != Prefix) return false;
        try
        {
            payload = FromBase64Url(parts[1]);
            signature = FromBase64Url(parts[2]);
            return payload.Length > 0 && signature.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

/// <summary>Turns raw machine identifiers into the short ID people read out over the phone.</summary>
public static class MachineIdentity
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I

    /// <summary>"7KQ2-M9XD-..." (4 groups of 4) from a hash of the raw identifier, so the raw value never leaves the PC.</summary>
    public static string FromRaw(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ClothingStorePOS|" + raw.Trim().ToUpperInvariant()));
        var sb = new StringBuilder();
        for (var i = 0; i < 16; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append('-');
            sb.Append(Alphabet[hash[i] % Alphabet.Length]);
        }
        return sb.ToString();
    }
}
