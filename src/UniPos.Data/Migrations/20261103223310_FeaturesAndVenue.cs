using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniPos.Data.Migrations
{
    /// <inheritdoc />
    public partial class FeaturesAndVenue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Venue",
                table: "Settings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FeatureSwitches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Feature = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TillId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Value = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureSwitches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureSwitches_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureSwitches_ChangedByUserId",
                table: "FeatureSwitches",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureSwitches_Feature_TillId",
                table: "FeatureSwitches",
                columns: new[] { "Feature", "TillId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureSwitches");

            migrationBuilder.DropColumn(
                name: "Venue",
                table: "Settings");
        }
    }
}
