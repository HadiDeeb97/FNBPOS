using UniPos.Core.Entities;

namespace UniPos.Core.Features;

/// <summary>Which features are on at one till. Services check it before acting; screens hide what is off.</summary>
public sealed class FeatureSet : IEquatable<FeatureSet>
{
    private readonly HashSet<Feature> _on;

    public FeatureSet(IEnumerable<Feature> on) => _on = [.. on];

    /// <summary>Everything on: before the features are loaded, and in a store not set up yet.</summary>
    public static FeatureSet All { get; } = new(FeatureCatalog.All);

    public bool IsOn(Feature feature) => _on.Contains(feature);

    public bool Delivery => IsOn(Feature.Delivery);
    public bool Purchasing => IsOn(Feature.Purchasing);
    public bool KitchenTickets => IsOn(Feature.KitchenTickets);
    public bool OrderNumbers => IsOn(Feature.OrderNumbers);
    public bool Takeaway => IsOn(Feature.Takeaway);

    /// <summary>
    /// The features on at <paramref name="tillId"/>: the till's own switch where it may have one, else the store's,
    /// else the default for the store's kind of place.
    /// </summary>
    public static FeatureSet Resolve(Venue? venue, IEnumerable<FeatureSwitch> switches, string? tillId)
    {
        var list = switches.ToList();
        return new FeatureSet(FeatureCatalog.All.Where(f =>
        {
            var till = FeatureCatalog.CanVaryByTill(f) && tillId is not null
                ? list.FirstOrDefault(s => s.Feature == f && s.TillId == tillId)
                : null;
            var store = list.FirstOrDefault(s => s.Feature == f && s.TillId is null);
            return (till ?? store)?.IsOn ?? Presets.IsOn(venue, f);
        }));
    }

    public bool Equals(FeatureSet? other) => other is not null && _on.SetEquals(other._on);
    public override bool Equals(object? obj) => Equals(obj as FeatureSet);
    public override int GetHashCode() => _on.Aggregate(0, (hash, f) => hash ^ (1 << (int)f));
}
