using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationNotBefore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "not_before",
                schema: "tendersync",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_not_before",
                schema: "tendersync",
                table: "notifications",
                columns: new[] { "status", "not_before" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_status_not_before",
                schema: "tendersync",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "not_before",
                schema: "tendersync",
                table: "notifications");
        }
    }
}
