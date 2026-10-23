using System.Globalization;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Pricing;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

/// <summary>Quick change of the LBP rate (top bar, settings). Every change is logged with who made it.</summary>
public sealed partial class ExchangeRateViewModel(IDialogService dialogs, SettingsService settings, Session session) : DialogViewModelBase(dialogs)
{
    public override string Title => Loc.T("Rate.Title");
    public decimal CurrentRate { get; } = settings.Current.LbpRate;
    public string CurrentRateText => Loc.T("Rate.Short", CurrentRate.ToString("N0"));
    public bool CanChange => session.Can(Core.Security.Permission.ChangeExchangeRate);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    public partial string NewRateText { get; set; } = settings.Current.LbpRate.ToString("#,0.##", CultureInfo.InvariantCulture);

    [ObservableProperty]
    public partial List<ExchangeRateChange> History { get; set; } = [];

    /// <summary>A few common prices at the new rate, so a typo (one zero too many) is obvious.</summary>
    public string Preview => TryParse(out var rate) && rate > 0
        ? string.Join("     ", new[] { 1m, 10m, 50m }.Select(usd => $"${usd:0} = {Lbp.Format(usd * rate)}"))
        : "";

    public override async Task OnOpenedAsync()
    {
        try
        {
            History = await settings.GetRateHistoryAsync(15);
        }
        catch
        {
            History = [];
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!TryParse(out var rate))
        {
            Dialogs.Warning(Loc.T("Err.RateRange"));
            return;
        }
        if (rate == CurrentRate)
        {
            Close(false);
            return;
        }

        // Big jumps are usually a typo; confirm before every till starts charging it.
        var change = CurrentRate > 0 ? Math.Abs(rate - CurrentRate) / CurrentRate : 0;
        if (change > 0.2m && !Dialogs.Confirm(Loc.T("Rate.BigChangeConfirm", CurrentRate.ToString("N0"), rate.ToString("N0"))))
            return;

        if (await RunAsync(() => settings.SetExchangeRateAsync(rate, session.User.Id))) Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private bool TryParse(out decimal rate) =>
        decimal.TryParse(NewRateText, NumberStyles.Number, CultureInfo.InvariantCulture, out rate) ||
        decimal.TryParse(NewRateText, NumberStyles.Number, CultureInfo.CurrentCulture, out rate);
}
