using System.ComponentModel;
using Microsoft.Data.SqlClient;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>Database errors that mean "busy or briefly unreachable, try again" rather than a real problem.</summary>
public static class DbErrors
{
    // Timeout, network/transport errors, deadlock victim, database starting or recovering.
    private static readonly HashSet<int> TransientNumbers = [-2, -1, 2, 53, 64, 121, 233, 1205, 4060, 10053, 10054, 10060, 10928, 10929, 40197, 40501, 40613, 49918, 49919, 49920];

    public static bool IsTransient(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case SqlException sql when sql.Errors.Cast<SqlError>().Any(err => TransientNumbers.Contains(err.Number)) || TransientNumbers.Contains(sql.Number):
                case TimeoutException:
                case Win32Exception { NativeErrorCode: 258 }: // "The wait operation timed out"
                case InvalidOperationException when e.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
                                                    && e.Message.Contains("closed", StringComparison.OrdinalIgnoreCase):
                    return true;
            }
        }
        return false;
    }
}
