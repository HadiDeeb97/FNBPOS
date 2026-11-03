using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrintNoteOnReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PrintNoteOnReceipt",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: true); // shops upgrading keep printing notes, as before
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrintNoteOnReceipt",
                table: "Settings");
        }
    }
}
