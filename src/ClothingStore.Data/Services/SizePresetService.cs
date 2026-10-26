using System.Text.Encodings.Web;
using System.Text.Json;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Data.Services;

/// <summary>A size button the store made itself on the new-product screen ("Abaya 52–60": "52, 54, 56, 58, 60").</summary>
public sealed record CustomSizePreset(string Name, string Sizes);

/// <summary>The store's own size buttons, shared by every till (kept as one small value in the database).</summary>
public class SizePresetService(IDbContextFactory<PosDbContext> factory)
{
    public const string StateKey = "products.sizePresets";
    public const int MaxNameLength = 30;
    public const int MaxSizesLength = 200;
    public const int MaxPresets = 20;

    /// <summary>Room the database keeps for all of them together.</summary>
    private const int MaxStoredLength = 2000;

    /// <summary>Arabic names stored as they are, not as \uXXXX escapes (six characters each).</summary>
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Tries again when another till changed the list at the same moment.</summary>
    private const int Attempts = 3;

    public async Task<List<CustomSizePreset>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var json = await db.AppState.AsNoTracking().Where(s => s.Key == StateKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return Parse(json);
    }

    /// <summary>Adds a button and returns the store's buttons as they are now.</summary>
    public async Task<List<CustomSizePreset>> AddAsync(string? name, string? sizes, CancellationToken ct = default)
    {
        name = name?.Trim() ?? "";
        sizes = Normalize(sizes);
        if (name.Length == 0) throw new BusinessRuleException(Loc.T("Err.PresetNameRequired"));
        if (name.Length > MaxNameLength) throw new BusinessRuleException(Loc.T("Err.PresetNameLength", MaxNameLength));
        if (sizes.Length == 0) throw new BusinessRuleException(Loc.T("Err.PresetSizesRequired"));
        if (sizes.Length > MaxSizesLength) throw new BusinessRuleException(Loc.T("Err.PresetSizesLength", MaxSizesLength));

        return await ChangeAsync(presets =>
        {
            if (presets.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                throw new BusinessRuleException(Loc.T("Err.PresetExists", name));
            if (presets.Count >= MaxPresets) throw new BusinessRuleException(Loc.T("Err.PresetTooMany", MaxPresets));
            presets.Add(new CustomSizePreset(name, sizes));
            if (JsonSerializer.Serialize(presets, Json).Length > MaxStoredLength)
                throw new BusinessRuleException(Loc.T("Err.PresetTooMany", presets.Count - 1));
            return true;
        }, ct);
    }

    /// <summary>Removes a button (by name) and returns the store's buttons as they are now.</summary>
    public Task<List<CustomSizePreset>> RemoveAsync(string name, CancellationToken ct = default) =>
        ChangeAsync(presets => presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)) > 0, ct);

    /// <summary>
    /// Reads the list, lets <paramref name="change"/> edit it (false: nothing to save) and writes it back only if no other
    /// till changed it in between; otherwise starts again from what that till saved, so neither change is lost.
    /// </summary>
    private async Task<List<CustomSizePreset>> ChangeAsync(Func<List<CustomSizePreset>, bool> change, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var old = await db.AppState.AsNoTracking().Where(s => s.Key == StateKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
            var presets = Parse(old);
            if (!change(presets)) return presets;
            var json = JsonSerializer.Serialize(presets, Json);
            try
            {
                if (old is null)
                {
                    db.AppState.Add(new AppState { Key = StateKey, Value = json });
                    await db.SaveChangesAsync(ct); // fails if another till created it meanwhile
                    return presets;
                }
                var saved = await db.AppState.Where(s => s.Key == StateKey && s.Value == old)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, json), ct);
                if (saved == 1) return presets;
            }
            catch (DbUpdateException) when (attempt < Attempts)
            {
            }
            if (attempt >= Attempts) throw new BusinessRuleException(Loc.T("Err.PresetBusy"));
        }
    }

    /// <summary>"s,m ; L" → "s, m, L": the same list the Generate box takes, tidied.</summary>
    public static string Normalize(string? sizes) =>
        string.Join(", ", (sizes ?? "").Split([',', '،', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static List<CustomSizePreset> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<CustomSizePreset>>(json)?
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Sizes))
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return []; // a damaged value never stops the product screen from opening
        }
    }
}
