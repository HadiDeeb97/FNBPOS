using ClothingStore.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Data.Services;

/// <summary>Another till saved at the same moment: the same receipt number, or the same stock or balance changed.</summary>
internal sealed class TillConflictException(string message) : Exception(message);

internal static class QueryHelpers
{
    /// <summary>
    /// Runs <paramref name="operation"/> again from the start, with fresh numbers, stock and balances, when another till
    /// saved at the same moment (nothing of the failed try was saved, and every rule is checked again). Only after a few
    /// tries is the cashier told.
    /// </summary>
    public static async Task<T> RetryOnTillConflictAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (TillConflictException ex)
            {
                if (attempt == 8) throw new BusinessRuleException(ex.Message);
                await Task.Delay(Random.Shared.Next(5, 40) * attempt, ct);
            }
        }
    }

    /// <summary>True when SQL Server rejected the save because of a unique index or constraint.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Returns the next sequential document number for the day, e.g. R20261005-0007.
    /// <paramref name="existingMax"/> is the highest number issued today (or null).
    /// </summary>
    public static string NextNumber(string prefix, DateTime date, string? existingMax)
    {
        var stem = $"{prefix}{date:yyyyMMdd}-";
        var next = 1;
        if (existingMax is not null && existingMax.StartsWith(stem, StringComparison.Ordinal)
            && int.TryParse(existingMax[stem.Length..], out var last))
            next = last + 1;
        return stem + next.ToString("D4");
    }

    public static string DayStem(string prefix, DateTime date) => $"{prefix}{date:yyyyMMdd}-";

    /// <summary>The highest of the day's numbers: longest first, as "-10000" sorts before "-9999" as text.</summary>
    public static IQueryable<string> Highest(IQueryable<string> numbers) =>
        numbers.OrderByDescending(n => n.Length).ThenByDescending(n => n);
}
