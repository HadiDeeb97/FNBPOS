using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class HistoryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines");

            migrationBuilder.DropIndex(
                name: "IX_Returns_SaleId",
                table: "Returns");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales",
                column: "CreatedAt")
                .Annotation("SqlServer:Include", new[] { "Status", "Channel", "Total" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId")
                .Annotation("SqlServer:Include", new[] { "Quantity" });

            migrationBuilder.CreateIndex(
                name: "IX_Returns_SaleId",
                table: "Returns",
                column: "SaleId")
                .Annotation("SqlServer:Include", new[] { "TotalRefund" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines");

            migrationBuilder.DropIndex(
                name: "IX_Returns_SaleId",
                table: "Returns");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CreatedAt",
                table: "Sales",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_SaleId",
                table: "SaleLines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Returns_SaleId",
                table: "Returns",
                column: "SaleId");
        }
    }
}
