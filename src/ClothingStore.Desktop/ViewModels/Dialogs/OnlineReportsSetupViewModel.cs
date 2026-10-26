using ClothingStore.Core.Localization;
using ClothingStore.Data.OnlineReports;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

/// <summary>
/// Sets up the shop's reports website. Normally with a Cloudflare API token: the POS creates and publishes everything on
/// the shop's own free account. Or, for a website made by hand, with its address and upload key.
/// </summary>
public sealed partial class OnlineReportsSetupViewModel(
    IDialogService dialogs, OnlineReportsService service, CloudflareDeployer cloudflare, string storeName, OnlineReportsConfig? existing)
    : DialogViewModelBase(dialogs)
{
    public const string TokensPage = "https://dash.cloudflare.com/profile/api-tokens";
    public const string SignUpPage = "https://dash.cloudflare.com/sign-up";

    /// <summary>A new website can take a minute or two to answer at its new address.</summary>
    private static readonly TimeSpan WaitForSite = TimeSpan.FromSeconds(60);

    public override string Title => Loc.T("Online.SetupTitle");

    [ObservableProperty]
    public partial string Token { get; set; } = "";

    [ObservableProperty]
    public partial bool RememberToken { get; set; } = true;

    /// <summary>The other way: a website already made (by hand, or on another PC).</summary>
    [ObservableProperty]
    public partial bool UseExisting { get; set; } = existing is not null && existing.Cloudflare is null;

    [ObservableProperty]
    public partial string SiteUrl { get; set; } = existing?.SiteUrl ?? "";

    [ObservableProperty]
    public partial string UploadKey { get; set; } = existing?.UploadKey ?? "";

    /// <summary>What it is doing now ("Creating the database…").</summary>
    [ObservableProperty]
    public partial string Progress { get; set; } = "";

    [RelayCommand]
    private void OpenTokensPage() => OpenInBrowser(TokensPage);

    [RelayCommand]
    private void OpenSignUpPage() => OpenInBrowser(SignUpPage);

    private static void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser: the address is in the guide.
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    [RelayCommand]
    private async Task SetUpAsync()
    {
        if (UseExisting) await ConnectExistingAsync();
        else await DeployAsync();
    }

    private async Task DeployAsync()
    {
        var token = Token.Trim();
        if (token.Length < 20)
        {
            Dialogs.Warning(Loc.T("Online.TokenRequired"));
            return;
        }
        // Setting up again keeps the same key, so other tills and the website keep agreeing.
        var key = existing is { UploadKey.Length: > 0 } ? existing.UploadKey : OnlineReportsService.NewUploadKey();
        string? url = null;
        CloudflareSite? site = null;
        var steps = new Progress<string>(step => Progress = Loc.T($"Online.Step.{step}"));
        var ok = await RunAsync(async () =>
        {
            try
            {
                (site, url) = await cloudflare.DeployAsync(token, key, storeName, existing?.Cloudflare, steps);
            }
            catch (CloudflareException ex)
            {
                throw new Core.BusinessRuleException(Loc.T("Online.CloudflareFailed", Loc.T($"Online.Step.{ex.Step}"), ex.Message));
            }
            await service.SaveConfigAsync(new OnlineReportsConfig
            {
                SiteUrl = url, UploadKey = key, Cloudflare = site, Enabled = true, SignOutAll = existing?.SignOutAll ?? 0,
            });
            if (RememberToken) CloudflareTokenStore.Save(token);
            else CloudflareTokenStore.Forget();

            Progress = Loc.T("Online.Step.wait");
            var started = DateTime.UtcNow;
            while (!(await service.CheckSiteAsync(url)).Ok && DateTime.UtcNow - started < WaitForSite)
                await Task.Delay(TimeSpan.FromSeconds(5));
        });
        Progress = "";
        if (!ok || url is null) return;
        if (!(await service.CheckSiteAsync(url)).Ok) Dialogs.Info(Loc.T("Online.SiteNotYet", url), Title);
        Close(true);
    }

    private async Task ConnectExistingAsync()
    {
        if (OnlineReportsService.NormalizeSiteUrl(SiteUrl) is not { } url)
        {
            Dialogs.Warning(Loc.T("Online.BadAddress"));
            return;
        }
        var key = UploadKey.Trim();
        if (key.Length < 16)
        {
            Dialogs.Warning(Loc.T("Online.KeyRequired"));
            return;
        }
        var ok = await RunAsync(async () =>
        {
            Progress = Loc.T("Online.Step.check");
            var (answers, version) = await service.CheckSiteAsync(url);
            if (!answers)
                throw new Core.BusinessRuleException(version is null ? Loc.T("Online.SiteNoAnswer", url) : Loc.T("Online.SiteOtherVersion", version));
            await service.SaveConfigAsync(new OnlineReportsConfig
            {
                SiteUrl = url, UploadKey = key, Enabled = true, SignOutAll = existing?.SignOutAll ?? 0,
                // The same website set up earlier by this POS: keep what it knows about it, to update it later.
                Cloudflare = existing?.SiteUrl == url ? existing.Cloudflare : null,
            });
        });
        Progress = "";
        if (ok) Close(true);
    }
}
