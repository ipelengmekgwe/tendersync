using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationRecipientUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recipient_user_id",
                schema: "tendersync",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_user_id_created_at",
                schema: "tendersync",
                table: "notifications",
                columns: new[] { "recipient_user_id", "created_at" });

            // Existing rows: give each to the one user whose preference holds that channel and address. Rows whose address
            // several users entered stay unassigned (listed to nobody) rather than shown to the wrong person.
            migrationBuilder.Sql("""
                update tendersync.notifications n
                set recipient_user_id = owner.user_id
                from (
                    select channel, lower(address) as address, (array_agg(user_id))[1] as user_id
                    from tendersync.notification_preferences
                    group by channel, lower(address)
                    having count(distinct user_id) = 1
                ) owner
                where n.recipient_user_id is null
                  and n.channel = owner.channel
                  and lower(n.recipient) = owner.address;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_recipient_user_id_created_at",
                schema: "tendersync",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "recipient_user_id",
                schema: "tendersync",
                table: "notifications");
        }
    }
}
