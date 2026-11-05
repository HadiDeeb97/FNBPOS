using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Features;
using UniPos.Core.Localization;
using UniPos.Data.Services;
using UniPos.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UniPos.Desktop.ViewModels.Dialogs;

/// <summary>A kind of place on the setup screen.</summary>
public sealed partial class VenueOption(Venue venue) : ObservableObject
{
    public Venue Venue { get; } = venue;
    public string Name { get; } = Loc.EnumText(venue);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Asked the first time an admin signs in to a store that isn't set up: its name, what kind of place it is (which
/// decides the features it starts with), tax and Lebanese pounds. "Later" asks again at the next sign-in.
/// </summary>
public sealed partial class FirstRunViewModel(
    IDialogService dialogs, SettingsService settings, FeatureService features, ProductService products, Session session, bool menuIsEmpty)
    : DialogViewModelBase(dialogs)
{
    public override string Title => Loc.T("FirstRun.Title");

    /// <summary>A store with no menu yet can start from a sample one for its kind of place (edited or deleted later).</summary>
    public bool CanAddSampleMenu { get; } = menuIsEmpty;

    [ObservableProperty]
    public partial bool AddSampleMenu { get; set; } = menuIsEmpty;

    public IReadOnlyList<VenueOption> Venues { get; } = Presets.All.Select(v => new VenueOption(v)).ToList();

    /// <summary>Name, tax and LBP, saved like the Settings page does.</summary>
    public StoreSettings Settings { get; } = settings.Current.Clone();

    [ObservableProperty]
    public partial decimal LbpRate { get; set; } = settings.Current.LbpRate;

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (Venues.FirstOrDefault(v => v.IsSelected) is not { } choice) throw new BusinessRuleException(Loc.T("Err.ChooseVenue"));

        // The venue's defaults for every feature: nothing chosen one by one yet.
        await features.SaveAsync(new FeatureChoices(choice.Venue, new Dictionary<Feature, bool?>()), session.User.Id);
        await settings.SaveAsync(Settings.Clone());
        if (Settings.LbpEnabled && LbpRate != settings.Current.LbpRate) await settings.SetExchangeRateAsync(LbpRate, session.User.Id);
        await settings.GetAsync();
        if (CanAddSampleMenu && AddSampleMenu && await products.IsMenuEmptyAsync()) await products.AddSampleMenuAsync(choice.Venue);
        session.Features = await features.GetAsync(session.TillId);
        Close(true);
    });

    [RelayCommand]
    private void Later() => Close(false);
}
