using UniPos.Core.Features;
using UniPos.Core.Localization;
using UniPos.Data.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UniPos.Desktop.ViewModels;

/// <summary>A till's own choice for a feature that may differ by till.</summary>
public enum TillChoice
{
    SameAsStore,
    On,
    Off,
}

/// <summary>One feature in Settings → Features: on or off for the store, and this till's own choice where it may have one.</summary>
public sealed partial class FeatureRow(FeatureState state) : ObservableObject
{
    public static IReadOnlyList<KeyValuePair<TillChoice, string>> TillChoices { get; } =
    [
        new(TillChoice.SameAsStore, Loc.T("Features.TillSame")),
        new(TillChoice.On, Loc.T("Features.TillOn")),
        new(TillChoice.Off, Loc.T("Features.TillOff")),
    ];

    public Feature Feature { get; } = state.Feature;
    public string Name { get; } = Loc.EnumText(state.Feature);
    public string Hint { get; } = Loc.T($"Features.Hint.{state.Feature}");
    public bool CanVaryByTill { get; } = state.CanVaryByTill;

    [ObservableProperty]
    public partial bool StoreOn { get; set; } = state.StoreIsOn;

    [ObservableProperty]
    public partial TillChoice Till { get; set; } = state.Till switch
    {
        true => TillChoice.On,
        false => TillChoice.Off,
        null => TillChoice.SameAsStore,
    };

    public bool? TillValue => Till switch
    {
        TillChoice.On => true,
        TillChoice.Off => false,
        _ => null,
    };
}
