using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "setup_outcomes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    setup_id = table.Column<string>(type: "text", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    instrument = table.Column<string>(type: "text", nullable: false),
                    asset_class = table.Column<string>(type: "text", nullable: false),
                    strategy = table.Column<string>(type: "text", nullable: false),
                    regime = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    decision_state = table.Column<string>(type: "text", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    entry = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    stop_loss = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    take_profit = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    r_multiple = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    exit_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setup_outcomes", x => x.id);
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "setup_outcomes");
        }
    }
}
