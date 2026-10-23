using ClothingStore.Data.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClothingStore.Desktop.Services;

/// <summary>
/// Checks every few minutes whether the scheduled backup is due. Every till runs this; the backup log in the
/// shared database and the server-side lock make sure only one of them actually backs up.
/// </summary>
public sealed class AutoBackupWorker(BackupService backups, ILogger<AutoBackupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // ConfigureAwait(false): keep this loop off the UI thread.
            await Task.Delay(FirstCheck, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(CheckEvery);
            do
            {
                try
                {
                    if (await backups.RunIfDueAsync(stoppingToken).ConfigureAwait(false) is { } record)
                        logger.LogInformation("Scheduled backup written to {Path}", record.FilePath);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Already recorded in the backup log, which Settings shows; try again on the next tick. (A timeout
                    // from SQL Server can surface as a cancellation too; only the app closing stops the loop.)
                    logger.LogWarning(ex, "Scheduled backup failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // App is closing.
        }
    }
}
