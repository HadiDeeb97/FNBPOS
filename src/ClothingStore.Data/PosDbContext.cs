using ClothingStore.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Data;

public class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SaleReturn> Returns => Set<SaleReturn>();
    public DbSet<SaleReturnLine> ReturnLines => Set<SaleReturnLine>();
    public DbSet<SaleReturnRefund> ReturnRefunds => Set<SaleReturnRefund>();
    public DbSet<HeldSale> HeldSales => Set<HeldSale>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftHandover> ShiftHandovers => Set<ShiftHandover>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<StoreSettings> Settings => Set<StoreSettings>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<ExchangeRateChange> ExchangeRateChanges => Set<ExchangeRateChange>();
    public DbSet<StoreLogo> StoreLogos => Set<StoreLogo>();
    public DbSet<AppState> AppState => Set<AppState>();
    public DbSet<DeliverySettlement> DeliverySettlements => Set<DeliverySettlement>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<DeliveryPartner> DeliveryPartners => Set<DeliveryPartner>();

    /// <summary>
    /// Usernames, SKUs and category/supplier names are unique regardless of case, even if the
    /// database was created with a case-sensitive default collation.
    /// </summary>
    private const string CaseInsensitive = "Latin1_General_100_CI_AS";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money (prices, costs, totals, balances) is stored to the cent; rates override this below.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(50).UseCollation(CaseInsensitive).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            e.Property(x => x.PreferredLanguage).HasMaxLength(10);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).UseCollation(CaseInsensitive).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Supplier>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).UseCollation(CaseInsensitive).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Name);
            // Narrow copy of what product searches look at, so a search reads this instead of whole product rows.
            e.HasIndex(x => x.IsActive).IncludeProperties(x => new { x.Name, x.Brand, x.StyleCode, x.CategoryId });
            e.HasOne(x => x.Category).WithMany(c => c.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Variants).WithOne(v => v.Product).HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProductVariant>(e =>
        {
            e.Property(x => x.Sku).HasMaxLength(64).UseCollation(CaseInsensitive).IsRequired();
            e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            // Likewise for variants: active ones with the columns a search matches on.
            e.HasIndex(x => new { x.IsActive, x.ProductId }).IncludeProperties(x => new { x.Sku, x.Color, x.Size, x.Barcode });
            e.Property(x => x.Size).HasMaxLength(20);
            e.Property(x => x.Color).HasMaxLength(40);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Ignore(x => x.EffectivePrice);
            e.Ignore(x => x.EffectiveCost);
            e.Ignore(x => x.IsLowStock);
            e.Ignore(x => x.Description);
            e.Ignore(x => x.DisplayName);
        });

        b.Entity<Customer>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.HasIndex(x => x.Phone).IsUnique().HasFilter("[Phone] IS NOT NULL");
            e.Property(x => x.Email).HasMaxLength(150);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Ignore(x => x.FullName);
            e.Property(x => x.Address).HasMaxLength(300);
            e.HasOne(x => x.Region).WithMany().HasForeignKey(x => x.RegionId).OnDelete(DeleteBehavior.SetNull);
            e.Ignore(x => x.TotalSpent);
            e.Ignore(x => x.Visits);
            e.Ignore(x => x.LastVisit);
            e.Ignore(x => x.FullAddress);
        });

        b.Entity<Sale>(e =>
        {
            e.Property(x => x.ReceiptNumber).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.ReceiptNumber).IsUnique();
            e.Ignore(x => x.RefundedTotal);
            // Sales history and reports pick sales by date; carrying status, channel and total lets the totals be added
            // up from the index alone.
            e.HasIndex(x => x.CreatedAt).IncludeProperties(x => new { x.Status, x.Channel, x.Total });
            e.Property(x => x.TaxRate).HasPrecision(9, 4);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(l => l.Sale).HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Payments).WithOne(p => p.Sale).HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Courier).HasMaxLength(100);
            e.HasIndex(x => x.Courier);
            e.Property(x => x.DeliveryReference).HasMaxLength(100).UseCollation(CaseInsensitive);
            e.HasIndex(x => x.DeliveryReference);
            e.HasOne(x => x.DeliverySettlement).WithMany(d => d.Sales).HasForeignKey(x => x.DeliverySettlementId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.ItemCount);
            // Void, return and delivery settlement each check the sale wasn't voided or settled at another till meanwhile.
            e.Property(x => x.Status).IsConcurrencyToken();
            e.Property(x => x.DeliverySettlementId).IsConcurrencyToken();
        });

        b.Entity<SaleLine>(e =>
        {
            e.HasIndex(x => x.SaleId).IncludeProperties(x => x.Quantity); // item counts in sales history
            e.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            // Two tills returning the same item at once: the second save fails instead of refunding it twice.
            e.Property(x => x.ReturnedQuantity).IsConcurrencyToken();
            e.Ignore(x => x.ReturnableQuantity);
        });

        b.Entity<SaleReturn>(e =>
        {
            e.Property(x => x.ReturnNumber).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.ReturnNumber).IsUnique();
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.SaleId).IncludeProperties(x => x.TotalRefund); // returned amounts in sales history
            e.HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(l => l.SaleReturn).HasForeignKey(l => l.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Refunds).WithOne(r => r.SaleReturn).HasForeignKey(r => r.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SaleReturnLine>(e =>
            e.HasOne(x => x.SaleLine).WithMany().HasForeignKey(x => x.SaleLineId).OnDelete(DeleteBehavior.Restrict));

        b.Entity<StockMovement>(e =>
        {
            e.HasIndex(x => new { x.ProductVariantId, x.CreatedAt });
            e.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PurchaseOrder>(e =>
        {
            e.Property(x => x.OrderNumber).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(l => l.PurchaseOrder).HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            // Receiving and cancelling on two PCs at once: the second save is refused instead of overwriting the first.
            e.Property(x => x.Status).IsConcurrencyToken();
            e.Ignore(x => x.Total);
            e.Ignore(x => x.TotalUnits);
        });

        b.Entity<PurchaseOrderLine>(e =>
        {
            e.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.QuantityReceived).IsConcurrencyToken();
            e.Ignore(x => x.LineTotal);
            e.Ignore(x => x.QuantityOutstanding);
        });

        b.Entity<Shift>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasIndex(x => new { x.TillId, x.Status });
            e.Property(x => x.TillId).HasMaxLength(40);
            e.Property(x => x.TillName).HasMaxLength(100);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CurrentUser).WithMany().HasForeignKey(x => x.CurrentUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ClosedBy).WithMany().HasForeignKey(x => x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Handovers).WithOne(h => h.Shift).HasForeignKey(h => h.ShiftId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.RunningUserId);
            // Closing and taking over the same drawer at the same moment: one of them is refused.
            e.Property(x => x.CurrentUserId).IsConcurrencyToken();
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasMany(x => x.CashMovements).WithOne(m => m.Shift).HasForeignKey(m => m.ShiftId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.Variance);
            e.Ignore(x => x.VarianceLbp);
        });

        b.Entity<ShiftHandover>(e =>
        {
            e.HasOne(x => x.FromUser).WithMany().HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToUser).WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<HeldSale>(e => e.Property(x => x.Label).HasMaxLength(100));

        b.Entity<BackupRecord>(e =>
        {
            e.HasIndex(x => x.StartedAt);
            e.Property(x => x.FilePath).HasMaxLength(500);
            e.Property(x => x.Error).HasMaxLength(2000);
        });

        b.Entity<StoreSettings>(e =>
        {
            e.Property(x => x.TaxRate).HasPrecision(9, 4);
            e.Property(x => x.MaxCashierDiscountPercent).HasPrecision(9, 4);
            e.Property(x => x.LoyaltyPointsPerUnit).HasPrecision(18, 4);
            e.Property(x => x.LoyaltyPointValue).HasPrecision(18, 4);
            e.Property(x => x.ReceiptLanguage).HasMaxLength(10);
            e.Property(x => x.CustomerDisplayMessage).HasMaxLength(200);
            e.Ignore(x => x.ActiveLbpRate);
        });

        b.Entity<Region>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).UseCollation(CaseInsensitive).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Ignore(x => x.DisplayName);
            // Lebanon's governorates to start with; stores can add more.
            e.HasData(
                new Region { Id = 1, Name = "Beirut", NameAr = "بيروت", SortOrder = 1 },
                new Region { Id = 2, Name = "Mount Lebanon", NameAr = "جبل لبنان", SortOrder = 2 },
                new Region { Id = 3, Name = "North", NameAr = "الشمال", SortOrder = 3 },
                new Region { Id = 4, Name = "Akkar", NameAr = "عكار", SortOrder = 4 },
                new Region { Id = 5, Name = "Bekaa", NameAr = "البقاع", SortOrder = 5 },
                new Region { Id = 6, Name = "Baalbek-Hermel", NameAr = "بعلبك الهرمل", SortOrder = 6 },
                new Region { Id = 7, Name = "South", NameAr = "الجنوب", SortOrder = 7 },
                new Region { Id = 8, Name = "Nabatieh", NameAr = "النبطية", SortOrder = 8 });
        });

        b.Entity<DeliveryPartner>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).UseCollation(CaseInsensitive).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.ContactName).HasMaxLength(150);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Phone2).HasMaxLength(40);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Notes).HasMaxLength(1000);
        });

        b.Entity<DeliverySettlement>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Courier).HasMaxLength(100);
            e.Property(x => x.Reference).HasMaxLength(300);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Difference);
        });

        b.Entity<AppState>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Value).HasMaxLength(2000);
        });

        b.Entity<StoreLogo>(e =>
        {
            e.Property(x => x.Image).HasMaxLength(Services.StoreLogoLimits.MaxBytes).IsRequired();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ExchangeRateChange>(e =>
        {
            e.HasIndex(x => x.ChangedAt);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void BumpVersions()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case ProductVariant v: v.Version++; break;
                case Customer c: c.Version++; break;
            }
        }
    }
}
