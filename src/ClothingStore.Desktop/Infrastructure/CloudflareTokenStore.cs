using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// The Cloudflare API token of online reports, kept on the PC that set the website up so it can update the website when
/// a new POS version changes it. Encrypted for this Windows user (DPAPI); never in the shared database or its backups.
/// </summary>
public static class CloudflareTokenStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClothingStorePOS", "cloudflare.token");

    private static readonly byte[] Entropy = "ClothingStorePOS.OnlineReports"u8.ToArray();

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null; // another Windows user's, or damaged: asked for again when needed
        }
    }

    public static void Save(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()), Entropy, DataProtectionScope.CurrentUser));
    }

    public static void Forget()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
