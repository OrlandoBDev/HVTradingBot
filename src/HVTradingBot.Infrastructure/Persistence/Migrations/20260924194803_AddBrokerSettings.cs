using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broker_connection_status",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    settings_version = table.Column<int>(type: "integer", nullable: false),
                    connected_account_id = table.Column<string>(type: "text", nullable: true),
                    accounts_json = table.Column<string>(type: "jsonb", nullable: true),
                    checked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_connection_status", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "broker_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    deriv_app_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    deriv_api_token_protected = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    deriv_api_token_hint = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    deriv_account_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_settings", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broker_connection_status");

            migrationBuilder.DropTable(
                name: "broker_settings");
        }
    }
}
