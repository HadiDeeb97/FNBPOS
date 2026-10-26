using System.Globalization;
using System.Windows;
using ClothingStore.Core.Localization;
using ClothingStore.Data.OnlineReports;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels;

/// <summary>
/// Settings → Online reports: the shop's reports website. Shows its link and whether uploads are getting through, and
/// lets an admin switch it off, send now, or sign every phone out.
/// </summary>
public sealed partial class OnlineReportsViewModel(
    IDialogService dialogs, OnlineReportsService service, CloudflareDeployer cloudflare, SettingsService settings, LicenseManager license,
    Session session)
    : ViewModelBase(dialogs)
{
    private bool _watching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetUp), nameof(SiteUrl), nameof(IsEnabled), nameof(CanUpdateWebsite), nameof(StatusText), nameof(HasProblem))]
    public partial OnlineReportsConfig? Config { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasProblem), nameof(CanUpdateWebsite))]
    public partial OnlineReportsState State { get; private set; } = new();

    /// <summary>
    /// The website was set up by the POS and is older than this POS's copy (or says so): it can be updated with the
    /// Cloudflare token (remembered on the PC that set it up, otherwise asked for).
    /// </summary>
    public bool CanUpdateWebsite => Config?.Cloudflare is { } site &&
        (site.WorkerBuild != CloudflareDeployer.CurrentBuild || State.Error == nameof(OnlineReportsError.Outdated));

    /// <summary>The month's sales target as typed (empty for none); saved with <see cref="SaveTargetCommand"/>.</summary>
    [ObservableProperty]
    public partial string MonthlyTargetText { get; set; } = "";

    /// <summary>Google Search Console's verification code as pasted (the code or the whole meta tag).</summary>
    [ObservableProperty]
    public partial string GoogleCodeText { get; set; } = "";

    partial void OnConfigChanged(OnlineReportsConfig? value)
    {
        MonthlyTargetText = value?.MonthlyTarget is { } target ? target.ToString("0.##", CultureInfo.CurrentCulture) : "";
        GoogleCodeText = value?.GoogleVerification ?? "";
    }

    [RelayCommand]
    private async Task SaveTargetAsync()
    {
        if (Config is null) return;
        decimal? target = null;
        if (!string.IsNullOrWhiteSpace(MonthlyTargetText))
        {
            if (!decimal.TryParse(MonthlyTargetText, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) &&
                !decimal.TryParse(MonthlyTargetText, NumberStyles.Number, CultureInfo.InvariantCulture, out value) || value < 0 || value > 1_000_000_000)
            {
                Dialogs.Warning(Loc.T("Online.TargetInvalid"));
                return;
            }
            target = value == 0 ? null : Math.Round(value, 2);
        }
        var google = OnlineReportsService.NormalizeGoogleCode(GoogleCodeText);
        if (!string.IsNullOrWhiteSpace(GoogleCodeText) && google is null)
        {
            Dialogs.Warning(Loc.T("Online.GoogleInvalid"));
            return;
        }
        var config = Config with { MonthlyTarget = target, GoogleVerification = google };
        if (!await RunAsync(() => service.SaveConfigAsync(config))) return;
        Config = config;
        Dialogs.Toast(Loc.T("Online.TargetSaved"));
        await UploadNowAsync(); // on the website within a minute
    }

    public bool IsLicensed => OnlineReportsWorker.IsLicensed(license);
    public bool IsSetUp => Config is not null;
    public string SiteUrl => Config?.SiteUrl ?? "";

    /// <summary>On/off; saved at once (every till follows within 5 minutes).</summary>
    public bool IsEnabled
    {
        get => Config?.Enabled ?? false;
        set
        {
            if (Config is null || Config.Enabled == value) return;
            _ = SetEnabledAsync(value);
        }
    }

    public bool HasProblem => IsSetUp && (!IsLicensed || State.Error is not null || State.LastUploadUtc is null && Config is { Enabled: true });

    /// <summary>"Last upload 16:42 from TILL-1 ✓", or what is wrong.</summary>
    public string StatusText
    {
        get
        {
            if (Config is null) return "";
            if (!IsLicensed) return Loc.T("Online.NeedsLicense");
            if (!Config.Enabled) return Loc.T("Online.StatusOff");
            var since = State.FailingSinceUtc is { } f ? When(f) : "";
            return State.Error switch
            {
                nameof(OnlineReportsError.Offline) => Loc.T("Online.StatusOffline", since),
                nameof(OnlineReportsError.Rejected) => Loc.T("Online.StatusRejected"),
                nameof(OnlineReportsError.Limit) => Loc.T("Online.StatusPaused", State.PausedUntilUtc is { } until ? When(until) : "00:00 UTC"),
                nameof(OnlineReportsError.Outdated) => Loc.T("Online.StatusOutdated"),
                nameof(OnlineReportsError.Server) => Loc.T("Online.StatusServer", since, State.ErrorDetail ?? ""),
                _ when State.LastUploadUtc is { } last => Loc.T("Online.StatusOk", When(last), State.Till ?? "-") + "  ·  " +
                    Loc.T("Online.UploadsToday", State.UploadsToday, OnlineReportsService.MaxUploadsPerDay),
                _ => Loc.T("Online.StatusWaiting"),
            };
        }
    }

    private static string When(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("dd MMM HH:mm");
    }

    public async Task LoadAsync()
    {
        if (!_watching)
        {
            _watching = true;
            service.Synced += (_, _) => Application.Current?.Dispatcher.BeginInvoke(async () => await RefreshStateAsync());
        }
        try
        {
            Config = await service.GetConfigAsync();
            State = await service.GetStateAsync();
            OnPropertyChanged(nameof(IsLicensed));
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Online.LoadFailed"), ex);
        }
    }

    private async Task RefreshStateAsync()
    {
        try
        {
            State = await service.GetStateAsync();
        }
        catch (Exception)
        {
            // The next refresh or opening of the page shows it.
        }
    }

    private async Task SetEnabledAsync(bool value)
    {
        if (Config is null) return;
        var config = Config with { Enabled = value };
        if (await RunAsync(() => service.SaveConfigAsync(config)))
        {
            Config = config;
            if (value) await UploadNowAsync();
        }
        else
        {
            OnPropertyChanged(nameof(IsEnabled)); // put the switch back
        }
    }

    [RelayCommand]
    private async Task SetUpAsync()
    {
        if (!IsLicensed)
        {
            Dialogs.Warning(Loc.T("Online.NeedsLicense"));
            return;
        }
        var setup = new OnlineReportsSetupViewModel(Dialogs, service, cloudflare, settings.Current.StoreName, Config);
        if (!Dialogs.ShowDialog(setup)) return;
        await LoadAsync();
        await UploadNowAsync();
    }

    [RelayCommand]
    private async Task UpdateWebsiteAsync()
    {
        if (Config is not { Cloudflare: { } site } config) return;
        var remembered = CloudflareTokenStore.Load();
        var token = remembered ?? Dialogs.Prompt(Loc.T("Online.UpdateTitle"), Loc.T("Online.UpdateAskToken"));
        if (string.IsNullOrWhiteSpace(token)) return;
        var ok = await RunAsync(async () =>
        {
            try
            {
                var (updated, url) = await cloudflare.DeployAsync(token, config.UploadKey, settings.Current.StoreName, site);
                await service.SaveConfigAsync(config with { Cloudflare = updated, SiteUrl = url });
                if (remembered is null) CloudflareTokenStore.Save(token); // this PC updates it by itself from now on
            }
            catch (CloudflareException ex)
            {
                throw new Core.BusinessRuleException(Loc.T("Online.CloudflareFailed", Loc.T($"Online.Step.{ex.Step}"), ex.Message));
            }
        });
        if (!ok) return;
        Config = await service.GetConfigAsync();
        Dialogs.Toast(Loc.T("Online.Updated"));
        await UploadNowAsync();
    }

    [RelayCommand]
    private void OpenSite()
    {
        if (!IsSetUp) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SiteUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Online.OpenFailed"), ex);
        }
    }

    [RelayCommand]
    private void CopyLink()
    {
        if (!IsSetUp) return;
        try
        {
            Clipboard.SetText(SiteUrl);
            Dialogs.Toast(Loc.T("Online.LinkCopied"));
        }
        catch (Exception)
        {
            // Another program is holding the clipboard: the link is on screen to copy by hand.
        }
    }

    [RelayCommand]
    private async Task UploadNowAsync()
    {
        if (!IsLicensed || Config is not { Enabled: true }) return;
        SyncResult? result = null;
        // The first upload builds 90 days of history, so it runs off the UI thread.
        if (!await RunAsync(async () => result = await Task.Run(() => service.SyncAsync(session.TillId, session.TillName, force: true)))) return;
        State = await service.GetStateAsync();
        if (result?.Outcome == SyncOutcome.Sent) Dialogs.Toast(Loc.T("Online.Uploaded"));
        else if (result?.Outcome == SyncOutcome.Failed) Dialogs.Warning(StatusText);
    }

    [RelayCommand]
    private async Task SignOutAllAsync()
    {
        if (!IsSetUp || !Dialogs.Confirm(Loc.T("Online.SignOutAllConfirm"))) return;
        if (!await RunAsync(() => service.SignOutAllDevicesAsync())) return;
        Config = await service.GetConfigAsync();
        await UploadNowAsync();
        if (State.Error is null) Dialogs.Toast(Loc.T("Online.SignedOutAll"));
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (!IsSetUp || !Dialogs.Confirm(Loc.T("Online.RemoveConfirm"))) return;
        if (await RunAsync(() => service.RemoveConfigAsync()))
        {
            CloudflareTokenStore.Forget();
            Config = null;
            State = new OnlineReportsState();
        }
    }
}
