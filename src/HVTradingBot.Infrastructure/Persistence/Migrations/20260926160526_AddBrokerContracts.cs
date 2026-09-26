using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broker_contracts",
                columns: table => new
                {
                    contract_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    broker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    broker_account_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    contract_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    buy_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    multiplier = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    entry_spot = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    current_spot = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    stop_loss = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    take_profit = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    profit = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    commission = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    purchase_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    sell_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    sell_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    exit_spot = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_contracts", x => x.contract_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broker_contracts_broker_account_id_is_open",
                table: "broker_contracts",
                columns: new[] { "broker_account_id", "is_open" });

            migrationBuilder.CreateIndex(
                name: "ix_broker_contracts_broker_account_id_sell_time_utc",
                table: "broker_contracts",
                columns: new[] { "broker_account_id", "sell_time_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broker_contracts");
        }
    }
}
