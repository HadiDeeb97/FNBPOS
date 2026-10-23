using ClothingStore.Core;
using ClothingStore.Core.Entities;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

public class CategoryService(IDbContextFactory<PosDbContext> factory)
{
    public async Task<List<Category>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Categories.AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<Category> SaveAsync(Category category, CancellationToken ct = default)
    {
        category.Name = category.Name.Trim();
        if (category.Name.Length == 0) throw new BusinessRuleException(Loc.T("Err.CategoryNameRequired"));

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Categories.AnyAsync(c => c.Name == category.Name && c.Id != category.Id, ct))
            throw new BusinessRuleException(Loc.T("Err.CategoryExists", category.Name));

        var entity = category.Id == 0
            ? db.Categories.Add(new Category()).Entity
            : await db.Categories.FindAsync([category.Id], ct) ?? throw new BusinessRuleException(Loc.T("Err.CategoryNotFound"));

        entity.Name = category.Name;
        entity.Description = QueryHelpers.Clean(category.Description);
        entity.IsActive = category.IsActive;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>Deletes an unused category, or deactivates it if products still reference it.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Categories.FindAsync([id], ct);
        if (entity is null) return false;

        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct))
        {
            entity.IsActive = false;
            await db.SaveChangesAsync(ct);
            return false;
        }

        db.Categories.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public class SupplierService(IDbContextFactory<PosDbContext> factory)
{
    public async Task<List<Supplier>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Suppliers.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<Supplier> SaveAsync(Supplier supplier, CancellationToken ct = default)
    {
        supplier.Name = supplier.Name.Trim();
        if (supplier.Name.Length == 0) throw new BusinessRuleException(Loc.T("Err.SupplierNameRequired"));

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Suppliers.AnyAsync(s => s.Name == supplier.Name && s.Id != supplier.Id, ct))
            throw new BusinessRuleException(Loc.T("Err.SupplierExists", supplier.Name));

        var entity = supplier.Id == 0
            ? db.Suppliers.Add(new Supplier()).Entity
            : await db.Suppliers.FindAsync([supplier.Id], ct) ?? throw new BusinessRuleException(Loc.T("Err.SupplierNotFound"));

        entity.Name = supplier.Name;
        entity.ContactName = QueryHelpers.Clean(supplier.ContactName);
        entity.Phone = QueryHelpers.Clean(supplier.Phone);
        entity.Email = QueryHelpers.Clean(supplier.Email);
        entity.Address = QueryHelpers.Clean(supplier.Address);
        entity.Notes = QueryHelpers.Clean(supplier.Notes);
        entity.IsActive = supplier.IsActive;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>Deletes an unused supplier, or deactivates it if it has purchase orders.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Suppliers.FindAsync([id], ct);
        if (entity is null) return false;

        if (await db.PurchaseOrders.AnyAsync(p => p.SupplierId == id, ct))
        {
            entity.IsActive = false;
            await db.SaveChangesAsync(ct);
            return false;
        }

        db.Suppliers.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
