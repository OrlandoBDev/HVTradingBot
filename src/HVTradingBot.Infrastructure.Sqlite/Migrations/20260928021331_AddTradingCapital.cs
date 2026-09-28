using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingCapital : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "capital_base",
                table: "system_state",
                type: "REAL",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "capital_pnl",
                table: "system_state",
                type: "REAL",
                precision: 28,
                scale: 10,
                nullable: false,
                defaultValue: 0.0);
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
