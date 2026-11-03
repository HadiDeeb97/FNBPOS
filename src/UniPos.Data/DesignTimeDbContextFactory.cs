using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UniPos.Data;

/// <summary>Used by <c>dotnet ef migrations add</c> (which does not connect to the server).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PosDbContext>().UseSqlServer(@"Server=(localdb)\MSSQLLocalDB;Database=UniPOS_Design;Trusted_Connection=True").Options);
}
