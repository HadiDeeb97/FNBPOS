using UniPos.Core.Receipts;
using UniPos.Data.Services;
using UniPos.Desktop.Infrastructure;

namespace UniPos.Desktop.Services;

/// <summary>
/// Prints kitchen tickets at their station's printer (this PC's own choice for the station, else the store's) and
/// records each result, so a ticket that didn't print waits on the Kitchen page instead of being lost.
/// </summary>
public sealed class KitchenPrinter(KitchenService kitchen, SettingsService settings)
{
    /// <summary>Prints the tickets of a sale just made; returns how many didn't print.</summary>
    public async Task<int> PrintSaleAsync(int saleId) =>
        await PrintAsync((await kitchen.GetForSaleAsync(saleId)).Where(r => !r.Ticket.IsPrinted));

    /// <summary>Prints the given tickets (again, for ones printed before); returns how many didn't print.</summary>
    public async Task<int> PrintAsync(IEnumerable<TicketRow> rows)
    {
        var failed = 0;
        foreach (var row in rows)
        {
            var printer = LocalPreferences.Current.StationPrinters.GetValueOrDefault(row.Ticket.StationId) ?? row.StationPrinter;
            var lines = KitchenTicketFormatter.Format(row.Ticket, row.Station, row.ReceiptNumber, settings.Current.ReceiptWidth, settings.Current.ReceiptLanguage);
            var error = PrintService.PrintTo(lines, printer, $"{row.Station} {row.ReceiptNumber}");
            try
            {
                await kitchen.RecordPrintAsync(row.Ticket.Id, error);
            }
            catch
            {
                // It printed (or not) all the same; the Kitchen page shows the last result recorded.
            }
            if (error is not null) failed++;
        }
        return failed;
    }

    /// <summary>The ticket's text as it prints, for the Kitchen page's preview.</summary>
    public IReadOnlyList<string> Preview(TicketRow row) =>
        KitchenTicketFormatter.Format(row.Ticket, row.Station, row.ReceiptNumber, settings.Current.ReceiptWidth, settings.Current.ReceiptLanguage);
}
