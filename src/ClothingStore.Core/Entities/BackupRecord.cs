namespace ClothingStore.Core.Entities;

/// <summary>One attempt to back up the database, successful or not.</summary>
public class BackupRecord : Entity
{
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public BackupKind Kind { get; set; }

    /// <summary>Path of the backup file on the SQL Server machine.</summary>
    public string? FilePath { get; set; }

    public bool Succeeded { get; set; }
    public string? Error { get; set; }

    /// <summary>Who started it; null for scheduled backups.</summary>
    public int? UserId { get; set; }
}
