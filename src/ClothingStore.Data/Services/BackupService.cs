using System.Data;
using System.Globalization;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

/// <summary>
/// Full copy-only backups taken by SQL Server, checksummed and verified, and logged in <see cref="BackupRecord"/>.
/// Backups are safe while tills are selling.
/// <para>
/// Every backup gets its own timestamped file, so none replaces another. Automatic ones (at start-up, scheduled and
/// after a shift closes) older than <see cref="KeepAutomaticDays"/> days are deleted, always keeping the newest
/// <see cref="KeepAutomaticAtLeast"/>; manual backups are never deleted.
/// </para>
/// </summary>
public class BackupService(IDbContextFactory<PosDbContext> factory)
{
    private static readonly TimeSpan BackupTimeout = TimeSpan.FromMinutes(30);

    /// <summary>After a failed automatic backup, wait this long before trying again.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    /// <summary>Automatic backups older than this many days are removed.</summary>
    public const int KeepAutomaticDays = 30;

    /// <summary>...but at least this many of the newest automatic backups always stay.</summary>
    public const int KeepAutomaticAtLeast = 10;

    /// <summary>Held while a backup runs so two tills never back up at the same time.</summary>
    private const string LockResource = "ClothingStorePOS.Backup";

    /// <summary>
    /// Backs up now to the folder set in Settings. Throws <see cref="BusinessRuleException"/> if the backup fails
    /// (the failure is still logged) or another till is backing up.
    /// </summary>
    public async Task<BackupRecord> BackupAsync(BackupKind kind, int? userId, CancellationToken ct = default) =>
        await TryBackupAsync(kind, userId, ct)
        ?? throw new BusinessRuleException(Loc.T("Err.BackupBusy"));

    /// <summary>
    /// Runs the scheduled backup if automatic backups are on and the last successful backup is older than the
    /// configured interval. Returns null when nothing was due.
    /// </summary>
    public async Task<BackupRecord?> RunIfDueAsync(CancellationToken ct = default)
    {
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var settings = await GetSettingsAsync(db, ct);
            if (!settings.AutoBackupEnabled) return null;

            var now = DateTime.Now;
            var lastSuccess = await db.BackupRecords.Where(b => b.Succeeded).MaxAsync(b => (DateTime?)b.StartedAt, ct);
            if (lastSuccess is not null && now - lastSuccess < TimeSpan.FromHours(settings.AutoBackupIntervalHours)) return null;

            var lastAttempt = await db.BackupRecords.AsNoTracking()
                .Where(b => b.Kind != BackupKind.Manual)
                .OrderByDescending(b => b.StartedAt)
                .FirstOrDefaultAsync(ct);
            if (lastAttempt is { Succeeded: false } && now - lastAttempt.StartedAt < RetryAfterFailure) return null;
        }

