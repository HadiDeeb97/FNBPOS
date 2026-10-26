using ClothingStore.Core.Licensing;
using ClothingStore.Data.OnlineReports;
using ClothingStore.Desktop.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClothingStore.Desktop.Services;

/// <summary>
/// Sends the shop's reports to its website every 5 minutes while the POS is open. Every till runs this; one of them at a
/// time actually sends (see <see cref="OnlineReportsService"/>). Nothing happens until an admin sets it up in Settings.
/// </summary>
public sealed class OnlineReportsWorker(
    OnlineReportsService service, OnlineReportsBuilder builder, CloudflareDeployer cloudflare, LicenseManager license, Session session,
    ILogger<OnlineReportsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan UpdateRetry = TimeSpan.FromHours(6);
    private DateTime _lastUpdateTry = DateTime.MinValue;

    /// <summary>Online reports are part of a licensed POS (a developer build without licensing counts as licensed).</summary>
    public static bool IsLicensed(LicenseManager license) =>
        license.Status.State is LicenseState.Valid or LicenseState.ExpiringSoon or LicenseState.NotConfigured;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // ConfigureAwait(false): keep this loop off the UI thread.
            await Task.Delay(FirstRun, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(OnlineReportsService.Interval);
            do
            {
                if (!IsLicensed(license)) continue;
                try
                {
                    await UpdateWebsiteIfNeededAsync(stoppingToken).ConfigureAwait(false);
                    var result = await service.SyncAsync(session.TillId, session.TillName, ct: stoppingToken).ConfigureAwait(false);
                    if (result.Outcome == SyncOutcome.Failed) logger.LogWarning("Online reports upload failed: {Error} {Detail}", result.Error, result.Detail);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // The database was busy or a report failed to build: try again on the next tick.
                    logger.LogWarning(ex, "Online reports upload failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // App is closing.
        }
    }

    /// <summary>
    /// After a POS update that changed the website, the PC that set it up (it has the Cloudflare token) publishes the new
    /// version, so the website keeps taking this POS's uploads. Other PCs show "Update website" in Settings.
    /// </summary>
    private async Task UpdateWebsiteIfNeededAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastUpdateTry < UpdateRetry) return;
        var config = await service.GetConfigAsync(ct).ConfigureAwait(false);
        if (config is not { Enabled: true, Cloudflare: { } site } || site.WorkerBuild == CloudflareDeployer.CurrentBuild) return;
        if (CloudflareTokenStore.Load() is not { } token) return;
        _lastUpdateTry = DateTime.UtcNow;
        try
        {
            var store = (await builder.StoreAsync(ct).ConfigureAwait(false)).Name;
            var (updated, url) = await cloudflare.DeployAsync(token, config.UploadKey, store, site, ct: ct).ConfigureAwait(false);
            // Saved over the latest settings, in case an admin changed something meanwhile.
            if (await service.GetConfigAsync(ct).ConfigureAwait(false) is { } latest)
                await service.SaveConfigAsync(latest with { Cloudflare = updated, SiteUrl = url }, ct).ConfigureAwait(false);
            logger.LogInformation("Online reports website updated to {Build}", updated.WorkerBuild);
        }
        catch (CloudflareException ex)
        {
            logger.LogWarning("Online reports website update failed at {Step}: {Message}", ex.Step, ex.Message);
        }
    }
}
