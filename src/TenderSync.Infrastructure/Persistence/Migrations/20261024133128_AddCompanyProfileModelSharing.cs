using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyProfileModelSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "share_key_personnel_with_model",
                schema: "tendersync",
                table: "company_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "share_key_personnel_with_model",
                schema: "tendersync",
                table: "company_profiles");
        }
    }
}