        return await TryBackupAsync(BackupKind.Scheduled, null, ct);
    }

    /// <summary>End-of-day backup after a shift is closed, if automatic backups are on. Returns null when skipped.</summary>
    public async Task<BackupRecord?> BackupAfterShiftCloseAsync(int userId, CancellationToken ct = default)
    {
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            if (!(await GetSettingsAsync(db, ct)).AutoBackupEnabled) return null;
        }
        return await TryBackupAsync(BackupKind.ShiftClose, userId, ct);
    }

    /// <summary>
    /// Backs up an existing database when the app starts, before it is upgraded to the new version.
    /// <para>
    /// Runs before migrations, so it uses plain SQL against tables that exist in every version (the entity model may
    /// already have columns the old database lacks). Returns null when there is no database yet (first run), when
    /// another till is backing up right now, or when a backup finished less than <see cref="StartupSkipWindow"/> ago
    /// (several tills opening together). A failure is logged and returned, never thrown: the shop must still open.
    /// </para>
    /// </summary>
    public async Task<BackupRecord?> BackupOnStartupAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Database.CanConnectAsync(ct)) return null; // no database yet: nothing to protect

        db.Database.SetCommandTimeout(BackupTimeout);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var hasLog = await ScalarAsync<int>(db, "SELECT CASE WHEN OBJECT_ID(N'dbo.BackupRecords') IS NULL THEN 0 ELSE 1 END AS [Value]", ct) == 1;
            if (hasLog)
            {
                var recent = await ScalarAsync<int>(db,
                    $"SELECT COUNT(*) AS [Value] FROM BackupRecords WHERE Succeeded = 1 AND CompletedAt > '{DateTime.Now - StartupSkipWindow:yyyy-MM-ddTHH:mm:ss}'", ct);
                if (recent > 0) return null;
            }

            if (!await TryLockAsync(db, ct)) return null;
            var record = new BackupRecord { Kind = BackupKind.Startup, StartedAt = DateTime.Now };
            try
            {
                string? configured = null;
                try
                {
                    configured = (await db.Database.SqlQueryRaw<string?>("SELECT TOP 1 BackupFolder AS [Value] FROM Settings ORDER BY Id").ToListAsync(ct))
                        .FirstOrDefault();
                }
                catch (SqlException)
                {
                    // No settings table yet: use the server's default folder.
                }

                var folder = await ResolveFolderAsync(db, configured, ct);
                record.FilePath = CombineServerPath(folder, FileName(db.Database.GetDbConnection().Database, BackupKind.Startup, record.StartedAt));
                await RunBackupAsync(db, record.FilePath, ct);
                record.Succeeded = true;
            }
            catch (Exception ex) when (ex is SqlException or BusinessRuleException)
            {
                record.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            }
            finally
            {
                record.CompletedAt = DateTime.Now;
                await ReleaseLockAsync(db);
            }

            if (hasLog)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT INTO BackupRecords (StartedAt, CompletedAt, Kind, FilePath, Succeeded, Error, UserId) VALUES (@s, @c, @k, @f, @ok, @e, NULL)",
                        [
                            new SqlParameter("@s", record.StartedAt), new SqlParameter("@c", record.CompletedAt),
                            new SqlParameter("@k", (int)record.Kind), new SqlParameter("@f", (object?)record.FilePath ?? DBNull.Value),
                            new SqlParameter("@ok", record.Succeeded), new SqlParameter("@e", (object?)record.Error ?? DBNull.Value),
                        ],
                        CancellationToken.None);
                }
                catch (SqlException)
                {
                    // The backup itself is what matters.
                }
            }
            return record;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Several tills opening within this window make one startup backup between them.</summary>
    public static readonly TimeSpan StartupSkipWindow = TimeSpan.FromMinutes(10);

    /// <summary>One value from a query that names its column [Value].</summary>
    private static async Task<T> ScalarAsync<T>(PosDbContext db, string sql, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<T>(sql).ToListAsync(ct)).Single();

    public async Task<List<BackupRecord>> GetRecentAsync(int count = 10, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.BackupRecords.AsNoTracking().OrderByDescending(b => b.StartedAt).Take(count).ToListAsync(ct);
    }

    /// <summary>Returns null if another till holds the backup lock.</summary>
    private async Task<BackupRecord?> TryBackupAsync(BackupKind kind, int? userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(BackupTimeout);
        var settings = await GetSettingsAsync(db, ct);

        // The lock is owned by the session, so everything below must run on this one connection.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            if (!await TryLockAsync(db, ct)) return null;

            var record = new BackupRecord { Kind = kind, UserId = userId, StartedAt = DateTime.Now };
            try
            {
                try
                {
                    var folder = await ResolveFolderAsync(db, settings.BackupFolder, ct);
                    record.FilePath = CombineServerPath(folder, FileName(db.Database.GetDbConnection().Database, kind, record.StartedAt));
                    await RunBackupAsync(db, record.FilePath, ct);
                    record.Succeeded = true;
                }
                catch (Exception ex) when (ex is SqlException or BusinessRuleException)
                {
                    record.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                }
                finally
                {
                    record.CompletedAt = DateTime.Now;
                    db.BackupRecords.Add(record);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
            }
            finally
            {
                await ReleaseLockAsync(db);
            }

            if (record.Succeeded && kind != BackupKind.Manual)
            {
                // Old automatic backups (including start-up ones) go once a new one has safely been written.
                try { await PruneAsync(ct: CancellationToken.None); }
                catch (SqlException) { /* cleaning up is optional */ }
            }

            return record.Succeeded
                ? record
                : throw new BusinessRuleException(record.FilePath is null
                    ? record.Error!
                    : Loc.T("Err.BackupFailed", record.FilePath, record.Error));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>A new file for every backup (date and time to the millisecond), so one never overwrites another.</summary>
    private static string FileName(string databaseName, BackupKind kind, DateTime at)
    {
        var stamp = at.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        return kind switch
        {
            BackupKind.Manual => $"{databaseName}_{stamp}.bak",
            BackupKind.Startup => $"{databaseName}_startup_{stamp}.bak",
            BackupKind.ShiftClose => $"{databaseName}_shift_{stamp}.bak",
            _ => $"{databaseName}_auto_{stamp}.bak",
        };
    }

    /// <summary>
    /// Deletes automatic backups older than <paramref name="keepDays"/> days (keeping the newest
    /// <paramref name="keepAtLeast"/> whatever their age) and their log rows. Best effort: if SQL Server may not delete
    /// files (not an administrator), the files and rows simply stay. Returns how many were removed.
    /// </summary>
    public async Task<int> PruneAsync(int keepDays = KeepAutomaticDays, int keepAtLeast = KeepAutomaticAtLeast, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var cutoff = DateTime.Now.AddDays(-keepDays);
        var automatic = await db.BackupRecords
            .Where(b => b.Kind != BackupKind.Manual && b.Succeeded && b.FilePath != null)
            .OrderByDescending(b => b.StartedAt)
            .ToListAsync(ct);
        var old = automatic.Skip(keepAtLeast).Where(b => b.StartedAt < cutoff).ToList();
        // Older versions reused one file per weekday: never delete a file a backup we keep still points to.
        var stillUsed = automatic.Except(old).Select(b => b.FilePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = 0;
        foreach (var record in old)
        {
            if (!stillUsed.Contains(record.FilePath!))
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync("EXEC master.dbo.xp_delete_file 0, @path;", [new SqlParameter("@path", record.FilePath)], ct);
                }
                catch (SqlException)
                {
                    break; // no permission to delete files: leave the rest alone
                }
            }
            db.BackupRecords.Remove(record);
            removed++;
        }
        if (removed > 0) await db.SaveChangesAsync(ct);
        return removed;
    }

    private static async Task<StoreSettings> GetSettingsAsync(PosDbContext db, CancellationToken ct) =>
        await db.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? new StoreSettings();

    private static async Task<bool> TryLockAsync(PosDbContext db, CancellationToken ct)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            """
            DECLARE @r int;
            EXEC @r = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
            SET @result = @r;
            """,
            [new SqlParameter("@resource", LockResource), result],
            ct);
        return (int)result.Value >= 0;
    }

    private static async Task ReleaseLockAsync(PosDbContext db) =>
        await db.Database.ExecuteSqlRawAsync(
            "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';",
            [new SqlParameter("@resource", LockResource)]);

    private static async Task<string> ResolveFolderAsync(PosDbContext db, string? folder, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(folder)) return folder.Trim();

        var serverDefault = (await db.Database
            .SqlQueryRaw<string?>("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS [Value]")
            .ToListAsync(ct)).SingleOrDefault();
        return string.IsNullOrWhiteSpace(serverDefault)
            ? throw new BusinessRuleException(Loc.T("Err.NoDefaultBackupFolder"))
            : serverDefault;
    }

    private static Task RunBackupAsync(PosDbContext db, string path, CancellationToken ct) =>
        // COPY_ONLY leaves any backup schedule the server's administrator runs undisturbed.
        db.Database.ExecuteSqlRawAsync(
            """
            DECLARE @db sysname = DB_NAME();
            BACKUP DATABASE @db TO DISK = @path WITH COPY_ONLY, INIT, FORMAT, CHECKSUM, NAME = @name;
            RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;
            """,
            [new SqlParameter("@path", path), new SqlParameter("@name", "Clothing Store POS backup")],
            ct);

    /// <summary>Joins a folder and file name using the server's path style (Windows or Linux).</summary>
    internal static string CombineServerPath(string folder, string fileName)
    {
        var separator = folder.Contains('/') && !folder.Contains('\\') ? '/' : '\\';
        return folder.TrimEnd('/', '\\') + separator + fileName;
    }
}
