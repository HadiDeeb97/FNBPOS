using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixCashOnNonCashSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sales fully paid by card/wallet/delivery/credit where the cash box still held an amount: that cash was
            // recorded as tendered and all of it as change, which showed on the receipt and threw the drawer count off.
            // No cash was taken on them (no cash payment rows), so the tendered and change figures are cleared.
            migrationBuilder.Sql("""
                UPDATE s SET CashTendered = 0, ChangeGiven = 0, CashTenderedLbp = 0, ChangeGivenLbp = 0
                FROM Sales s
                WHERE (s.CashTendered > 0 OR s.CashTenderedLbp > 0 OR s.ChangeGiven > 0 OR s.ChangeGivenLbp > 0)
                  AND NOT EXISTS (SELECT 1 FROM Payments p WHERE p.SaleId = s.Id AND p.Method IN (0, 5));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
