using System.Globalization;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Tests;

public class BackupServiceTests
{
    private static async Task ConfigureAsync(TestDatabase db, Action<StoreSettings> configure)
    {
        var settings = await db.Settings.GetAsync();
        configure(settings);
        await db.Settings.SaveAsync(settings);
    }

    private static async Task AgeBackupsAsync(TestDatabase db, TimeSpan by)
    {
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var minutes = -by.TotalMinutes;
        await ctx.BackupRecords.ExecuteUpdateAsync(b => b.SetProperty(x => x.StartedAt, x => x.StartedAt.AddMinutes(minutes)));
    }

    [Fact]
    public async Task Manual_backup_is_verified_and_logged()
    {
        await using var db = await TestDatabase.CreateAsync();
        var backups = new BackupService(db.Factory);

        var record = await backups.BackupAsync(BackupKind.Manual, db.Admin.Id);

        Assert.True(record.Succeeded);
        Assert.Matches(@"ClothingStorePOS_Test_\w+_\d{8}-\d{6}-\d{3}\.bak$", record.FilePath);
        var logged = Assert.Single(await backups.GetRecentAsync());
        Assert.Equal((record.FilePath, BackupKind.Manual, db.Admin.Id), (logged.FilePath, logged.Kind, logged.UserId));
    }

    [Fact]
    public async Task Failed_backup_is_logged_and_reported()
    {
        await using var db = await TestDatabase.CreateAsync();
        await ConfigureAsync(db, s => s.BackupFolder = "/no/such/folder");
        var backups = new BackupService(db.Factory);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => backups.BackupAsync(BackupKind.Manual, db.Admin.Id));

        Assert.Contains("/no/such/folder", ex.Message);
        var logged = Assert.Single(await backups.GetRecentAsync());
        Assert.False(logged.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(logged.Error));
    }

    [Fact]
    public async Task Scheduled_backup_runs_when_due_into_its_own_file()
    {
        await using var db = await TestDatabase.CreateAsync();
        var backups = new BackupService(db.Factory);

        var first = await backups.RunIfDueAsync();
        Assert.NotNull(first);
        Assert.Equal(BackupKind.Scheduled, first.Kind);
        Assert.Null(first.UserId);
        Assert.EndsWith($"_auto_{first.StartedAt.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.bak", first.FilePath);

        Assert.Null(await backups.RunIfDueAsync()); // last backup is recent

        await AgeBackupsAsync(db, TimeSpan.FromHours(25));
        var second = await backups.RunIfDueAsync();
        Assert.NotNull(second);
        Assert.NotEqual(first.FilePath, second.FilePath); // the earlier backup is kept, not overwritten
    }

    [Fact]
    public async Task Old_automatic_backups_are_removed_but_manual_ones_and_the_newest_stay()
    {
        await using var db = await TestDatabase.CreateAsync();
        var backups = new BackupService(db.Factory);
        var manual = await backups.BackupAsync(BackupKind.Manual, db.Admin.Id);
        await backups.BackupAsync(BackupKind.ShiftClose, db.Admin.Id);
        await backups.BackupAsync(BackupKind.ShiftClose, db.Admin.Id);
        await AgeBackupsAsync(db, TimeSpan.FromDays(40));

        Assert.Equal(1, await backups.PruneAsync(keepDays: 30, keepAtLeast: 1));
        var left = await backups.GetRecentAsync();
        Assert.Equal(2, left.Count);
        Assert.Contains(left, b => b.FilePath == manual.FilePath);
        Assert.Contains(left, b => b.Kind == BackupKind.ShiftClose);
    }

    [Fact]
    public async Task Any_recent_backup_postpones_the_scheduled_one_and_the_interval_is_configurable()
    {
        await using var db = await TestDatabase.CreateAsync();
        await ConfigureAsync(db, s => s.AutoBackupIntervalHours = 2);
        var backups = new BackupService(db.Factory);

        await backups.BackupAsync(BackupKind.Manual, db.Admin.Id);
        Assert.Null(await backups.RunIfDueAsync());

        await AgeBackupsAsync(db, TimeSpan.FromHours(3));
        Assert.NotNull(await backups.RunIfDueAsync());
    }

    [Fact]
    public async Task Turning_automatic_backups_off_skips_scheduled_and_shift_close_backups()
    {
        await using var db = await TestDatabase.CreateAsync();
        await ConfigureAsync(db, s => s.AutoBackupEnabled = false);
        var backups = new BackupService(db.Factory);

        Assert.Null(await backups.RunIfDueAsync());
        Assert.Null(await backups.BackupAfterShiftCloseAsync(db.Cashier.Id));
        Assert.Empty(await backups.GetRecentAsync());
    }

    [Fact]
    public async Task Shift_close_backup_is_logged_with_the_cashier()
    {
        await using var db = await TestDatabase.CreateAsync();

        var record = await new BackupService(db.Factory).BackupAfterShiftCloseAsync(db.Cashier.Id);

        Assert.NotNull(record);
        Assert.Equal((BackupKind.ShiftClose, db.Cashier.Id, true), (record.Kind, record.UserId, record.Succeeded));
    }

    [Fact]
    public async Task Failed_scheduled_backup_waits_before_retrying()
    {
        await using var db = await TestDatabase.CreateAsync();
        await ConfigureAsync(db, s => s.BackupFolder = "/no/such/folder");
        var backups = new BackupService(db.Factory);

        await Assert.ThrowsAsync<BusinessRuleException>(() => backups.RunIfDueAsync());
        Assert.Null(await backups.RunIfDueAsync()); // no retry straight away

        await AgeBackupsAsync(db, BackupService.RetryAfterFailure + TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<BusinessRuleException>(() => backups.RunIfDueAsync());
        Assert.Equal(2, (await backups.GetRecentAsync()).Count);
    }

    [Fact]
    public async Task Only_one_till_backs_up_at_a_time()
    {
        await using var db = await TestDatabase.CreateAsync();
        var backups = new BackupService(db.Factory);
        string connectionString;
        await using (var ctx = await db.Factory.CreateDbContextAsync()) connectionString = ctx.Database.GetConnectionString()!;

        await using (var otherTill = new SqlConnection(connectionString))
        {
            await otherTill.OpenAsync();
            await using var cmd = new SqlCommand(
                "EXEC sp_getapplock @Resource = 'ClothingStorePOS.Backup', @LockMode = 'Exclusive', @LockOwner = 'Session';", otherTill);
            await cmd.ExecuteNonQueryAsync();

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => backups.BackupAsync(BackupKind.Manual, db.Admin.Id));
            Assert.Contains("Another till", ex.Message);
            Assert.Null(await backups.RunIfDueAsync());
            Assert.Empty(await backups.GetRecentAsync());
        }

        Assert.True((await backups.BackupAsync(BackupKind.Manual, db.Admin.Id)).Succeeded); // lock released
    }

    [Theory]
    [InlineData(@"D:\Backups", @"D:\Backups\pos.bak")]
    [InlineData(@"\\shop-pc\backups\", @"\\shop-pc\backups\pos.bak")]
    [InlineData("/var/opt/mssql/backup/", "/var/opt/mssql/backup/pos.bak")]
    public void Server_paths_keep_the_servers_separator(string folder, string expected) =>
        Assert.Equal(expected, BackupService.CombineServerPath(folder, "pos.bak"));
}
