using UniPos.Core;
using UniPos.Core.Entities;
using Microsoft.EntityFrameworkCore;
using UniPos.Core.Localization;

namespace UniPos.Data.Services;

/// <summary>A ticket for the Kitchen page, with what it needs to print again.</summary>
public sealed record TicketRow(KitchenTicket Ticket, string Station, string? StationPrinter, string ReceiptNumber);

/// <summary>
/// Stations (kitchen, bar, drinks) and the tickets orders print at them. Tickets are saved with the sale; the till that
/// took the order prints them and records here whether that worked, so one that didn't is listed until it does.
/// </summary>
public class KitchenService(IDbContextFactory<PosDbContext> factory)
{
    public async Task<List<Station>> GetStationsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Stations.AsNoTracking().Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct);
    }

    public async Task<Station> SaveStationAsync(Station station, CancellationToken ct = default)
    {
        var name = station.Name.Trim();
        if (name.Length == 0) throw new BusinessRuleException(Loc.T("Err.StationNameRequired"));
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Stations.AnyAsync(s => s.Id != station.Id && s.Name == name, ct))
            throw new BusinessRuleException(Loc.T("Err.StationNameTaken", name));

        Station entity;
        if (station.Id == 0)
        {
            entity = new Station { SortOrder = await db.Stations.CountAsync(ct) };
            db.Stations.Add(entity);
        }
        else
        {
            entity = await db.Stations.FindAsync([station.Id], ct) ?? throw new BusinessRuleException(Loc.T("Err.StationNotFound"));
        }
        entity.Name = name;
        entity.PrinterName = QueryHelpers.Clean(station.PrinterName);
        entity.IsActive = station.IsActive;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>Deletes a station that never had a ticket; otherwise switches it off. Its items stop printing either way.</summary>
    public async Task<bool> DeleteStationAsync(int stationId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var station = await db.Stations.FindAsync([stationId], ct);
        if (station is null) return false;
        if (await db.KitchenTickets.AnyAsync(t => t.StationId == stationId, ct))
        {
            station.IsActive = false;
            await db.SaveChangesAsync(ct);
            return false;
        }
        db.Stations.Remove(station); // its items keep no station (set null)
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The tickets of a sale (to print them after it is saved, or again from sales history).</summary>
    public Task<List<TicketRow>> GetForSaleAsync(int saleId, CancellationToken ct = default) =>
        QueryAsync(q => q.Where(t => t.SaleId == saleId), ct);

    /// <summary>Tickets made between two times, the ones not printed first; <paramref name="notPrintedOnly"/> for just those.</summary>
    public Task<List<TicketRow>> GetAsync(DateTime from, DateTime to, bool notPrintedOnly = false, CancellationToken ct = default) =>
        QueryAsync(q => q.Where(t => t.CreatedAt >= from && t.CreatedAt < to && (!notPrintedOnly || t.PrintedAt == null)), ct);

    private async Task<List<TicketRow>> QueryAsync(Func<IQueryable<KitchenTicket>, IQueryable<KitchenTicket>> filter, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var tickets = await filter(db.KitchenTickets.AsNoTracking())
            .Include(t => t.Lines.OrderBy(l => l.Id))
            .Include(t => t.Station)
            .OrderBy(t => t.PrintedAt == null ? 0 : 1).ThenByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Take(500)
            .ToListAsync(ct);
        var saleIds = tickets.Select(t => t.SaleId).Distinct().ToList();
        var receipts = await db.Sales.AsNoTracking().Where(s => saleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.ReceiptNumber, ct);
        return tickets.Select(t => new TicketRow(t, t.Station?.Name ?? "", t.Station?.PrinterName, receipts.GetValueOrDefault(t.SaleId) ?? "")).ToList();
    }

    /// <summary>Tickets still waiting to print since the start of today (the number on the Kitchen menu item).</summary>
    public async Task<int> CountNotPrintedAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = DateTime.Today;
        return await db.KitchenTickets.CountAsync(t => t.PrintedAt == null && t.CreatedAt >= today, ct);
    }

    /// <summary>Records a print: when it printed, or why it didn't (printed again later from the Kitchen page).</summary>
    public async Task RecordPrintAsync(int ticketId, string? error, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ticket = await db.KitchenTickets.FindAsync([ticketId], ct) ?? throw new BusinessRuleException(Loc.T("Err.TicketNotFound"));
        if (error is null)
        {
            ticket.PrintedAt = DateTime.Now;
            ticket.PrintCount++;
            ticket.PrintError = null;
        }
        else
        {
            ticket.PrintError = error.Length > 500 ? error[..500] : error;
        }
        await db.SaveChangesAsync(ct);
    }
}
