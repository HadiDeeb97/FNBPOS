using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShiftsPerTill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClosedByUserId",
                table: "Shifts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentUserId",
                table: "Shifts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TillId",
                table: "Shifts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TillName",
                table: "Shifts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShiftHandovers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromUserId = table.Column<int>(type: "int", nullable: false),
                    ToUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftHandovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftHandovers_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftHandovers_Users_FromUserId",
                        column: x => x.FromUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftHandovers_Users_ToUserId",
                        column: x => x.ToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_ClosedByUserId",
                table: "Shifts",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_CurrentUserId",
                table: "Shifts",
                column: "CurrentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_TillId_Status",
                table: "Shifts",
                columns: new[] { "TillId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftHandovers_FromUserId",
                table: "ShiftHandovers",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftHandovers_ShiftId",
                table: "ShiftHandovers",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftHandovers_ToUserId",
                table: "ShiftHandovers",
                column: "ToUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Users_ClosedByUserId",
                table: "Shifts",
                column: "ClosedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shifts_Users_CurrentUserId",
                table: "Shifts",
                column: "CurrentUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Shifts from before: run by whoever opened them.
            migrationBuilder.Sql("UPDATE Shifts SET CurrentUserId = UserId WHERE CurrentUserId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Users_ClosedByUserId",
                table: "Shifts");

            migrationBuilder.DropForeignKey(
                name: "FK_Shifts_Users_CurrentUserId",
                table: "Shifts");

            migrationBuilder.DropTable(
                name: "ShiftHandovers");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_ClosedByUserId",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_CurrentUserId",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_TillId_Status",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "CurrentUserId",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "TillId",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "TillName",
                table: "Shifts");
        }
    }
}
