using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class SearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_IsActive_ProductId",
                table: "ProductVariants",
                columns: new[] { "IsActive", "ProductId" })
                .Annotation("SqlServer:Include", new[] { "Sku", "Color", "Size", "Barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_IsActive",
                table: "Products",
                column: "IsActive")
                .Annotation("SqlServer:Include", new[] { "Name", "Brand", "StyleCode", "CategoryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_IsActive_ProductId",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Products_IsActive",
                table: "Products");
        }
    }
}
