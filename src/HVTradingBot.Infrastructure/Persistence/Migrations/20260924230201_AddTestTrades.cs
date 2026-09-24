using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "test_trades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requested_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    close_requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    client_order_id = table.Column<string>(type: "text", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fill_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    exit_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    realized_pnl = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    hold_seconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_trades", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_test_trades_requested_at_utc",
                table: "test_trades",
                column: "requested_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "test_trades");
        }
    }
}
