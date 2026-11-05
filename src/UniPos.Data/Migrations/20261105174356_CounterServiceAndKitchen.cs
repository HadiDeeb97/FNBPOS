using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniPos.Data.Migrations
{
    /// <inheritdoc />
    public partial class CounterServiceAndKitchen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CallName",
                table: "Sales",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OrderDay",
                table: "Sales",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderNumber",
                table: "Sales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderType",
                table: "Sales",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "SaleLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StationId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Stations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    PrinterName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KitchenTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaleId = table.Column<int>(type: "int", nullable: false),
                    StationId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrderNumber = table.Column<int>(type: "int", nullable: true),
                    OrderType = table.Column<int>(type: "int", nullable: false),
                    CallName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PrintedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrintCount = table.Column<int>(type: "int", nullable: false),
                    PrintError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitchenTickets_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KitchenTickets_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KitchenTicketLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KitchenTicketId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Options = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenTicketLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitchenTicketLines_KitchenTickets_KitchenTicketId",
                        column: x => x.KitchenTicketId,
                        principalTable: "KitchenTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_OrderDay_OrderNumber",
                table: "Sales",
                columns: new[] { "OrderDay", "OrderNumber" },
                unique: true,
                filter: "[OrderNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StationId",
                table: "Products",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTicketLines_KitchenTicketId",
                table: "KitchenTicketLines",
                column: "KitchenTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTickets_CreatedAt",
                table: "KitchenTickets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTickets_PrintedAt",
                table: "KitchenTickets",
                column: "PrintedAt");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTickets_SaleId",
                table: "KitchenTickets",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTickets_StationId",
                table: "KitchenTickets",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_Name",
                table: "Stations",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Stations_StationId",
                table: "Products",
                column: "StationId",
                principalTable: "Stations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Stations_StationId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "KitchenTicketLines");

            migrationBuilder.DropTable(
                name: "KitchenTickets");

            migrationBuilder.DropTable(
                name: "Stations");

            migrationBuilder.DropIndex(
                name: "IX_Sales_OrderDay_OrderNumber",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Products_StationId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CallName",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "OrderDay",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "OrderType",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "StationId",
                table: "Products");
        }
    }
}
