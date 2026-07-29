using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketReplyEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ticket_email_outbox_message_kind",
                table: "TicketEmailOutboxes");

            migrationBuilder.AddColumn<string>(
                name: "Body",
                table: "TicketEmailOutboxes",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_ticket_email_outbox_message_kind",
                table: "TicketEmailOutboxes",
                columns: new[] { "ContactMessageId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_ticket_email_outbox_message_kind",
                table: "TicketEmailOutboxes");

            migrationBuilder.DropColumn(
                name: "Body",
                table: "TicketEmailOutboxes");

            migrationBuilder.CreateIndex(
                name: "ux_ticket_email_outbox_message_kind",
                table: "TicketEmailOutboxes",
                columns: new[] { "ContactMessageId", "Kind" },
                unique: true);
        }
    }
}
