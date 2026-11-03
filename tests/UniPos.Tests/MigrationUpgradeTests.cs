using UniPos.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace UniPos.Tests;

public class StartupUpgradeCheckTests
{
    [Fact]
    public async Task An_up_to_date_database_needs_no_upgrade_and_an_older_one_does()
    {
        await using var db = await TestDatabase.CreateAsync();
        var initializer = new UniPos.Data.Seeding.DatabaseInitializer(db.Factory);
        Assert.False(await initializer.NeedsUpgradeAsync());

        await using var ctx = await db.Factory.CreateDbContextAsync();
        await ctx.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase); // before the first migration
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
        Assert.Equal(expectedMb, UniPos.Data.Seeding.DatabaseInitializer.RecommendedServerMemoryMb(physicalMb));

    /// <summary>Out-of-the-box SQL Server (no limit) gets one at startup; a limit somebody chose is left alone.</summary>
    [Fact]
    public async Task Startup_limits_an_unlimited_server_but_keeps_a_chosen_limit()
    {
        await using var db = await TestDatabase.CreateAsync();
        var initializer = new UniPos.Data.Seeding.DatabaseInitializer(db.Factory);
        await using var ctx = await db.Factory.CreateDbContextAsync();
        var original = await MaxServerMemoryAsync(ctx);
        try
        {
            await SetMaxServerMemoryAsync(ctx, UniPos.Data.Seeding.DatabaseInitializer.UnlimitedServerMemoryMb);
            await initializer.InitializeAsync(seedDemoData: false);
            var physicalMb = await ctx.Database.SqlQueryRaw<long>("SELECT physical_memory_kb / 1024 AS Value FROM sys.dm_os_sys_info").SingleAsync();
            Assert.Equal(UniPos.Data.Seeding.DatabaseInitializer.RecommendedServerMemoryMb(physicalMb), await MaxServerMemoryAsync(ctx));

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
