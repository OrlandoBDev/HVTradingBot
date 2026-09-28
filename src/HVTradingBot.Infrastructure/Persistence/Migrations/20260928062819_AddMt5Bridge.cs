using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMt5Bridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bridge_key_protected",
                table: "mt5_settings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "connection",
                table: "mt5_settings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "MetaApi"); // existing MT5 settings were MetaApi

            migrationBuilder.CreateTable(
                name: "mt5_bridge_commands",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payload = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    done_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    result_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mt5_bridge_commands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mt5_bridge_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ea_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    account_json = table.Column<string>(type: "text", nullable: false),
                    positions_json = table.Column<string>(type: "text", nullable: false),
                    deals_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mt5_bridge_state", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mt5_bridge_commands_status",
                table: "mt5_bridge_commands",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mt5_bridge_commands");

            migrationBuilder.DropTable(
                name: "mt5_bridge_state");

            migrationBuilder.DropColumn(
                name: "bridge_key_protected",
                table: "mt5_settings");

            migrationBuilder.DropColumn(
                name: "connection",
                table: "mt5_settings");
        }
    }
}
