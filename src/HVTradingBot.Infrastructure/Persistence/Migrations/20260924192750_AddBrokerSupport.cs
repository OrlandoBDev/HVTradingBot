using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_paper_positions",
                table: "paper_positions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_paper_orders",
                table: "paper_orders");

            migrationBuilder.RenameTable(
                name: "paper_positions",
                newName: "positions");

            migrationBuilder.RenameTable(
                name: "paper_orders",
                newName: "orders");

            migrationBuilder.RenameIndex(
                name: "ix_paper_positions_is_open",
                table: "positions",
                newName: "ix_positions_is_open");

            migrationBuilder.RenameIndex(
                name: "ix_paper_positions_closed_at_utc",
                table: "positions",
                newName: "ix_positions_closed_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_paper_positions_client_order_id",
                table: "positions",
                newName: "ix_positions_client_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_paper_orders_client_order_id",
                table: "orders",
                newName: "ix_orders_client_order_id");

            migrationBuilder.AddColumn<string>(
                name: "broker_account_id",
                table: "system_state",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "broker_is_demo",
                table: "system_state",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "broker_name",
                table: "system_state",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "market_data_source",
                table: "system_state",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "broker",
                table: "positions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "broker_account_id",
                table: "positions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "broker_contract_id",
                table: "positions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "broker_multiplier",
                table: "positions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "broker_stake",
                table: "positions",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "broker",
                table: "orders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "broker_account_id",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "broker_contract_id",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_positions",
                table: "positions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_orders",
                table: "orders",
                column: "id");

            migrationBuilder.CreateTable(
                name: "broker_accounts",
                columns: table => new
                {
                    account_key = table.Column<string>(type: "text", nullable: false),
                    broker = table.Column<string>(type: "text", nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    starting_balance = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    last_balance = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_accounts", x => x.account_key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_positions_broker_contract_id",
                table: "positions",
                column: "broker_contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_broker_contract_id",
                table: "orders",
                column: "broker_contract_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broker_accounts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_positions",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_broker_contract_id",
                table: "positions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_orders",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_broker_contract_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "broker_account_id",
                table: "system_state");

            migrationBuilder.DropColumn(
                name: "broker_is_demo",
                table: "system_state");

            migrationBuilder.DropColumn(
                name: "broker_name",
                table: "system_state");

            migrationBuilder.DropColumn(
                name: "market_data_source",
                table: "system_state");

            migrationBuilder.DropColumn(
                name: "broker",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "broker_account_id",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "broker_contract_id",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "broker_multiplier",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "broker_stake",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "broker",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "broker_account_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "broker_contract_id",
                table: "orders");

            migrationBuilder.RenameTable(
                name: "positions",
                newName: "paper_positions");

            migrationBuilder.RenameTable(
                name: "orders",
                newName: "paper_orders");

            migrationBuilder.RenameIndex(
                name: "ix_positions_is_open",
                table: "paper_positions",
                newName: "ix_paper_positions_is_open");

            migrationBuilder.RenameIndex(
                name: "ix_positions_closed_at_utc",
                table: "paper_positions",
                newName: "ix_paper_positions_closed_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_positions_client_order_id",
                table: "paper_positions",
                newName: "ix_paper_positions_client_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_orders_client_order_id",
                table: "paper_orders",
                newName: "ix_paper_orders_client_order_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_paper_positions",
                table: "paper_positions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_paper_orders",
                table: "paper_orders",
                column: "id");
        }
    }
}
