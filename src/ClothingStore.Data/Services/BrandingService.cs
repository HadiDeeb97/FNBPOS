using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Data.Services;

public static class StoreLogoLimits
{
    /// <summary>The app resizes logos to 256 x 256 PNG first, which is far below this.</summary>
    public const int MaxBytes = 1024 * 1024;
}

/// <summary>The store logo, shared by every till.</summary>
public class BrandingService(IDbContextFactory<PosDbContext> factory)
{
    public async Task<StoreLogo?> GetLogoAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.StoreLogos.AsNoTracking().OrderBy(l => l.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>When the logo last changed (null = no logo), so other tills can check cheaply whether to reload it.</summary>
    public async Task<DateTime?> GetLogoVersionAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.StoreLogos.AsNoTracking().OrderBy(l => l.Id).Select(l => (DateTime?)l.UpdatedAt).FirstOrDefaultAsync(ct);
    }

    /// <summary>Replaces the logo; null or empty removes it. Managers and admins only.</summary>
    public async Task<DateTime?> SetLogoAsync(byte[]? image, int userId, CancellationToken ct = default)
    {
        if (image is { Length: > StoreLogoLimits.MaxBytes }) throw new BusinessRuleException(Loc.T("Err.LogoTooLarge"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || !Permissions.Has(user.Role, Permission.ManageBranding))
            throw new BusinessRuleException(Loc.T("Err.LogoNotAllowed"));

        var existing = await db.StoreLogos.ToListAsync(ct);
        db.StoreLogos.RemoveRange(existing);
        if (image is not { Length: > 0 })
        {
            await db.SaveChangesAsync(ct);
            return null;
        }

        var logo = new StoreLogo { Image = image, UpdatedAt = DateTime.Now, UpdatedByUserId = userId };
        db.StoreLogos.Add(logo);
        await db.SaveChangesAsync(ct);
        return logo.UpdatedAt;
    }
}
