using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    security_stamp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    failed_logins = table.Column<int>(type: "INTEGER", nullable: false),
                    locked_until_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    last_login_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    password_changed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    timestamp_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    actor = table.Column<string>(type: "TEXT", nullable: false),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    details = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "backtest_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    parameters = table.Column<string>(type: "TEXT", nullable: false),
                    summary = table.Column<string>(type: "TEXT", nullable: false),
                    total_trades = table.Column<int>(type: "INTEGER", nullable: false),
                    net_pnl = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backtest_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "broker_accounts",
                columns: table => new
                {
                    account_key = table.Column<string>(type: "TEXT", nullable: false),
                    broker = table.Column<string>(type: "TEXT", nullable: false),
                    is_demo = table.Column<bool>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false),
                    starting_balance = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    last_balance = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_accounts", x => x.account_key);
                });

            migrationBuilder.CreateTable(
                name: "broker_connection_status",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    settings_version = table.Column<int>(type: "INTEGER", nullable: false),
                    connected_account_id = table.Column<string>(type: "TEXT", nullable: true),
                    accounts_json = table.Column<string>(type: "TEXT", nullable: true),
                    checked_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_connection_status", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "broker_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    deriv_app_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    deriv_api_token_protected = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    deriv_api_token_hint = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    deriv_account_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_by = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "candles",
                columns: table => new
                {
                    instrument = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    time_frame = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    open_time_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    open = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    high = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    low = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    close = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    spread = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    volume = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candles", x => new { x.instrument, x.time_frame, x.open_time_utc });
                });

            migrationBuilder.CreateTable(
                name: "close_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    position_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    instrument = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    requested_by = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    exit_price = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    realized_pnl = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_close_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_selection",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    instruments = table.Column<string>(type: "TEXT", nullable: false),
                    derived_only_when_forex_closed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    applied_version = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_by = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_selection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_snapshots",
                columns: table => new
                {
                    instrument = table.Column<string>(type: "TEXT", nullable: false),
                    market_time_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    bid = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    ask = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    spread_pips = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    regime = table.Column<string>(type: "TEXT", nullable: true),
                    last_decision = table.Column<string>(type: "TEXT", nullable: true),
                    last_decision_time_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    indicators = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_snapshots", x => x.instrument);
                });

            migrationBuilder.CreateTable(
                name: "markets",
                columns: table => new
                {
                    broker_symbol = table.Column<string>(type: "TEXT", nullable: false),
                    symbol = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    market = table.Column<string>(type: "TEXT", nullable: false),
                    submarket = table.Column<string>(type: "TEXT", nullable: false),
                    asset_class = table.Column<string>(type: "TEXT", nullable: false),
                    base_currency = table.Column<string>(type: "TEXT", nullable: false),
                    quote_currency = table.Column<string>(type: "TEXT", nullable: false),
                    pip_size = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    price_decimals = table.Column<int>(type: "INTEGER", nullable: false),
                    multipliers = table.Column<string>(type: "TEXT", nullable: false),
                    is_tradable = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_open = table.Column<bool>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_markets", x => x.broker_symbol);
                });

            migrationBuilder.CreateTable(
                name: "notification_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    smtp_host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    smtp_port = table.Column<int>(type: "INTEGER", nullable: false),
                    username = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    password_protected = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    password_hint = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    from_address = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    from_name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    to_addresses = table.Column<string>(type: "TEXT", nullable: false),
                    on_trade_opened = table.Column<bool>(type: "INTEGER", nullable: false),
                    on_trade_closed = table.Column<bool>(type: "INTEGER", nullable: false),
                    on_order_rejected = table.Column<bool>(type: "INTEGER", nullable: false),
                    on_kill_switch = table.Column<bool>(type: "INTEGER", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_by = table.Column<string>(type: "TEXT", nullable: true),
                    last_attempt_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_attempt_succeeded = table.Column<bool>(type: "INTEGER", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    client_order_id = table.Column<string>(type: "TEXT", nullable: false),
                    broker = table.Column<string>(type: "TEXT", nullable: false),
                    broker_account_id = table.Column<string>(type: "TEXT", nullable: true),
                    broker_contract_id = table.Column<string>(type: "TEXT", nullable: true),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    instrument = table.Column<string>(type: "TEXT", nullable: false),
                    direction = table.Column<string>(type: "TEXT", nullable: false),
                    units = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    requested_price = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    fill_price = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    stop_loss = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    take_profit = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    reject_reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    market_time_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "paper_accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: false),
                    balance = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    starting_balance = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    version = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paper_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    client_order_id = table.Column<string>(type: "TEXT", nullable: false),
                    broker = table.Column<string>(type: "TEXT", nullable: false),
                    broker_account_id = table.Column<string>(type: "TEXT", nullable: true),
                    broker_contract_id = table.Column<string>(type: "TEXT", nullable: true),
                    broker_stake = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    broker_multiplier = table.Column<int>(type: "INTEGER", nullable: true),
                    instrument = table.Column<string>(type: "TEXT", nullable: false),
                    direction = table.Column<string>(type: "TEXT", nullable: false),
                    units = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    entry_price = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    stop_loss = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    take_profit = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    initial_risk_amount = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    strategy = table.Column<string>(type: "TEXT", nullable: false),
                    score = table.Column<int>(type: "INTEGER", nullable: false),
                    is_open = table.Column<bool>(type: "INTEGER", nullable: false),
                    max_favorable_excursion = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    max_adverse_excursion = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    closed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    exit_price = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    exit_reason = table.Column<string>(type: "TEXT", nullable: true),
                    realized_pnl = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    commission = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    r_multiple = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    mae_pips = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    mfe_pips = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "risk_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    limits = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_by = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "setup_outcomes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    setup_id = table.Column<string>(type: "TEXT", nullable: false),
                    decision_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    instrument = table.Column<string>(type: "TEXT", nullable: false),
                    asset_class = table.Column<string>(type: "TEXT", nullable: false),
                    strategy = table.Column<string>(type: "TEXT", nullable: false),
                    regime = table.Column<string>(type: "TEXT", nullable: false),
                    direction = table.Column<string>(type: "TEXT", nullable: false),
                    decision_state = table.Column<string>(type: "TEXT", nullable: false),
                    score = table.Column<int>(type: "INTEGER", nullable: false),
                    entry = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    stop_loss = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    take_profit = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    r_multiple = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    exit_price = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setup_outcomes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    mode = table.Column<string>(type: "TEXT", nullable: false),
                    kill_switch_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    kill_switch_reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    kill_switch_changed_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    consecutive_losses = table.Column<int>(type: "INTEGER", nullable: false),
                    cooldown_until_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    pnl_day = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    daily_realized_pnl = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    derived_daily_realized_pnl = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    pnl_week_start = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    weekly_realized_pnl = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: false),
                    last_bar_time_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_data_received_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    worker_heartbeat_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    broker_name = table.Column<string>(type: "TEXT", nullable: true),
                    broker_account_id = table.Column<string>(type: "TEXT", nullable: true),
                    broker_is_demo = table.Column<bool>(type: "INTEGER", nullable: false),
                    market_data_source = table.Column<string>(type: "TEXT", nullable: true),
                    version = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_trades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    instrument = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    requested_by = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    close_requested_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    client_order_id = table.Column<string>(type: "TEXT", nullable: true),
                    position_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    fill_price = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    exit_price = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    realized_pnl = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    hold_seconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_trades", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trade_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    correlation_id = table.Column<string>(type: "TEXT", nullable: false),
                    instrument = table.Column<string>(type: "TEXT", nullable: false),
                    market_time_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    regime = table.Column<string>(type: "TEXT", nullable: false),
                    strategy = table.Column<string>(type: "TEXT", nullable: true),
                    direction = table.Column<string>(type: "TEXT", nullable: true),
                    score = table.Column<int>(type: "INTEGER", nullable: true),
                    entry = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    stop_loss = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    take_profit = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    reward_to_risk = table.Column<double>(type: "REAL", precision: 28, scale: 10, nullable: true),
                    client_order_id = table.Column<string>(type: "TEXT", nullable: true),
                    reasons = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    details = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_decisions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_users_username",
                table: "app_users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp_utc",
                table: "audit_logs",
                column: "timestamp_utc");

            migrationBuilder.CreateIndex(
                name: "ix_backtest_runs_created_at_utc",
                table: "backtest_runs",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_close_requests_position_id_status",
                table: "close_requests",
                columns: new[] { "position_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_markets_symbol",
                table: "markets",
                column: "symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_broker_contract_id",
                table: "orders",
                column: "broker_contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_client_order_id",
                table: "orders",
                column: "client_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_broker_contract_id",
                table: "positions",
                column: "broker_contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_client_order_id",
                table: "positions",
                column: "client_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_closed_at_utc",
                table: "positions",
                column: "closed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_positions_is_open",
                table: "positions",
                column: "is_open");

            migrationBuilder.CreateIndex(
                name: "ix_setup_outcomes_closed_at_utc",
                table: "setup_outcomes",
                column: "closed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_setup_outcomes_instrument_status",
                table: "setup_outcomes",
                columns: new[] { "instrument", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_setup_outcomes_setup_id",
                table: "setup_outcomes",
                column: "setup_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_test_trades_requested_at_utc",
                table: "test_trades",
                column: "requested_at_utc");

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
                name: "app_users");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "backtest_runs");

            migrationBuilder.DropTable(
                name: "broker_accounts");

            migrationBuilder.DropTable(
                name: "broker_connection_status");

            migrationBuilder.DropTable(
                name: "broker_settings");

            migrationBuilder.DropTable(
                name: "candles");

            migrationBuilder.DropTable(
                name: "close_requests");

            migrationBuilder.DropTable(
                name: "market_selection");

            migrationBuilder.DropTable(
                name: "market_snapshots");

            migrationBuilder.DropTable(
                name: "markets");

            migrationBuilder.DropTable(
                name: "notification_settings");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "paper_accounts");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropTable(
                name: "risk_settings");

            migrationBuilder.DropTable(
                name: "setup_outcomes");

            migrationBuilder.DropTable(
                name: "system_state");

            migrationBuilder.DropTable(
                name: "test_trades");

            migrationBuilder.DropTable(
                name: "trade_decisions");
        }
    }
}
