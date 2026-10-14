using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMt5Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mt5_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    token_protected = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    token_hint = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    account_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    region = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    symbol_suffix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    commission_percent = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_by = table.Column<string>(type: "TEXT", nullable: true),
                    status_state = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    status_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    status_version = table.Column<int>(type: "INTEGER", nullable: false),
                    status_login = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    status_server = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    status_broker = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    status_is_demo = table.Column<bool>(type: "INTEGER", nullable: true),
                    status_balance = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    status_currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    status_checked_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mt5_settings", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mt5_settings");
        }
    }
}
