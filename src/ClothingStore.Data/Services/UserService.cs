using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Data.Services;

public class UserService(IDbContextFactory<PosDbContext> factory)
{
    public const int MinPasswordLength = 6;

    public async Task<User?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;

        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username.Trim(), ct);
        if (user is null || !user.IsActive || !PasswordHasher.Verify(password, user.PasswordHash)) return null;

        user.LastLoginAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>Validates a manager/admin's credentials for an override (discount, return window...).</summary>
    public async Task<User?> AuthorizeAsync(string username, string password, Permission permission, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username.Trim(), ct);
        if (user is null || !user.IsActive || !PasswordHasher.Verify(password, user.PasswordHash)) return null;
        return Permissions.Has(user.Role, permission) ? user : null;
    }

    /// <summary>True until the default administrator has changed the initial password.</summary>
    public async Task<bool> IsFirstRunAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AnyAsync(u => u.Role == UserRole.Admin && u.IsActive && u.MustChangePassword, ct);
    }

    public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);
    }

    /// <summary>
    /// Creates or updates an account on behalf of <paramref name="actingUserId"/>. Admins can manage every account;
    /// managers can only create and edit cashiers (and can't turn anyone into a manager or admin).
    /// </summary>
    public async Task<User> SaveAsync(User user, string? newPassword, int actingUserId, CancellationToken ct = default)
    {
        user.Username = user.Username.Trim();
        user.FullName = user.FullName.Trim();
        if (user.Username.Length < 3) throw new BusinessRuleException(Loc.T("Err.UsernameShort"));
        if (user.Username.Any(char.IsWhiteSpace)) throw new BusinessRuleException(Loc.T("Err.UsernameSpaces"));
        if (user.FullName.Length == 0) throw new BusinessRuleException(Loc.T("Err.FullNameRequired"));
        if (user.Id == 0 && string.IsNullOrEmpty(newPassword)) throw new BusinessRuleException(Loc.T("Err.PasswordRequired"));
        if (!string.IsNullOrEmpty(newPassword)) ValidatePassword(newPassword);

        await using var db = await factory.CreateDbContextAsync(ct);
        var actor = await GetActorAsync(db, actingUserId, ct);
        if (await db.Users.AnyAsync(u => u.Username == user.Username && u.Id != user.Id, ct))
            throw new BusinessRuleException(Loc.T("Err.UsernameTaken", user.Username));

        User entity;
        if (user.Id == 0)
        {
            EnsureCanManage(actor, user.Role);
            entity = new User { CreatedAt = DateTime.Now };
            db.Users.Add(entity);
        }
        else
        {
            entity = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id, ct)
                     ?? throw new BusinessRuleException(Loc.T("Err.UserNotFound"));
            EnsureCanManage(actor, entity.Role);
            EnsureCanManage(actor, user.Role);
            if (entity.Id == actor.Id && !user.IsActive) throw new BusinessRuleException(Loc.T("Err.CantDeactivateSelf"));
            var losingAdmin = entity.Role == UserRole.Admin && entity.IsActive && (user.Role != UserRole.Admin || !user.IsActive);
            if (losingAdmin && !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct))
                throw new BusinessRuleException(Loc.T("Err.NeedAdmin"));
        }

        entity.Username = user.Username;
        entity.FullName = user.FullName;
        entity.Role = user.Role;
        entity.IsActive = user.IsActive;
        if (!string.IsNullOrEmpty(newPassword))
        {
            entity.PasswordHash = PasswordHasher.Hash(newPassword);
            entity.MustChangePassword = user.MustChangePassword;
        }

        await db.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>Sets a temporary password that must be changed at the next sign-in (forgotten passwords).</summary>
    public async Task ResetPasswordAsync(int userId, string temporaryPassword, int actingUserId, CancellationToken ct = default)
    {
        ValidatePassword(temporaryPassword);
        await using var db = await factory.CreateDbContextAsync(ct);
        var actor = await GetActorAsync(db, actingUserId, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new BusinessRuleException(Loc.T("Err.UserNotFound"));
        EnsureCanManage(actor, user.Role);
        user.PasswordHash = PasswordHasher.Hash(temporaryPassword);
        user.MustChangePassword = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Turns an account on or off (off = can't sign in; its history stays).</summary>
    public async Task SetActiveAsync(int userId, bool active, int actingUserId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var actor = await GetActorAsync(db, actingUserId, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new BusinessRuleException(Loc.T("Err.UserNotFound"));
        EnsureCanManage(actor, user.Role);
        if (!active && user.Id == actor.Id) throw new BusinessRuleException(Loc.T("Err.CantDeactivateSelf"));
        if (!active && user.Role == UserRole.Admin && user.IsActive &&
            !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct))
            throw new BusinessRuleException(Loc.T("Err.NeedAdmin"));
        user.IsActive = active;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Deletes an account that was never used (no sales, shifts, returns...). Accounts with history can only be
    /// deactivated, so reports keep showing who did what.
    /// </summary>
    public async Task DeleteAsync(int userId, int actingUserId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var actor = await GetActorAsync(db, actingUserId, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new BusinessRuleException(Loc.T("Err.UserNotFound"));
        EnsureCanManage(actor, user.Role);
        if (user.Id == actor.Id) throw new BusinessRuleException(Loc.T("Err.CantDeleteSelf"));
        if (user.Role == UserRole.Admin && !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct))
            throw new BusinessRuleException(Loc.T("Err.NeedAdmin"));

        // Stock movements would silently lose who made them (their link is set to null), so they count as history too.
        if (await db.StockMovements.AnyAsync(m => m.UserId == user.Id, ct))
            throw new BusinessRuleException(Loc.T("Err.UserHasHistory", user.Username));

        db.Users.Remove(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new BusinessRuleException(Loc.T("Err.UserHasHistory", user.Username));
        }
    }

    private static async Task<User> GetActorAsync(PosDbContext db, int actingUserId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actingUserId && u.IsActive, ct);
        if (actor is null || !(Permissions.Has(actor.Role, Permission.ManageUsers) || Permissions.Has(actor.Role, Permission.ManageCashiers)))
            throw new BusinessRuleException(Loc.T("Err.UsersNotAllowed"));
        return actor;
    }

    /// <summary>Managers may only touch cashier accounts.</summary>
    private static void EnsureCanManage(User actor, UserRole role)
    {
        if (Permissions.Has(actor.Role, Permission.ManageUsers)) return;
        if (role != UserRole.Cashier) throw new BusinessRuleException(Loc.T("Err.ManagerCashiersOnly"));
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        ValidatePassword(newPassword);
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new BusinessRuleException(Loc.T("Err.UserNotFound"));
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            throw new BusinessRuleException(Loc.T("Err.CurrentPasswordWrong"));
        if (currentPassword == newPassword)
            throw new BusinessRuleException(Loc.T("Err.PasswordSame"));

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetPreferredLanguageAsync(int userId, string language, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.PreferredLanguage, Loc.Normalize(language)), ct);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < MinPasswordLength)
            throw new BusinessRuleException(Loc.T("Err.PasswordShort", MinPasswordLength));
    }
}
