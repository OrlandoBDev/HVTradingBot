using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    smtp_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    smtp_port = table.Column<int>(type: "integer", nullable: false),
                    username = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    password_protected = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    password_hint = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    from_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    to_addresses = table.Column<string>(type: "jsonb", nullable: false),
                    on_trade_opened = table.Column<bool>(type: "boolean", nullable: false),
                    on_trade_closed = table.Column<bool>(type: "boolean", nullable: false),
                    on_order_rejected = table.Column<bool>(type: "boolean", nullable: false),
                    on_kill_switch = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    last_attempt_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_attempt_succeeded = table.Column<bool>(type: "boolean", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_settings", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_settings");
        }
    }
}
