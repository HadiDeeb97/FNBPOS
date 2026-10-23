using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClothingStore.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An earlier build shipped this migration as 20261006192415_AddDeliveryPayments (everything below except
            // DeliveryReference). Databases that ran it already have those columns, so each step only runs if needed.
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Sales', 'Courier') IS NULL
                    ALTER TABLE [Sales] ADD [Courier] nvarchar(100) NULL;
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Sales', 'DeliveryReference') IS NULL
                    ALTER TABLE [Sales] ADD [DeliveryReference] nvarchar(100) COLLATE Latin1_General_100_CI_AS NULL;
                """);
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.Sales', 'DeliverySettlementId') IS NULL
                    ALTER TABLE [Sales] ADD [DeliverySettlementId] int NULL;
                """);
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.DeliverySettlements', N'U') IS NULL
                BEGIN
                    CREATE TABLE [DeliverySettlements] (
                        [Id] int NOT NULL IDENTITY(1, 1),
                        [CreatedAt] datetime2 NOT NULL,
                        [UserId] int NOT NULL,
                        [ShiftId] int NULL,
                        [Courier] nvarchar(100) NULL,
                        [Method] int NOT NULL,
                        [Expected] decimal(18,2) NOT NULL,
                        [Received] decimal(18,2) NOT NULL,
                        [ReceivedLbp] decimal(18,2) NOT NULL,
                        [ExchangeRate] decimal(18,2) NOT NULL,
                        [Reference] nvarchar(300) NULL,
                        CONSTRAINT [PK_DeliverySettlements] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_DeliverySettlements_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
                    );
                END
                """);
            CreateIndexIfMissing(migrationBuilder, "Sales", "IX_Sales_Courier", "[Courier]");
            CreateIndexIfMissing(migrationBuilder, "Sales", "IX_Sales_DeliveryReference", "[DeliveryReference]");
            CreateIndexIfMissing(migrationBuilder, "Sales", "IX_Sales_DeliverySettlementId", "[DeliverySettlementId]");
            CreateIndexIfMissing(migrationBuilder, "DeliverySettlements", "IX_DeliverySettlements_CreatedAt", "[CreatedAt]");
            CreateIndexIfMissing(migrationBuilder, "DeliverySettlements", "IX_DeliverySettlements_UserId", "[UserId]");
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.FK_Sales_DeliverySettlements_DeliverySettlementId', N'F') IS NULL
                    ALTER TABLE [Sales] ADD CONSTRAINT [FK_Sales_DeliverySettlements_DeliverySettlementId]
                        FOREIGN KEY ([DeliverySettlementId]) REFERENCES [DeliverySettlements] ([Id]);
                """);
        }

        private static void CreateIndexIfMissing(MigrationBuilder migrationBuilder, string table, string index, string columns) =>
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{index}' AND object_id = OBJECT_ID(N'dbo.{table}'))
                    CREATE INDEX [{index}] ON [{table}] ({columns});
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_DeliverySettlements_DeliverySettlementId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "DeliverySettlements");

            migrationBuilder.DropIndex(
                name: "IX_Sales_Courier",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_DeliveryReference",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_DeliverySettlementId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Courier",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DeliveryReference",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DeliverySettlementId",
                table: "Sales");
        }
    }
}
