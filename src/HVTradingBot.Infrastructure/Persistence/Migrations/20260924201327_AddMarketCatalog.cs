using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_selection",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    instruments = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    applied_version = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_selection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "markets",
                columns: table => new
                {
                    broker_symbol = table.Column<string>(type: "text", nullable: false),
                    symbol = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    market = table.Column<string>(type: "text", nullable: false),
                    submarket = table.Column<string>(type: "text", nullable: false),
                    asset_class = table.Column<string>(type: "text", nullable: false),
                    base_currency = table.Column<string>(type: "text", nullable: false),
                    quote_currency = table.Column<string>(type: "text", nullable: false),
                    pip_size = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    price_decimals = table.Column<int>(type: "integer", nullable: false),
                    multipliers = table.Column<string>(type: "jsonb", nullable: false),
                    is_tradable = table.Column<bool>(type: "boolean", nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_markets", x => x.broker_symbol);
                });

            migrationBuilder.CreateIndex(
                name: "ix_markets_symbol",
                table: "markets",
                column: "symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_selection");

            migrationBuilder.DropTable(
                name: "markets");
        }
    }
}
