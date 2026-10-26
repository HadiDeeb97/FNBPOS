using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class CoveringIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The lines copy the product's fields, which already have these limits; anything longer from an old
            // version is shortened so the change below can't fail.
            migrationBuilder.Sql("""
                UPDATE [SaleLines] SET
                    [ProductName] = LEFT([ProductName], 200), [CategoryName] = LEFT([CategoryName], 100),
                    [VariantDescription] = LEFT([VariantDescription], 100), [Sku] = LEFT([Sku], 64)
                WHERE LEN([ProductName]) > 200 OR LEN([CategoryName]) > 100 OR LEN([VariantDescription]) > 100 OR LEN([Sku]) > 64;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Sales_Courier",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CustomerId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_IsActive_ProductId",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Payments_SaleId",
                table: "Payments");

            migrationBuilder.AlterColumn<string>(
                name: "VariantDescription",
                table: "SaleLines",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "SaleLines",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ProductName",
                table: "SaleLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "CategoryName",
                table: "SaleLines",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_OpenedAt",
                table: "Shifts",
                column: "OpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Status_ClosedAt",
                table: "Shifts",
                columns: new[] { "Status", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_Courier",
                table: "Sales",
                column: "Courier")
                .Annotation("SqlServer:Include", new[] { "CreatedAt", "Status", "DeliverySettlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales",
                column: "CreatedAt")
                .Annotation("SqlServer:Include", new[] { "Status", "Channel", "Total", "Subtotal", "DiscountTotal", "TaxTotal", "DeliveryFee", "UserId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CustomerId",
                table: "Sales",
                column: "CustomerId")
                .Annotation("SqlServer:Include", new[] { "Status", "Total", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId")
                .Annotation("SqlServer:Include", new[] { "Quantity", "ReturnedQuantity", "ProductVariantId", "UnitPrice", "UnitCost", "LineTotal", "TaxAmount", "DiscountAmount", "ProductName", "CategoryName" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_IsActive_ProductId",
                table: "ProductVariants",
                columns: new[] { "IsActive", "ProductId" })
                .Annotation("SqlServer:Include", new[] { "Sku", "Color", "Size", "Barcode", "StockQuantity", "ReorderLevel", "PriceOverride", "CostOverride" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SaleId",
                table: "Payments",
                column: "SaleId")
                .Annotation("SqlServer:Include", new[] { "Method", "Amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shifts_OpenedAt",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_Status_ClosedAt",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Sales_Courier",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CustomerId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_IsActive_ProductId",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Payments_SaleId",
                table: "Payments");

            migrationBuilder.AlterColumn<string>(
                name: "VariantDescription",
                table: "SaleLines",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "SaleLines",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "ProductName",
                table: "SaleLines",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "CategoryName",
                table: "SaleLines",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_Courier",
                table: "Sales",
                column: "Courier");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales",
                column: "CreatedAt")
                .Annotation("SqlServer:Include", new[] { "Status", "Channel", "Total" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CustomerId",
                table: "Sales",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId")
                .Annotation("SqlServer:Include", new[] { "Quantity" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_IsActive_ProductId",
                table: "ProductVariants",
                columns: new[] { "IsActive", "ProductId" })
                .Annotation("SqlServer:Include", new[] { "Sku", "Color", "Size", "Barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SaleId",
                table: "Payments",
                column: "SaleId");
        }
    }
}
