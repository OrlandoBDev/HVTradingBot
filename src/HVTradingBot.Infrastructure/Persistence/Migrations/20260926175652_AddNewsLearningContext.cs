using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVTradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsLearningContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "news_condition",
                table: "setup_outcomes",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trend_alignment",
                table: "setup_outcomes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "news_condition",
                table: "setup_outcomes");

            migrationBuilder.DropColumn(
                name: "trend_alignment",
                table: "setup_outcomes");
        }
    }
}
