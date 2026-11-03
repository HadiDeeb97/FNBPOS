using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Features;
using UniPos.Core.Localization;
using UniPos.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace UniPos.Data.Services;

/// <summary>One feature on the Settings page: its venue default, the store's choice and this till's own.</summary>
/// <param name="Store">Null: the venue's default.</param>
/// <param name="Till">Null: as for the store (always null when the feature can't vary by till).</param>
public sealed record FeatureState(Feature Feature, bool VenueDefault, bool? Store, bool? Till)
{
    public bool CanVaryByTill => FeatureCatalog.CanVaryByTill(Feature);
    public bool StoreIsOn => Store ?? VenueDefault;
    public bool IsOn => Till ?? StoreIsOn;
}

public sealed record FeatureOverview(Venue? Venue, IReadOnlyList<FeatureState> Features);

/// <summary>What a manager chose on the Settings page or the setup screen.</summary>
/// <param name="Store">Each feature for the whole store (null: the venue's default). Features left out are reset to it.</param>
/// <param name="Till">This till's own choices (null: as for the store), or null to leave them as they are.</param>
public sealed record FeatureChoices(Venue Venue, IReadOnlyDictionary<Feature, bool?> Store, string? TillId = null,
    IReadOnlyDictionary<Feature, bool?>? Till = null);

/// <summary>
/// The store's kind of place and the features switched on or off for the store and for each till. Switching a feature
/// off hides it and stops new work with it; nothing recorded with it is deleted. It is refused while something still
/// needs that feature's screens (money a delivery company owes, an order placed with a supplier).
/// </summary>
public class FeatureService(IDbContextFactory<PosDbContext> factory, DeliveryService deliveries)
{
    /// <summary>The features on at <paramref name="tillId"/> (null: the store's, ignoring every till's own choices).</summary>
    public async Task<FeatureSet> GetAsync(string? tillId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ReadAsync(db, tillId, ct);
    }

    internal static async Task<FeatureSet> ReadAsync(PosDbContext db, string? tillId, CancellationToken ct)
    {
        var venue = await db.Settings.AsNoTracking().OrderBy(s => s.Id).Select(s => s.Venue).FirstOrDefaultAsync(ct);
        var switches = await db.FeatureSwitches.AsNoTracking().Where(s => s.TillId == null || s.TillId == tillId).ToListAsync(ct);
        return FeatureSet.Resolve(venue, switches, tillId);
    }

    public async Task<FeatureOverview> GetOverviewAsync(string tillId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var venue = await db.Settings.AsNoTracking().OrderBy(s => s.Id).Select(s => s.Venue).FirstOrDefaultAsync(ct);
        var switches = await db.FeatureSwitches.AsNoTracking().Where(s => s.TillId == null || s.TillId == tillId).ToListAsync(ct);
        return new FeatureOverview(venue, FeatureCatalog.All.Select(f => new FeatureState(
            f,
            Presets.IsOn(venue, f),
            switches.FirstOrDefault(s => s.Feature == f && s.TillId == null)?.IsOn,
            FeatureCatalog.CanVaryByTill(f) ? switches.FirstOrDefault(s => s.Feature == f && s.TillId == tillId)?.IsOn : null)).ToList());
    }

    public async Task SaveAsync(FeatureChoices choices, int userId, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(choices.Venue)) throw new BusinessRuleException(Loc.T("Err.ChooseVenue"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || !Permissions.Has(user.Role, Permission.ManageSettings))
            throw new BusinessRuleException(Loc.T("Err.FeaturesNotAllowed"));

        var settings = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new StoreSettings();
            db.Settings.Add(settings);
        }
        var before = await ReadAsync(db, null, ct);
        var tillBefore = choices.TillId is null ? null : await ReadAsync(db, choices.TillId, ct);

        settings.Venue = choices.Venue;
        var rows = await db.FeatureSwitches.Where(s => s.TillId == null || s.TillId == choices.TillId).ToListAsync(ct);
        foreach (var feature in FeatureCatalog.All)
        {
            // A choice that matches the venue's default is not kept, so the store follows its venue for it.
            var store = choices.Store.GetValueOrDefault(feature);
            if (store == Presets.IsOn(choices.Venue, feature)) store = null;
            Put(db, rows, feature, null, store, userId);

            if (choices.TillId is not null && choices.Till is not null && FeatureCatalog.CanVaryByTill(feature))
                Put(db, rows, feature, choices.TillId, choices.Till.GetValueOrDefault(feature), userId);
        }

        // What would go out of sight: for the store, and at this till.
        var after = FeatureSet.Resolve(choices.Venue, rows.Where(r => db.Entry(r).State != EntityState.Deleted), null);
        var turnedOff = FeatureCatalog.All.Where(f => before.IsOn(f) && !after.IsOn(f)).ToList();
        if (tillBefore is not null)
        {
            var tillAfter = FeatureSet.Resolve(choices.Venue, rows.Where(r => db.Entry(r).State != EntityState.Deleted), choices.TillId);
            turnedOff.AddRange(FeatureCatalog.All.Where(f => tillBefore.IsOn(f) && !tillAfter.IsOn(f) && !turnedOff.Contains(f)));
        }
        foreach (var feature in turnedOff)
            if (await WhyNeededAsync(db, feature, ct) is { } reason)
                throw new BusinessRuleException(Loc.T("Err.FeatureStillNeeded", Loc.EnumText(feature), reason));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds, changes or removes the row for <paramref name="feature"/> at <paramref name="tillId"/>.</summary>
    private static void Put(PosDbContext db, List<FeatureSwitch> rows, Feature feature, string? tillId, bool? value, int userId)
    {
        var row = rows.FirstOrDefault(r => r.Feature == feature && r.TillId == tillId);
        if (value is null)
        {
            if (row is not null) db.FeatureSwitches.Remove(row);
            return;
        }
        var text = value.Value ? FeatureSwitch.On : FeatureSwitch.Off;
        if (row is null)
        {
            row = new FeatureSwitch { Feature = feature, TillId = tillId };
            db.FeatureSwitches.Add(row);
            rows.Add(row);
        }
        else if (row.Value == text)
        {
            return;
        }
        row.Value = text;
        row.ChangedAt = DateTime.Now;
        row.ChangedByUserId = userId;
    }

    /// <summary>Why <paramref name="feature"/> can't be switched off yet, or null when it can.</summary>
    private async Task<string?> WhyNeededAsync(PosDbContext db, Feature feature, CancellationToken ct)
    {
        switch (feature)
        {
            case Feature.Delivery:
                // Money a delivery company still owes is settled on the Deliveries page, which would go out of sight.
                var owed = (await deliveries.GetBalancesAsync(ct)).Sum(b => b.Orders);
                return owed > 0 ? Loc.T("Features.DeliveryOwed", owed) : null;

            case Feature.Purchasing:
                // Goods ordered from a supplier are received on the purchase orders page.
                var open = await db.PurchaseOrders.CountAsync(
                    p => p.Status == PurchaseOrderStatus.Ordered || p.Status == PurchaseOrderStatus.PartiallyReceived, ct);
                return open > 0 ? Loc.T("Features.OrdersOpen", open) : null;

            default:
                return null;
        }
    }
}
