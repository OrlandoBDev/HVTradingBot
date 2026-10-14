using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingCapital : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "capital_base",
                table: "system_state",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "capital_pnl",
                table: "system_state",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "capital_base",
                table: "system_state");

            migrationBuilder.DropColumn(
                name: "capital_pnl",
                table: "system_state");
        }
    }
}
