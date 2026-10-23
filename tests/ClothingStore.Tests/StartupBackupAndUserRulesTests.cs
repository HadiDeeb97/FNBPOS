using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data;
using ClothingStore.Data.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ClothingStore.Tests;

public class StartupBackupTests
{
    [Fact]
    public async Task An_existing_database_is_backed_up_at_startup_but_not_again_minutes_later()
    {
        await using var db = await TestDatabase.CreateAsync();
        var backups = new BackupService(db.Factory);

        var record = await backups.BackupOnStartupAsync();
        Assert.NotNull(record);
        Assert.True(record.Succeeded, record.Error);
        Assert.Matches(@"_startup_\d{8}-\d{6}-\d{3}\.bak$", record.FilePath);
        Assert.Equal(BackupKind.Startup, Assert.Single(await backups.GetRecentAsync()).Kind);

        // A second till opening a minute later doesn't make another one.
        Assert.Null(await backups.BackupOnStartupAsync());
    }

    [Fact]
    public async Task No_database_yet_means_nothing_to_back_up()
    {
        var factory = Factory($"ClothingStorePOS_Test_{Guid.NewGuid():N}");
        Assert.Null(await new BackupService(factory).BackupOnStartupAsync());
    }

    [Fact]
    public async Task Works_on_a_database_from_an_older_version_before_it_is_upgraded()
    {
        // Only the tables every version has, without the columns this version added.
        var name = $"ClothingStorePOS_Test_{Guid.NewGuid():N}";
        await ExecuteAsync("master", $"CREATE DATABASE [{name}]");
        try
        {
            await ExecuteAsync(name, """
                CREATE TABLE Settings (Id int IDENTITY PRIMARY KEY, StoreName nvarchar(100) NOT NULL, BackupFolder nvarchar(500) NULL);
                INSERT INTO Settings (StoreName) VALUES (N'Old store');
                CREATE TABLE BackupRecords (Id int IDENTITY PRIMARY KEY, StartedAt datetime2 NOT NULL, CompletedAt datetime2 NOT NULL,
                    Kind int NOT NULL, FilePath nvarchar(500) NULL, Succeeded bit NOT NULL, Error nvarchar(2000) NULL, UserId int NULL);
                """);

            var record = await new BackupService(Factory(name)).BackupOnStartupAsync();
            Assert.True(record!.Succeeded, record.Error);
            Assert.Equal(1, await ScalarAsync(name, "SELECT COUNT(*) FROM BackupRecords WHERE Kind = 3 AND Succeeded = 1"));
        }
        finally
        {
            await ExecuteAsync("master", $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        }
    }

    private static string ConnectionString(string database) =>
        new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(TestDatabase.ServerVariable)) { InitialCatalog = database }.ConnectionString;

    private static IDbContextFactory<PosDbContext> Factory(string database) =>
        new PooledDbContextFactory<PosDbContext>(new DbContextOptionsBuilder<PosDbContext>().UseSqlServer(ConnectionString(database)).Options);

    private static async Task ExecuteAsync(string database, string sql)
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(string database, string sql)
    {
        await using var connection = new SqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}

public class UserRulesTests
{
    [Fact]
    public async Task Managers_add_and_edit_cashiers_only()
    {
        await using var db = await TestDatabase.CreateAsync();

        var cashier = await db.Users.SaveAsync(new User { Username = "newbie", FullName = "New Cashier", Role = UserRole.Cashier }, "secret9", db.Manager.Id);
        Assert.Equal(UserRole.Cashier, cashier.Role);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Users.SaveAsync(new User { Username = "boss2", FullName = "Second Manager", Role = UserRole.Manager }, "secret9", db.Manager.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Users.SaveAsync(new User { Id = cashier.Id, Username = "newbie", FullName = "Promoted", Role = UserRole.Manager, IsActive = true }, null, db.Manager.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Users.SaveAsync(new User { Id = db.Admin.Id, Username = "admin", FullName = "Renamed", Role = UserRole.Admin, IsActive = true }, null, db.Manager.Id));

        await db.Users.ResetPasswordAsync(cashier.Id, "temp123", db.Manager.Id);
        var signedIn = await db.Users.AuthenticateAsync("newbie", "temp123");
        Assert.True(signedIn!.MustChangePassword);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Users.ResetPasswordAsync(db.Admin.Id, "temp123", db.Manager.Id));

        await db.Users.SetActiveAsync(cashier.Id, false, db.Manager.Id);
        Assert.Null(await db.Users.AuthenticateAsync("newbie", "temp123"));
    }

    [Fact]
    public async Task Cashiers_cannot_manage_accounts()
    {
        await using var db = await TestDatabase.CreateAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            db.Users.SaveAsync(new User { Username = "friend", FullName = "Friend", Role = UserRole.Cashier }, "secret9", db.Cashier.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Users.ResetPasswordAsync(db.Cashier.Id, "temp123", db.Cashier.Id));
    }

    [Fact]
    public async Task Admins_manage_everyone_but_not_lock_themselves_out()
    {
        await using var db = await TestDatabase.CreateAsync();

        var manager2 = await db.Users.SaveAsync(new User { Username = "boss2", FullName = "Second Manager", Role = UserRole.Manager }, "secret9", db.Admin.Id);
        await db.Users.SetActiveAsync(db.Manager.Id, false, db.Admin.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Users.SetActiveAsync(db.Admin.Id, false, db.Admin.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Users.DeleteAsync(db.Admin.Id, db.Admin.Id));

        // Never used: can be deleted. Used (made a sale): only deactivated.
        await db.Users.DeleteAsync(manager2.Id, db.Admin.Id);
        Assert.DoesNotContain(await db.Users.GetAllAsync(), u => u.Id == manager2.Id);

        var tee = await db.CreateTeeAsync();
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Card, 22m)],
        });
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Users.DeleteAsync(db.Cashier.Id, db.Admin.Id));
    }
}
