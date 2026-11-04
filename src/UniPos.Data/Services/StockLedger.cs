using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Localization;

namespace UniPos.Data.Services;

/// <summary>Every stock change goes through here so the movement history always reconciles.</summary>
internal static class StockLedger
{
    /// <summary>
    /// Moves stock of a counted item. An item made to order (<see cref="Product.TrackStock"/> off) has no stock: selling,
    /// refunding or voiding it moves nothing (null is returned), and receiving or adjusting it is refused.
    /// </summary>
    public static StockMovement? Apply(
        PosDbContext db,
        Product product,
        int change,
        StockMovementType type,
        int? userId,
        string? reference = null,
        string? notes = null,
        bool allowNegative = false)
    {
        if (!product.TrackStock)
        {
            if (type is StockMovementType.Sale or StockMovementType.Return or StockMovementType.Void or StockMovementType.SaleDeleted)
                return null;
            throw new BusinessRuleException(Loc.T("Err.StockNotTracked", product.Name));
        }

        var after = product.StockQuantity + change;
        // Only taking stock out can be refused: receiving, returns and voids must go through even while the count is
        // already below zero (after a sale with negative stock allowed), or that stock could never be put back.
        if (change < 0 && after < 0 && !allowNegative)
            throw new BusinessRuleException(Loc.T("Err.NotEnoughStock", product.Name, product.StockQuantity, -change));

        product.StockQuantity = after;
        var movement = new StockMovement
        {
            Product = product,
            ProductId = product.Id,
            Type = type,
            QuantityChange = change,
            QuantityAfter = after,
            UserId = userId,
            Reference = reference,
            Notes = notes,
            CreatedAt = DateTime.Now,
        };
        db.StockMovements.Add(movement);
        return movement;
    }
}
