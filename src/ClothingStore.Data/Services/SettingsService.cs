using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Security;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

public class SettingsService(IDbContextFactory<PosDbContext> factory)
{
    private StoreSettings? _cached;

    /// <summary>Raised after settings are saved so open screens can refresh (tax rate, currency...).</summary>
    public event EventHandler? SettingsChanged;

    public StoreSettings Current => _cached ?? throw new InvalidOperationException("Settings not loaded yet.");

    public async Task<StoreSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        _cached = await db.Settings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct) ?? new StoreSettings();
        return _cached;
    }

    public async Task SaveAsync(StoreSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.StoreName)) throw new BusinessRuleException(Loc.T("Err.StoreNameRequired"));
        if (settings.TaxRate is < 0 or > 100) throw new BusinessRuleException(Loc.T("Err.TaxRateRange"));
        if (settings.MaxCashierDiscountPercent is < 0 or > 100) throw new BusinessRuleException(Loc.T("Err.DiscountLimitRange"));
        if (settings.LoyaltyPointsPerUnit < 0 || settings.LoyaltyPointValue < 0) throw new BusinessRuleException(Loc.T("Err.LoyaltyNegative"));
        if (settings.ReturnWindowDays < 0) throw new BusinessRuleException(Loc.T("Err.ReturnWindowNegative"));
        if (string.IsNullOrWhiteSpace(settings.ReceiptPrefix) || settings.ReceiptPrefix.Length > 5)
            throw new BusinessRuleException(Loc.T("Err.ReceiptPrefixLength"));
        settings.ReceiptWidth = Math.Clamp(settings.ReceiptWidth, 24, 80);
        settings.BackupFolder = QueryHelpers.Clean(settings.BackupFolder);
        settings.CustomerDisplayMessage = QueryHelpers.Clean(settings.CustomerDisplayMessage);
        if (settings.CustomerDisplayMessage?.Length > 200) throw new BusinessRuleException(Loc.T("Err.DisplayMessageLength"));
        settings.ReceiptLanguage = Loc.Normalize(settings.ReceiptLanguage);
        if (settings.AutoBackupIntervalHours is < 1 or > 168)
            throw new BusinessRuleException(Loc.T("Err.BackupInterval"));
        if (settings.LbpRounding is < 1 or > 100_000)
            throw new BusinessRuleException(Loc.T("Err.LbpRounding"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            settings.Id = 0;
            db.Settings.Add(settings);
        }
        else
        {
            // The rate has its own permission and history (SetExchangeRateAsync), so saving the settings
            // screen never puts back a rate someone changed while it was open.
            settings.Id = existing.Id;
            settings.LbpRate = existing.LbpRate;
            db.Entry(existing).CurrentValues.SetValues(settings);
        }
        await db.SaveChangesAsync(ct);
        _cached = settings;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Changes the LBP rate for every till and records who changed it.</summary>
    public async Task SetExchangeRateAsync(decimal rate, int userId, CancellationToken ct = default)
    {
        rate = Math.Round(rate, 2, MidpointRounding.AwayFromZero);
        if (rate is < 1 or > 100_000_000) throw new BusinessRuleException(Loc.T("Err.RateRange"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || !Permissions.Has(user.Role, Permission.ChangeExchangeRate))
            throw new BusinessRuleException(Loc.T("Err.RateNotAllowed"));

        var existing = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            existing = new StoreSettings();
            db.Settings.Add(existing);
        }
        if (existing.LbpRate == rate) return;

        db.ExchangeRateChanges.Add(new ExchangeRateChange { ChangedAt = DateTime.Now, OldRate = existing.LbpRate, NewRate = rate, UserId = userId });
        existing.LbpRate = rate;
        await db.SaveChangesAsync(ct);

        if (_cached is not null) _cached.LbpRate = rate;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<List<ExchangeRateChange>> GetRateHistoryAsync(int take = 20, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ExchangeRateChanges.AsNoTracking().Include(c => c.User)
            .OrderByDescending(c => c.ChangedAt).Take(take).ToListAsync(ct);
    }

    /// <summary>
    /// Picks up a rate or LBP setting changed on another till. Cheap (one row, three columns), so it can run on a timer.
    /// Returns true and raises <see cref="SettingsChanged"/> when something changed.
    /// </summary>
    public async Task<bool> RefreshCurrencyAsync(CancellationToken ct = default)
    {
        if (_cached is null) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        var latest = await db.Settings.AsNoTracking().OrderBy(s => s.Id)
            .Select(s => new { s.LbpEnabled, s.LbpRate, s.LbpRounding })
            .FirstOrDefaultAsync(ct);
        if (latest is null) return false;
        if (latest.LbpEnabled == _cached.LbpEnabled && latest.LbpRate == _cached.LbpRate && latest.LbpRounding == _cached.LbpRounding)
            return false;

        _cached.LbpEnabled = latest.LbpEnabled;
        _cached.LbpRate = latest.LbpRate;
        _cached.LbpRounding = latest.LbpRounding;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
