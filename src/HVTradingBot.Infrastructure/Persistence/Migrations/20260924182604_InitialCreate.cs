using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "backtest_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    summary = table.Column<string>(type: "jsonb", nullable: false),
                    total_trades = table.Column<int>(type: "integer", nullable: false),
                    net_pnl = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backtest_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "candles",
                columns: table => new
                {
                    instrument = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    time_frame = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    open_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    open = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    high = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    low = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    close = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    spread = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candles", x => new { x.instrument, x.time_frame, x.open_time_utc });
                });

            migrationBuilder.CreateTable(
                name: "market_snapshots",
                columns: table => new
                {
                    instrument = table.Column<string>(type: "text", nullable: false),
                    market_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    bid = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    ask = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    spread_pips = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    regime = table.Column<string>(type: "text", nullable: true),
                    last_decision = table.Column<string>(type: "text", nullable: true),
                    last_decision_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    indicators = table.Column<string>(type: "jsonb", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_snapshots", x => x.instrument);
                });

            migrationBuilder.CreateTable(
                name: "paper_accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    starting_balance = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paper_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "paper_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_order_id = table.Column<string>(type: "text", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    instrument = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    units = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    requested_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    fill_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    stop_loss = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    take_profit = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reject_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    market_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paper_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "paper_positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_order_id = table.Column<string>(type: "text", nullable: false),
                    instrument = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    units = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    stop_loss = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    take_profit = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    initial_risk_amount = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    strategy = table.Column<string>(type: "text", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    max_favorable_excursion = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    max_adverse_excursion = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    exit_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    exit_reason = table.Column<string>(type: "text", nullable: true),
                    realized_pnl = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    r_multiple = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    mae_pips = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    mfe_pips = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paper_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    kill_switch_active = table.Column<bool>(type: "boolean", nullable: false),
                    kill_switch_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    kill_switch_changed_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    consecutive_losses = table.Column<int>(type: "integer", nullable: false),
                    cooldown_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pnl_day = table.Column<DateOnly>(type: "date", nullable: true),
                    daily_realized_pnl = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    pnl_week_start = table.Column<DateOnly>(type: "date", nullable: true),
                    weekly_realized_pnl = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    last_bar_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_data_received_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    worker_heartbeat_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trade_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    instrument = table.Column<string>(type: "text", nullable: false),
                    market_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    regime = table.Column<string>(type: "text", nullable: false),
                    strategy = table.Column<string>(type: "text", nullable: true),
                    direction = table.Column<string>(type: "text", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: true),
                    entry = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    stop_loss = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    take_profit = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    reward_to_risk = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    client_order_id = table.Column<string>(type: "text", nullable: true),
                    reasons = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_decisions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp_utc",
                table: "audit_logs",
                column: "timestamp_utc");

            migrationBuilder.CreateIndex(
                name: "ix_backtest_runs_created_at_utc",
                table: "backtest_runs",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_paper_orders_client_order_id",
                table: "paper_orders",
                column: "client_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_paper_positions_client_order_id",
                table: "paper_positions",
                column: "client_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_paper_positions_closed_at_utc",
                table: "paper_positions",
                column: "closed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_paper_positions_is_open",
                table: "paper_positions",
                column: "is_open");

            migrationBuilder.CreateIndex(
                name: "ix_trade_decisions_client_order_id",
                table: "trade_decisions",
                column: "client_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_trade_decisions_instrument_market_time_utc",
                table: "trade_decisions",
                columns: new[] { "instrument", "market_time_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_trade_decisions_market_time_utc",
                table: "trade_decisions",
                column: "market_time_utc");

            migrationBuilder.CreateIndex(
                name: "ix_trade_decisions_state",
                table: "trade_decisions",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "backtest_runs");

            migrationBuilder.DropTable(
                name: "candles");

            migrationBuilder.DropTable(
                name: "market_snapshots");

            migrationBuilder.DropTable(
                name: "paper_accounts");

            migrationBuilder.DropTable(
                name: "paper_orders");

            migrationBuilder.DropTable(
                name: "paper_positions");

            migrationBuilder.DropTable(
                name: "system_state");

            migrationBuilder.DropTable(
                name: "trade_decisions");
        }
    }
}
