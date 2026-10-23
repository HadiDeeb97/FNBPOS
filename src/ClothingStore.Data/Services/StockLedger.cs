using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;

namespace ClothingStore.Data.Services;

/// <summary>Every stock change goes through here so the movement history always reconciles.</summary>
internal static class StockLedger
{
    public static StockMovement Apply(
        PosDbContext db,
        ProductVariant variant,
        int change,
        StockMovementType type,
        int? userId,
        string? reference = null,
        string? notes = null,
        bool allowNegative = false)
    {
        var after = variant.StockQuantity + change;
        // Only taking stock out can be refused: receiving, returns and voids must go through even while the count is
        // already below zero (after a sale with negative stock allowed), or that stock could never be put back.
        if (change < 0 && after < 0 && !allowNegative)
            throw new BusinessRuleException(Loc.T("Err.NotEnoughStock", variant.Sku, variant.StockQuantity, -change));

        variant.StockQuantity = after;
        var movement = new StockMovement
        {
            ProductVariant = variant,
            ProductVariantId = variant.Id,
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
