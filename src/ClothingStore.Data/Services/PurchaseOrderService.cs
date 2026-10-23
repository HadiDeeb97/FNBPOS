using ClothingStore.Core;
using ClothingStore.Core.Entities;
using Microsoft.EntityFrameworkCore;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

public class PurchaseOrderService(IDbContextFactory<PosDbContext> factory)
{
    public async Task<List<PurchaseOrder>> GetAllAsync(PurchaseOrderStatus? status = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Lines)
            .AsSplitQuery()
            .AsQueryable();
        if (status is not null) query = query.Where(p => p.Status == status);
        return await query.OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
    }

    public async Task<PurchaseOrder?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v!.Product)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    /// <summary>Creates or updates a draft/ordered PO. Lines are replaced wholesale.</summary>
    public async Task<PurchaseOrder> SaveAsync(PurchaseOrder order, int userId, CancellationToken ct = default)
    {
        if (order.SupplierId == 0) throw new BusinessRuleException(Loc.T("Err.ChooseSupplier"));
        var lines = order.Lines.Where(l => l.QuantityOrdered > 0).ToList();
        if (lines.Count == 0) throw new BusinessRuleException(Loc.T("Err.OrderNeedsItem"));
        if (lines.Any(l => l.UnitCost < 0)) throw new BusinessRuleException(Loc.T("Err.UnitCostNegative"));
        if (lines.GroupBy(l => l.ProductVariantId).Any(g => g.Count() > 1))
            throw new BusinessRuleException(Loc.T("Err.ItemOnceOnOrder"));

        await using var db = await factory.CreateDbContextAsync(ct);
        PurchaseOrder entity;
        if (order.Id == 0)
        {
            var now = DateTime.Now;
            var stem = QueryHelpers.DayStem("PO", now);
            var last = await db.PurchaseOrders.Where(p => p.OrderNumber.StartsWith(stem))
                .OrderByDescending(p => p.OrderNumber).Select(p => p.OrderNumber).FirstOrDefaultAsync(ct);
            entity = new PurchaseOrder
            {
                OrderNumber = QueryHelpers.NextNumber("PO", now, last),
                CreatedAt = now,
                CreatedByUserId = userId,
                Status = PurchaseOrderStatus.Draft,
            };
            db.PurchaseOrders.Add(entity);
        }
        else
        {
            entity = await db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == order.Id, ct)
                     ?? throw new BusinessRuleException(Loc.T("Err.PoNotFound"));
            if (entity.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered))
                throw new BusinessRuleException(Loc.T("Err.PoNotEditable"));
            db.PurchaseOrderLines.RemoveRange(entity.Lines);
            entity.Lines.Clear();
        }

        entity.SupplierId = order.SupplierId;
        entity.ExpectedDate = order.ExpectedDate;
        entity.Notes = QueryHelpers.Clean(order.Notes);
        foreach (var l in lines)
        {
            entity.Lines.Add(new PurchaseOrderLine
            {
                ProductVariantId = l.ProductVariantId,
                QuantityOrdered = l.QuantityOrdered,
                UnitCost = Money.Round(l.UnitCost),
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.PoChangedMeanwhile"));
        }
        catch (DbUpdateException ex) when (QueryHelpers.IsUniqueViolation(ex))
        {
            // Another PC created an order with the same number a moment earlier; saving again takes the next one.
            throw new BusinessRuleException(Loc.T("Err.CodeTakenMeanwhile"));
        }
        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task MarkOrderedAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var po = await db.PurchaseOrders.FindAsync([id], ct) ?? throw new BusinessRuleException(Loc.T("Err.PoNotFound"));
        if (po.Status != PurchaseOrderStatus.Draft) throw new BusinessRuleException(Loc.T("Err.PoOnlyDraftOrdered"));
        po.Status = PurchaseOrderStatus.Ordered;
        po.OrderedAt = DateTime.Now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.PoChangedMeanwhile"));
        }
    }

    /// <summary>
    /// Books received quantities into stock. <paramref name="receivedByLineId"/> maps PO line id to units received now.
    /// When <paramref name="updateCosts"/> is set, the variant cost is updated to the PO unit cost.
    /// </summary>
    /// <summary>
    /// Books received quantities into stock. More than still outstanding on a line is refused unless
    /// <paramref name="allowOverReceipt"/> (the supplier really sent extra): it catches typos, and a second PC receiving
    /// the same delivery from a screen opened before the first PC saved.
    /// </summary>
    public async Task<PurchaseOrder> ReceiveAsync(int id, IReadOnlyDictionary<int, int> receivedByLineId, int userId, bool updateCosts = true,
        bool allowOverReceipt = false, CancellationToken ct = default)
    {
        if (receivedByLineId.Values.Any(q => q < 0)) throw new BusinessRuleException(Loc.T("Err.ReceivedNegative"));
        if (receivedByLineId.Values.All(q => q == 0)) throw new BusinessRuleException(Loc.T("Err.EnterReceived"));

        await using var db = await factory.CreateDbContextAsync(ct);
        var po = await db.PurchaseOrders
            .Include(p => p.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v!.Product)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new BusinessRuleException(Loc.T("Err.PoNotFound"));

        if (po.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Received)
            throw new BusinessRuleException(Loc.T("Err.PoClosed"));

        foreach (var (lineId, qty) in receivedByLineId.Where(kv => kv.Value > 0))
        {
            var line = po.Lines.FirstOrDefault(l => l.Id == lineId) ?? throw new BusinessRuleException(Loc.T("Err.PoLineNotFound"));
            if (qty > line.QuantityOutstanding && !allowOverReceipt)
                throw new BusinessRuleException(Loc.T("Err.ReceiveMoreThanOutstanding",
                    $"{line.ProductVariant!.Product!.Name} {line.ProductVariant.Description}".Trim(), line.QuantityOutstanding, qty));
            line.QuantityReceived += qty;
            var variant = line.ProductVariant!;
            StockLedger.Apply(db, variant, qty, StockMovementType.PurchaseReceipt, userId, po.OrderNumber);
            if (updateCosts && variant.EffectiveCost != line.UnitCost)
                variant.CostOverride = line.UnitCost == variant.Product!.Cost ? null : line.UnitCost;
        }

        po.Status = po.Lines.All(l => l.QuantityReceived >= l.QuantityOrdered)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
        if (po.Status == PurchaseOrderStatus.Received) po.ReceivedAt = DateTime.Now;
        po.OrderedAt ??= DateTime.Now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.StockChangedReceiving"));
        }
        return (await GetAsync(id, ct))!;
    }

    /// <summary>Cancels an order. Partially received orders are closed (received stock stays).</summary>
    public async Task CancelAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var po = await db.PurchaseOrders.FindAsync([id], ct) ?? throw new BusinessRuleException(Loc.T("Err.PoNotFound"));
        if (po.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
            throw new BusinessRuleException(Loc.T("Err.PoClosed"));
        po.Status = po.Status == PurchaseOrderStatus.PartiallyReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.Cancelled;
        if (po.Status == PurchaseOrderStatus.Received) po.ReceivedAt = DateTime.Now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(Loc.T("Err.PoChangedMeanwhile"));
        }
    }

    public async Task DeleteDraftAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var po = await db.PurchaseOrders.FindAsync([id], ct) ?? throw new BusinessRuleException(Loc.T("Err.PoNotFound"));
        if (po.Status != PurchaseOrderStatus.Draft) throw new BusinessRuleException(Loc.T("Err.PoOnlyDraftDelete"));
        db.PurchaseOrders.Remove(po);
        await db.SaveChangesAsync(ct);
    }
}
