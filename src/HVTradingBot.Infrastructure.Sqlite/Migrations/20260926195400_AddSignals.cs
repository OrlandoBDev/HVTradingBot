using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "signal_daily_realized_pnl",
                table: "system_state",
                type: "REAL",
                precision: 28,
                scale: 10,
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "signal_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    settings = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_by = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signal_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    setup_id = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    instrument = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    direction = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    strategy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    score = table.Column<int>(type: "INTEGER", nullable: false),
                    regime = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    entry = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    stop_loss = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    take_profit = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    checks = table.Column<string>(type: "TEXT", nullable: false),
                    risk_amount = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    accepted_rules = table.Column<string>(type: "TEXT", nullable: false),
                    decided_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    decided_by = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    position_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    fill_price = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    notified = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_signals_created_at_utc",
                table: "signals",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_signals_setup_id",
                table: "signals",
                column: "setup_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_signals_status_expires_at_utc",
                table: "signals",
                columns: new[] { "status", "expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signal_settings");

            migrationBuilder.DropTable(
                name: "signals");

            migrationBuilder.DropColumn(
                name: "signal_daily_realized_pnl",
                table: "system_state");
        }
    }
}
