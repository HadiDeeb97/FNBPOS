using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniPos.Data.Migrations
{
    /// <inheritdoc />
    public partial class MenuAndModifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No UniPOS shop ever had size/colour variants: only a development database made before menus can, with the
            // clothing demo catalogue. Its sales point at variants that are about to go, so stop with a clear message.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [ProductVariants])
                    THROW 50000, 'This database still holds clothing items with sizes and colours from before menus. Delete it and start the POS again to create a new one.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_ProductVariants_ProductVariantId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleLines_ProductVariants_ProductVariantId",
                table: "SaleLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_ProductVariants_ProductVariantId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines");

            migrationBuilder.DropIndex(
                name: "IX_Products_IsActive",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LabelBarcode",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "VariantDescription",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Material",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Season",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StyleCode",
                table: "Products");

            migrationBuilder.RenameColumn(
                name: "ProductVariantId",
                table: "StockMovements",
                newName: "ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_StockMovements_ProductVariantId_CreatedAt",
                table: "StockMovements",
                newName: "IX_StockMovements_ProductId_CreatedAt");

            migrationBuilder.RenameColumn(
                name: "ProductVariantId",
                table: "SaleLines",
                newName: "ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_SaleLines_ProductVariantId",
                table: "SaleLines",
                newName: "IX_SaleLines_ProductId");

            migrationBuilder.RenameColumn(
                name: "ProductVariantId",
                table: "PurchaseOrderLines",
                newName: "ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrderLines_ProductVariantId",
                table: "PurchaseOrderLines",
                newName: "IX_PurchaseOrderLines_ProductId");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "Products");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Options",
                table: "SaleLines",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Products",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReorderLevel",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "Products",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                collation: "Latin1_General_100_CI_AS");

            migrationBuilder.AddColumn<bool>(
                name: "SoldOut",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TrackStock",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ModifierGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    MinChoices = table.Column<int>(type: "int", nullable: false),
                    MaxChoices = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModifierGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Modifiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModifierGroupId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SoldOut = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Modifiers_ModifierGroups_ModifierGroupId",
                        column: x => x.ModifierGroupId,
                        principalTable: "ModifierGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductModifierGroups",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ModifierGroupId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductModifierGroups", x => new { x.ProductId, x.ModifierGroupId });
                    table.ForeignKey(
                        name: "FK_ProductModifierGroups_ModifierGroups_ModifierGroupId",
                        column: x => x.ModifierGroupId,
                        principalTable: "ModifierGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductModifierGroups_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleLineModifiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaleLineId = table.Column<int>(type: "int", nullable: false),
                    ModifierId = table.Column<int>(type: "int", nullable: true),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLineModifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleLineModifiers_Modifiers_ModifierId",
                        column: x => x.ModifierId,
                        principalTable: "Modifiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SaleLineModifiers_SaleLines_SaleLineId",
                        column: x => x.SaleLineId,
                        principalTable: "SaleLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId")
                .Annotation("SqlServer:Include", new[] { "Quantity", "ReturnedQuantity", "ProductId", "UnitPrice", "UnitCost", "LineTotal", "TaxAmount", "DiscountAmount", "ProductName", "CategoryName" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Barcode",
                table: "Products",
                column: "Barcode",
                unique: true,
                filter: "[Barcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_IsActive",
                table: "Products",
                column: "IsActive")
                .Annotation("SqlServer:Include", new[] { "Name", "Sku", "Barcode", "CategoryId", "Price", "SoldOut", "TrackStock", "StockQuantity", "ReorderLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModifierGroups_Name",
                table: "ModifierGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Modifiers_ModifierGroupId",
                table: "Modifiers",
                column: "ModifierGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductModifierGroups_ModifierGroupId",
                table: "ProductModifierGroups",
                column: "ModifierGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLineModifiers_ModifierId",
                table: "SaleLineModifiers",
                column: "ModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLineModifiers_SaleLineId",
                table: "SaleLineModifiers",
                column: "SaleLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_Products_ProductId",
                table: "PurchaseOrderLines",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleLines_Products_ProductId",
                table: "SaleLines",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Products_ProductId",
                table: "StockMovements",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_Products_ProductId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleLines_Products_ProductId",
                table: "SaleLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Products_ProductId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "ProductModifierGroups");

            migrationBuilder.DropTable(
                name: "SaleLineModifiers");

            migrationBuilder.DropTable(
                name: "Modifiers");

            migrationBuilder.DropTable(
                name: "ModifierGroups");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines");

            migrationBuilder.DropIndex(
                name: "IX_Products_Barcode",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_IsActive",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Sku",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Options",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReorderLevel",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SoldOut",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TrackStock",
                table: "Products");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "StockMovements",
                newName: "ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_StockMovements_ProductId_CreatedAt",
                table: "StockMovements",
                newName: "IX_StockMovements_ProductVariantId_CreatedAt");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "SaleLines",
                newName: "ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_SaleLines_ProductId",
                table: "SaleLines",
                newName: "IX_SaleLines_ProductVariantId");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "PurchaseOrderLines",
                newName: "ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseOrderLines_ProductId",
                table: "PurchaseOrderLines",
                newName: "IX_PurchaseOrderLines_ProductVariantId");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Products");

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LabelBarcode",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VariantDescription",
                table: "SaleLines",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Material",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Season",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StyleCode",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CostOverride = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PriceOverride = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ReorderLevel = table.Column<int>(type: "int", nullable: false),
                    Size = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    StockQuantity = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId")
                .Annotation("SqlServer:Include", new[] { "Quantity", "ReturnedQuantity", "ProductVariantId", "UnitPrice", "UnitCost", "LineTotal", "TaxAmount", "DiscountAmount", "ProductName", "CategoryName" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_IsActive",
                table: "Products",
                column: "IsActive")
                .Annotation("SqlServer:Include", new[] { "Name", "Brand", "StyleCode", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Barcode",
                table: "ProductVariants",
                column: "Barcode",
                unique: true,
                filter: "[Barcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_IsActive_ProductId",
                table: "ProductVariants",
                columns: new[] { "IsActive", "ProductId" })
                .Annotation("SqlServer:Include", new[] { "Sku", "Color", "Size", "Barcode", "StockQuantity", "ReorderLevel", "PriceOverride", "CostOverride" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Sku",
                table: "ProductVariants",
                column: "Sku",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_ProductVariants_ProductVariantId",
                table: "PurchaseOrderLines",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleLines_ProductVariants_ProductVariantId",
                table: "SaleLines",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_ProductVariants_ProductVariantId",
                table: "StockMovements",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
