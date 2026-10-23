using ClothingStore.Core;
using ClothingStore.Data;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClothingStore.Tests;

/// <summary>Databases created by earlier builds must upgrade cleanly.</summary>
public class MigrationUpgradeTests
{
    [Fact]
    public async Task A_database_that_ran_the_first_delivery_payments_migration_upgrades()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync("RemoveOnlineOrders");

        // What the earlier build's 20261006192415_AddDeliveryPayments left behind ("Column name 'Courier' ... specified
        // more than once" when the renamed migration then ran).
        await ctx.Database.ExecuteSqlRawAsync("""
            ALTER TABLE [Sales] ADD [Courier] nvarchar(100) NULL, [DeliverySettlementId] int NULL;
            CREATE TABLE [DeliverySettlements] (
                [Id] int NOT NULL IDENTITY(1, 1), [CreatedAt] datetime2 NOT NULL, [UserId] int NOT NULL, [ShiftId] int NULL,
                [Courier] nvarchar(100) NULL, [Method] int NOT NULL, [Expected] decimal(18,2) NOT NULL, [Received] decimal(18,2) NOT NULL,
                [ReceivedLbp] decimal(18,2) NOT NULL, [ExchangeRate] decimal(18,2) NOT NULL, [Reference] nvarchar(300) NULL,
                CONSTRAINT [PK_DeliverySettlements] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_DeliverySettlements_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]));
            """);
        await ctx.Database.ExecuteSqlRawAsync("""
            CREATE INDEX [IX_Sales_Courier] ON [Sales] ([Courier]);
            CREATE INDEX [IX_Sales_DeliverySettlementId] ON [Sales] ([DeliverySettlementId]);
            CREATE INDEX [IX_DeliverySettlements_CreatedAt] ON [DeliverySettlements] ([CreatedAt]);
            CREATE INDEX [IX_DeliverySettlements_UserId] ON [DeliverySettlements] ([UserId]);
            ALTER TABLE [Sales] ADD CONSTRAINT [FK_Sales_DeliverySettlements_DeliverySettlementId]
                FOREIGN KEY ([DeliverySettlementId]) REFERENCES [DeliverySettlements] ([Id]);
            INSERT [__EFMigrationsHistory] (MigrationId, ProductVersion) VALUES (N'20261006192415_AddDeliveryPayments', N'10.0.0');
            """);

        await migrator.MigrateAsync();

        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        var tee = await db.CreateTeeAsync();
        var sale = await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, CustomerId = await db.ShopperIdAsync(), Lines = [new CheckoutLine(tee.Variants[0].Id, 1)],
            Channel = SalesChannel.Instagram, Courier = "Toters", DeliveryReference = "trk-1",
            Payments = [new PaymentInput(PaymentMethod.Delivery, 22m)],
        });
        Assert.Equal(sale.Id, (await db.Deliveries.FindAsync("TRK-1"))!.SaleId);
    }
}

public class StartupUpgradeCheckTests
{
    [Fact]
    public async Task An_up_to_date_database_needs_no_upgrade_and_an_older_one_does()
    {
        await using var db = await TestDatabase.CreateAsync();
        var initializer = new ClothingStore.Data.Seeding.DatabaseInitializer(db.Factory);
        Assert.False(await initializer.NeedsUpgradeAsync());

        await using var ctx = await db.Factory.CreateDbContextAsync();
        await ctx.GetService<IMigrator>().MigrateAsync("SaleConcurrencyChecks");
        Assert.True(await initializer.NeedsUpgradeAsync());
    }
}

public class ServerMemoryLimitTests
{
    [Theory]
    [InlineData(2048, 1024)]
    [InlineData(8192, 2048)]
    [InlineData(16384, 4096)]
    [InlineData(65536, 4096)]
    public void The_limit_is_a_quarter_of_the_machine_between_1_and_4_GB(long physicalMb, int expectedMb) =>
        Assert.Equal(expectedMb, ClothingStore.Data.Seeding.DatabaseInitializer.RecommendedServerMemoryMb(physicalMb));

    /// <summary>Out-of-the-box SQL Server (no limit) gets one at startup; a limit somebody chose is left alone.</summary>
    [Fact]
    public async Task Startup_limits_an_unlimited_server_but_keeps_a_chosen_limit()
    {
        await using var db = await TestDatabase.CreateAsync();
        var initializer = new ClothingStore.Data.Seeding.DatabaseInitializer(db.Factory);
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var original = await MaxServerMemoryAsync(ctx);
        try
        {
            await SetMaxServerMemoryAsync(ctx, ClothingStore.Data.Seeding.DatabaseInitializer.UnlimitedServerMemoryMb);
            await initializer.InitializeAsync(seedDemoData: false);
            var physicalMb = await ctx.Database.SqlQueryRaw<long>("SELECT physical_memory_kb / 1024 AS Value FROM sys.dm_os_sys_info").SingleAsync();
            Assert.Equal(ClothingStore.Data.Seeding.DatabaseInitializer.RecommendedServerMemoryMb(physicalMb), await MaxServerMemoryAsync(ctx));

            await SetMaxServerMemoryAsync(ctx, 3000);
            await initializer.InitializeAsync(seedDemoData: false);
            Assert.Equal(3000, await MaxServerMemoryAsync(ctx));
        }
        finally
        {
            await SetMaxServerMemoryAsync(ctx, original);
        }
    }

    private static Task<int> MaxServerMemoryAsync(PosDbContext ctx) => ctx.Database
        .SqlQueryRaw<int>("SELECT CAST(value_in_use AS int) AS Value FROM sys.configurations WHERE name = 'max server memory (MB)'")
        .SingleAsync();

    private static Task SetMaxServerMemoryAsync(PosDbContext ctx, int mb) => ctx.Database.ExecuteSqlAsync(
        $"EXEC sp_configure 'show advanced options', 1; RECONFIGURE; EXEC sp_configure 'max server memory (MB)', {mb}; RECONFIGURE;");
}
