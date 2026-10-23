using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOnlineOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Orders confirmed on the old screen but never completed still had their items taken out of stock.
            // Put that stock back (with a movement, so the ledger still adds up) before the tables go.
            migrationBuilder.Sql("""
                DECLARE @held TABLE (VariantId int NOT NULL, Qty int NOT NULL, OrderNumber nvarchar(32) NOT NULL, UserId int NOT NULL);

                INSERT @held (VariantId, Qty, OrderNumber, UserId)
                SELECT l.ProductVariantId, SUM(l.Quantity), o.OrderNumber, o.CreatedByUserId
                FROM OnlineOrderLines l JOIN OnlineOrders o ON o.Id = l.OnlineOrderId
                WHERE o.StockHeld = 1
                GROUP BY l.ProductVariantId, o.OrderNumber, o.CreatedByUserId;

                UPDATE v SET StockQuantity = v.StockQuantity + h.Qty, Version = v.Version + 1
                FROM ProductVariants v JOIN (SELECT VariantId, SUM(Qty) AS Qty FROM @held GROUP BY VariantId) h ON h.VariantId = v.Id;

                INSERT StockMovements (ProductVariantId, Type, QuantityChange, QuantityAfter, UserId, Reference, Notes, CreatedAt)
                SELECT h.VariantId, 9, h.Qty,
                       v.StockQuantity - ISNULL(SUM(h.Qty) OVER (PARTITION BY h.VariantId ORDER BY h.OrderNumber
                                                                 ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING), 0),
                       h.UserId, h.OrderNumber, N'Online orders screen removed: held stock returned', SYSDATETIME()
                FROM @held h JOIN ProductVariants v ON v.Id = h.VariantId;
                """);

            migrationBuilder.DropTable(
                name: "OnlineOrderLines");

            migrationBuilder.DropTable(
                name: "OnlineOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnlineOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    SaleId = table.Column<int>(type: "int", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Courier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DeliveryFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountType = table.Column<int>(type: "int", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Handle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemsTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OrderNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ShippedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StockHeld = table.Column<bool>(type: "bit", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OnlineOrders_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnlineOrders_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OnlineOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OnlineOrderId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VariantDescription = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineOrderLines_OnlineOrders_OnlineOrderId",
                        column: x => x.OnlineOrderId,
                        principalTable: "OnlineOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OnlineOrderLines_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrderLines_OnlineOrderId",
                table: "OnlineOrderLines",
                column: "OnlineOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrderLines_ProductVariantId",
                table: "OnlineOrderLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_CreatedByUserId",
                table: "OnlineOrders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_CustomerId",
                table: "OnlineOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_OrderNumber",
                table: "OnlineOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_SaleId",
                table: "OnlineOrders",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_Status_CreatedAt",
                table: "OnlineOrders",
                columns: new[] { "Status", "CreatedAt" });
        }
    }
}
