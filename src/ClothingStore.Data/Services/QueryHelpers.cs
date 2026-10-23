using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Data.Services;

internal static class QueryHelpers
{
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
}
