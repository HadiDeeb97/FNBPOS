using System.IO;
using System.Text.Json;
using UniPos.Core;

namespace UniPos.LicenseMaker;

/// <summary>Which folder holds the signing keys and customer list (remembered on this computer only).</summary>
public sealed class MakerSettings
{
    private static readonly string FilePath = Path.Combine(AppIdentity.LocalFolder, "license-maker.json");

    public string? KeysFolder { get; set; }

    /// <summary>Not the Clothing Store POS's "POS Licenses": this program has its own keys and customer list.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppIdentity.Name + " Licenses");

    public static MakerSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<MakerSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new MakerSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
