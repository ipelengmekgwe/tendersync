using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenderSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "summary",
                schema: "tendersync",
                table: "tenders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "summary",
                schema: "tendersync",
                table: "tenders");
        }
    }
}
