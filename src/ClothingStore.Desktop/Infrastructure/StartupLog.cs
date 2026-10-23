using System.IO;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Keeps the full details of start-up failures (the message box only has room for the last line), so a problem
/// that happens on a shop PC can be looked into afterwards.
/// </summary>
public static class StartupLog
{
    private const long MaxBytes = 1024 * 1024;

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClothingStorePOS", "startup-errors.log");

    /// <summary>Appends the error with the time; returns the log's path, or null when it couldn't be written.</summary>
    public static string? Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes) File.Delete(Path);
            File.AppendAllText(Path, $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====\n{ex}\n\n");
            return Path;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
