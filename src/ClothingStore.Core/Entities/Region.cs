namespace ClothingStore.Core.Entities;

/// <summary>A state / governorate for customer addresses. Starts with Lebanon's governorates; more can be added.</summary>
public class Region : Entity
{
    public string Name { get; set; } = "";

    /// <summary>Arabic name, shown when the app is in Arabic (falls back to <see cref="Name"/>).</summary>
    public string? NameAr { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public string DisplayName => Localization.Loc.IsRightToLeft && !string.IsNullOrWhiteSpace(NameAr) ? NameAr! : Name;

    public override string ToString() => DisplayName;
}
