using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLebanesePounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CountedCashLbp",
                table: "Shifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedCashLbp",
                table: "Shifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningFloatLbp",
                table: "Shifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "LbpEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LbpRate",
                table: "Settings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 89500m);

            migrationBuilder.AddColumn<int>(
                name: "LbpRounding",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<decimal>(
                name: "CashTenderedLbp",
                table: "Sales",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ChangeGivenLbp",
                table: "Sales",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Sales",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Returns",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountLbp",
                table: "ReturnRefunds",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Currency",
                table: "CashMovements",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ExchangeRateChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OldRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NewRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangeRateChanges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRateChanges_ChangedAt",
                table: "ExchangeRateChanges",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRateChanges_UserId",
                table: "ExchangeRateChanges",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRateChanges");

            migrationBuilder.DropColumn(
                name: "CountedCashLbp",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ExpectedCashLbp",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "OpeningFloatLbp",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "LbpEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LbpRate",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LbpRounding",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CashTenderedLbp",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ChangeGivenLbp",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Returns");

            migrationBuilder.DropColumn(
                name: "AmountLbp",
                table: "ReturnRefunds");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "CashMovements");
        }
    }
}
