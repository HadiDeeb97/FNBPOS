namespace ClothingStore.Core;

/// <summary>
/// The program's own name on a PC: its data folders, its single-copy lock and the name on its backups. It differs from
/// the Clothing Store POS it grew out of, so both can run on one PC without touching each other's files or database.
/// </summary>
public static class AppIdentity
{
    public const string Name = "UniPOS";

    /// <summary>%LocalAppData%\UniPOS: preferences, logs and tokens of this PC.</summary>
    public static string LocalFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);

    /// <summary>%ProgramData%\UniPOS: the license, shared by every Windows user of the PC.</summary>
    public static string CommonFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Name);
}
