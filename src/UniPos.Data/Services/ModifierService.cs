using UniPos.Core;
using UniPos.Core.Entities;
using Microsoft.EntityFrameworkCore;
using UniPos.Core.Localization;

namespace UniPos.Data.Services;

/// <summary>Modifier groups ("Size", "Fillings", "Remove") with their options, shared by the items they are attached to.</summary>
public class ModifierService(IDbContextFactory<PosDbContext> factory)
{
    public async Task<List<ModifierGroup>> GetGroupsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var groups = await db.ModifierGroups.AsNoTracking()
            .Include(g => g.Options)
            .Where(g => includeInactive || g.IsActive)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);
        foreach (var g in groups) g.Options = g.Options.Where(o => includeInactive || o.IsActive).OrderBy(o => o.SortOrder).ThenBy(o => o.Id).ToList();
        return groups;
    }

    /// <summary>How many items offer each group (for the groups list, and before deleting one).</summary>
    public async Task<Dictionary<int, int>> CountItemsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ProductModifierGroups.GroupBy(l => l.ModifierGroupId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    /// <summary>
    /// Creates or updates a group with its options, in the order given. Options left out are removed; sales that had
    /// them keep them by name.
    /// </summary>
    public async Task<ModifierGroup> SaveGroupAsync(ModifierGroup group, CancellationToken ct = default)
    {
        var name = group.Name.Trim();
        if (name.Length == 0) throw new BusinessRuleException(Loc.T("Err.GroupNameRequired"));
        var options = group.Options.Where(o => !string.IsNullOrWhiteSpace(o.Name)).ToList();
        if (options.Count == 0) throw new BusinessRuleException(Loc.T("Err.GroupNeedsOption"));
        if (options.GroupBy(o => o.Name.Trim(), StringComparer.CurrentCultureIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new BusinessRuleException(Loc.T("Err.OptionNameTwice", twice.Key));
        if (options.Any(o => o.Price < 0 || o.Cost < 0)) throw new BusinessRuleException(Loc.T("Err.PriceCostNegative"));
        if (group.MinChoices < 0 || group.MaxChoices < 0) throw new BusinessRuleException(Loc.T("Err.ChoicesNegative"));
        if (group.MaxChoices > 0 && group.MinChoices > group.MaxChoices) throw new BusinessRuleException(Loc.T("Err.MinAboveMax"));
        if (group.MinChoices > options.Count) throw new BusinessRuleException(Loc.T("Err.MinAboveOptions", options.Count));

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.ModifierGroups.AnyAsync(g => g.Id != group.Id && g.Name == name, ct))
            throw new BusinessRuleException(Loc.T("Err.GroupNameTaken", name));

        ModifierGroup entity;
        if (group.Id == 0)
        {
            entity = new ModifierGroup();
            db.ModifierGroups.Add(entity);
        }
        else
        {
            entity = await db.ModifierGroups.Include(g => g.Options).FirstOrDefaultAsync(g => g.Id == group.Id, ct)
                     ?? throw new BusinessRuleException(Loc.T("Err.GroupNotFound"));
        }
        entity.Name = name;
        entity.MinChoices = group.MinChoices;
        entity.MaxChoices = group.MaxChoices;
        entity.IsActive = group.IsActive;

        var keep = options.Where(o => o.Id != 0).Select(o => o.Id).ToHashSet();
        foreach (var gone in entity.Options.Where(o => !keep.Contains(o.Id)).ToList()) db.Modifiers.Remove(gone);
        foreach (var (incoming, order) in options.Select((o, i) => (o, i)))
        {
            var option = incoming.Id == 0
                ? new Modifier()
                : entity.Options.FirstOrDefault(o => o.Id == incoming.Id) ?? throw new BusinessRuleException(Loc.T("Err.GroupNotFound"));
            if (incoming.Id == 0) entity.Options.Add(option);
            option.Name = incoming.Name.Trim();
            option.Price = Money.Round(incoming.Price);
            option.Cost = Money.Round(incoming.Cost);
            option.SoldOut = incoming.SoldOut;
            option.IsActive = incoming.IsActive;
            option.SortOrder = order;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (QueryHelpers.IsUniqueViolation(ex))
        {
            throw new BusinessRuleException(Loc.T("Err.GroupNameTaken", name));
        }
        return (await GetGroupsAsync(includeInactive: true, ct)).First(g => g.Id == entity.Id);
    }

    /// <summary>Deletes a group: the items offering it stop asking; sales that had its options keep them by name.</summary>
    public async Task DeleteGroupAsync(int groupId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.ModifierGroups.Where(g => g.Id == groupId).ExecuteDeleteAsync(ct);
    }
}
